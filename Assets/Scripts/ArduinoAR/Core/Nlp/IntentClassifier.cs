using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArduinoAR
{
    public enum QueryKind
    {
        /// <summary>"Which pins support PWM?" - list the pins for a topic.</summary>
        List,
        /// <summary>"What is PWM?" - explain a concept (plus the board's pins for it).</summary>
        Explain,
        /// <summary>"Does pin 9 support PWM?" - yes/no for specific pins and a topic.</summary>
        CapabilityCheck,
        /// <summary>"What does pin 13 do?" - everything known about specific pins.</summary>
        PinLookup,
        /// <summary>"Which microcontroller is on this board?"</summary>
        BoardInfo,
        /// <summary>"How do I connect an I2C sensor?" - wiring steps with pin roles (I2C only).</summary>
        Connect,
        /// <summary>"How is the SPDUINO different from the Uno?" - attribute-by-attribute comparison.</summary>
        Compare,
        /// <summary>Recognised but not answerable from verified knowledge (e.g. current ratings).</summary>
        Unsupported,
        Unknown,
    }

    public sealed class ClassifiedIntent
    {
        public string Topic;                    // Topics id or null
        public readonly List<string> TiedTopics = new();
        public QueryKind Kind = QueryKind.Unknown;
        /// <summary>True when the wording itself says list/explain/check, so a follow-up must not override it.</summary>
        public bool KindExplicit;
        public int TopicScore;
        public float Confidence;
        public string UnsupportedReason;
        /// <summary>Only pins with this capability (e.g. just SDA for "which one is SDA?"), or null.</summary>
        public string CapabilityFilter;
        /// <summary>Wiring was asked for a topic without step-by-step guidance (anything but I2C).</summary>
        public bool ConnectWithoutGuide;
        /// <summary>Built-in feature asked about (BoardInfo), e.g. "WiFi".</summary>
        public string AskedFeature;
        /// <summary>Circuit wiring guide (see <see cref="CircuitGuides"/>) for Connect questions about a component, or null.</summary>
        public string CircuitId;
        /// <summary>A follow-up within a circuit ("Why do I need a resistor?").</summary>
        public bool CircuitFollowUp;

        public string Id => Kind switch
        {
            QueryKind.Connect when CircuitId != null => CircuitFollowUp ? $"circuit.{CircuitId}.followup" : $"circuit.{CircuitId}",
            QueryKind.PinLookup => "pin.lookup",
            QueryKind.BoardInfo => "board.info",
            QueryKind.Compare => "board.compare",
            QueryKind.Unsupported => "unsupported",
            QueryKind.Unknown => "unknown",
            _ => $"{Topic}.{Kind.ToString().ToLowerInvariant()}",
        };
    }

    /// <summary>
    /// Rule-based intent classifier: weighted topic keywords + question-type cues + entities.
    /// Deterministic; confidence reflects how strong the cues were.
    /// </summary>
    public static class IntentClassifier
    {
        static readonly Regex s_ExplainStrong = new(@"\b(explain|meaning|mean|means|define|definition|how does|how do i|how to|why|purpose|tell me about|works?|concept|difference)\b");
        static readonly Regex s_ExplainWeak = new(@"\b(what is|what are)\b");
        static readonly Regex s_ListStrong = new(@"\b(which|where|list|show|locate|find)\b");
        static readonly Regex s_PinsWord = new(@"\bpins\b");
        static readonly Regex s_CurrentRating = new(@"\b(current|amps?|amperage|milliamps?|ma|power rating|max(?:imum)? load)\b");
        // Rating/tolerance questions: explicit "safe/tolerate/max ... NV", or applying a voltage other than
        // the board's own 5V/3.3V rails to a pin.
        static readonly Regex s_VoltageTolerance = new(
            @"\b(safe|tolerate|tolerant|handle|maximum|max|withstand)\b.*\b\d{1,2}(?:\.\d)?v\b" +
            @"|\b(?!5v\b|3\.3v\b)\d{1,2}(?:\.\d)?v\b.*\b(?:to|into|in|on)\s+(?:the\s+)?(?:vin|5v|3\.3v|aref|pin|pins|analog|digital|a\d|d\d+)\b");
        // Limit questions without a number: "What's the maximum voltage for the analog pins?", "how many volts can A0 take?"
        static readonly Regex s_VoltageLimit = new(
            @"\b(?:max|maximum|highest|safe|safely|tolerate|tolerates|tolerance|withstand|limit|limits|rated|rating)\b.*\bvoltages?\b" +
            @"|\bvoltages?\b.*\b(?:max|maximum|highest|limit|limits|tolerance|tolerate|safe|rating)\b|\bhow many volts\b");

        static readonly Regex s_PinChoice = new(@"\b(?:where|which pins?|what pins?) (?:can|could|should) i\b");

        // Input-voltage questions are ratings too ("input voltage range", "what voltage can VIN take").
        static readonly Regex s_InputVoltage = new(@"\b(?:input|supply|recommended)\s+voltage\b|\bvoltage\s+(?:range|limits?)\b|\bwhat\s+voltage\s+can\b");

        public static ClassifiedIntent Classify(string normalized, ExtractedEntities entities)
        {
            var intent = new ClassifiedIntent();

            // Topic scoring (all topics tied for the best score are kept). Pin references are excluded, so
            // "digital pin 3" or "the VIN pin" name a pin rather than ask about the digital/power topic.
            string topicText = string.IsNullOrEmpty(entities.TopicText) && entities.PinIds.Count == 0 ? normalized : entities.TopicText;
            var scored = Topics.All
                .Select(t => (t, score: t.Keywords.Where(k => TextAnalysis.ContainsPhrase(topicText, k.keyword)).Sum(k => k.weight)))
                .Where(s => s.score > 0).OrderByDescending(s => s.score).ToList();
            if (scored.Count > 0)
            {
                intent.TopicScore = scored[0].score;
                foreach (var s in scored.Where(s => s.score == intent.TopicScore)) intent.TiedTopics.Add(s.t.Id);
                intent.Topic = intent.TiedTopics[0];
            }

            int explain = (s_ExplainStrong.IsMatch(normalized) ? 2 : 0) + (s_ExplainWeak.IsMatch(normalized) ? 1 : 0);
            int list = (s_ListStrong.IsMatch(normalized) ? 2 : 0) + (s_PinsWord.IsMatch(normalized) ? 1 : 0);
            intent.KindExplicit = explain > 0 || list > 0;

            // Ratings we have no verified data for: answer honestly instead of guessing.
            if (s_CurrentRating.IsMatch(normalized) && !normalized.Contains("current pin"))
            {
                intent.Kind = QueryKind.Unsupported;
                intent.UnsupportedReason = "current rating";
                intent.Confidence = 0.8f;
                return intent;
            }
            if (s_VoltageTolerance.IsMatch(normalized) || s_InputVoltage.IsMatch(normalized) || s_VoltageLimit.IsMatch(normalized))
            {
                intent.Kind = QueryKind.Unsupported;
                intent.UnsupportedReason = "voltage rating";
                intent.Confidence = 0.8f;
                return intent;
            }

            // Two boards named, or a comparison word plus another board ("compare this board with the Uno").
            int boards = entities.BoardMentions.Count + (entities.MentionsGenericArduino && entities.HasCompareCue && entities.BoardMentions.Count == 1 ? 1 : 0);
            if (boards >= 2 || (entities.HasCompareCue && boards >= 1))
            {
                SetBoardLevel(intent, QueryKind.Compare, 0.85f);
                return intent;
            }

            // "Does this board have WiFi?" - answered from the documented feature list, never with pins.
            if (entities.AskedFeature != null)
            {
                SetBoardLevel(intent, QueryKind.BoardInfo, 0.8f);
                intent.AskedFeature = entities.AskedFeature;
                return intent;
            }

            // Wiring a component ("How do I connect an LED?", "Connect a button to pin 7"): a circuit guide.
            // "Where can I connect a potentiometer?" asks which pins are suitable: the analog-input list answers it.
            bool asksWhichPins = s_PinChoice.IsMatch(normalized) && entities.PinIds.Count == 0
                                 && entities.Component is CircuitGuides.Potentiometer or CircuitGuides.Ldr;
            if (entities.HasConnectCue && entities.Component != null && !asksWhichPins)
            {
                intent.Kind = QueryKind.Connect;
                intent.CircuitId = entities.Component;
                intent.KindExplicit = true;
                intent.Confidence = 0.85f;
                return intent;
            }

            bool hasPins = entities.PinIds.Count > 0;
            if (intent.Topic == Topics.I2c && entities.RoleCapability != null) intent.CapabilityFilter = entities.RoleCapability;

            // Wiring questions: step-by-step guidance exists for I2C modules; other topics keep their usual
            // answer with a note. Questions about specific pins stay pin questions.
            if (entities.HasConnectCue && !hasPins && intent.Topic != null && intent.Topic != Topics.Board)
            {
                if (intent.Topic == Topics.I2c && intent.CapabilityFilter == null)
                {
                    intent.Kind = QueryKind.Connect;
                    intent.KindExplicit = true;
                    intent.Confidence = intent.TopicScore >= 4 ? 0.9f : 0.75f;
                    return intent;
                }
                if (intent.Topic != Topics.I2c) intent.ConnectWithoutGuide = true;
            }

            if (intent.Topic == Topics.Board)
                intent.Kind = QueryKind.BoardInfo;
            else if (hasPins && intent.Topic != null)
                intent.Kind = QueryKind.CapabilityCheck;
            else if (hasPins)
                intent.Kind = QueryKind.PinLookup;
            else if (intent.Topic != null)
                intent.Kind = explain > list ? QueryKind.Explain : QueryKind.List;
            else
                intent.Kind = QueryKind.Unknown;

            intent.Confidence = intent.Kind switch
            {
                QueryKind.Unknown => 0f,
                QueryKind.PinLookup => 0.95f,
                _ => (hasPins ? 0.95f : intent.TopicScore >= 5 ? 0.9f : intent.TopicScore == 4 ? 0.8f : intent.TopicScore == 3 ? 0.7f : 0.55f)
                     - (intent.TiedTopics.Count > 1 ? 0.15f : 0f),
            };
            return intent;
        }

        static void SetBoardLevel(ClassifiedIntent intent, QueryKind kind, float confidence)
        {
            intent.Topic = Topics.Board;
            intent.TiedTopics.Clear();
            intent.TiedTopics.Add(Topics.Board);
            intent.Kind = kind;
            intent.KindExplicit = true;
            intent.Confidence = confidence;
        }

        /// <summary>Kind implied by wording alone (used when a follow-up inherits the previous topic).</summary>
        public static QueryKind KindFromWording(string normalized) =>
            (s_ExplainStrong.IsMatch(normalized) ? 2 : 0) + (s_ExplainWeak.IsMatch(normalized) ? 1 : 0)
            > (s_ListStrong.IsMatch(normalized) ? 2 : 0) + (s_PinsWord.IsMatch(normalized) ? 1 : 0)
                ? QueryKind.Explain : QueryKind.List;

        public static bool HasExplainWording(string normalized) => s_ExplainStrong.IsMatch(normalized) || s_ExplainWeak.IsMatch(normalized);
    }

    /// <summary>Conversation memory for follow-up questions. One instance per board/session.</summary>
    public sealed class DialogueContext
    {
        public string BoardId;
        public string LastTopic;
        public QueryKind LastKind = QueryKind.Unknown;
        public readonly List<string> LastAskedPins = new();
        public readonly List<string> LastHighlightedPins = new();
        public int Turns;
        /// <summary>Circuit whose wiring was last explained (see <see cref="CircuitGuides"/>), for follow-ups; kept until another circuit is explained.</summary>
        public string ActiveCircuit;
        /// <summary>Its main pin when the user chose one ("LED on pin 6"); null for the circuit's default pin.</summary>
        public string ActiveCircuitPin;

        public bool HasPrevious(BoardProfile board) =>
            Turns > 0 && board != null && BoardId == board.Id && LastKind != QueryKind.Unknown;

        public void Reset()
        {
            BoardId = null; LastTopic = null; LastKind = QueryKind.Unknown;
            LastAskedPins.Clear(); LastHighlightedPins.Clear(); Turns = 0;
            ActiveCircuit = null; ActiveCircuitPin = null;
        }
    }
}
