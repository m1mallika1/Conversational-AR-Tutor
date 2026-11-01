using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ArduinoAR
{
    /// <summary>Structured output of the NLP pipeline for one question.</summary>
    public sealed class AssistantResponse
    {
        public string Question;
        public string NormalizedQuestion;
        public string IntentId;                  // e.g. "pwm.list", "pin.lookup", "unknown"
        public string IntentTitle;
        public QueryKind Kind;
        public string Topic;
        public ExtractedEntities Entities;
        /// <summary>Pins the question was resolved to (explicit, or taken from the previous turn).</summary>
        public readonly List<string> ResolvedPinIds = new();
        public string BoardId;
        public string BoardName;
        public readonly List<KnowledgeFact> RetrievedFacts = new();
        public string Answer;
        public readonly List<string> Evidence = new();
        public readonly List<PinRecord> HighlightPins = new();
        public float Confidence;
        public string ConfidenceLabel;
        /// <summary>Plain statement of what is uncertain (unverified facts, weak match, missing context).</summary>
        public string Uncertainty;
        public readonly List<string> Caveats = new();
        public bool UsedContext;
        /// <summary>Wiring role per highlighted pin ("SDA", "SCL", "Power", "GND"); empty for ordinary highlights.</summary>
        public readonly Dictionary<string, string> PinRoles = new();
        /// <summary>True when the question named a board other than the one in view: answered for that board, nothing highlighted.</summary>
        public bool AboutOtherBoard;
        /// <summary>Display-only note under the answer (e.g. the highlight colour key); never spoken.</summary>
        public string Legend;
        public bool IsUnknown => Kind == QueryKind.Unknown && RetrievedFacts.Count == 0;

        public IEnumerable<string> HighlightIds => HighlightPins.Select(p => p.Id);

        /// <summary>Answer, evidence, confidence and caveats as one display string.</summary>
        public string ToDisplayText(int maxEvidence = 3)
        {
            var sb = new StringBuilder(Answer);
            if (!string.IsNullOrEmpty(Legend)) sb.Append('\n').Append(Legend);
            if (Evidence.Count > 0)
            {
                sb.Append("\nEvidence:");
                foreach (var e in Evidence.Take(maxEvidence)) sb.Append("\n• ").Append(e);
                if (Evidence.Count > maxEvidence) sb.Append($"\n• (+{Evidence.Count - maxEvidence} more)");
            }
            sb.Append($"\nConfidence: {ConfidenceLabel} ({Confidence:0.00})");
            if (!string.IsNullOrEmpty(Uncertainty)) sb.Append(". ").Append(Uncertainty);
            foreach (var c in Caveats) sb.Append("\n⚠ ").Append(c);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Offline, retrieval-grounded question answering for development boards.
    /// Pipeline: normalise → extract entities → classify intent → resolve follow-ups with
    /// <see cref="DialogueContext"/> → retrieve facts from <see cref="KnowledgeBase"/> → generate a
    /// templated answer from those facts only → pins to highlight. It is NOT an LLM: answers are composed
    /// from retrieved, source-tagged facts by deterministic templates.
    /// </summary>
    public static class BoardAssistant
    {
        public static AssistantResponse Process(string question, BoardProfile board, DialogueContext context = null)
        {
            var resp = new AssistantResponse { Question = question ?? "", BoardId = board?.Id, BoardName = board?.DisplayName };
            string norm = TextAnalysis.Normalize(resp.Question);
            resp.NormalizedQuestion = norm;
            var entities = TextAnalysis.Extract(norm, board);
            var intent = IntentClassifier.Classify(norm, entities);

            if (intent.Kind == QueryKind.Compare && board != null)
            {
                resp.Entities = entities;
                AnswerGenerator.GenerateComparison(resp, intent, board, OtherBoardFor(board, entities));
                return resp; // a comparison is not a pin context for follow-ups
            }

            // The question names a different board than the one in view ("Does the Uno have SDA pins?" while
            // looking at the SPDUINO): answer for that board, without highlights or dialogue context.
            var named = board == null ? null : entities.BoardMentions.Where(id => id != board.Id).ToList();
            if (named != null && named.Count == 1 && !entities.BoardMentions.Contains(board.Id))
            {
                var other = BoardProfileRegistry.ById(named[0]);
                if (other != null)
                {
                    board = other;
                    context = null;
                    resp.AboutOtherBoard = true;
                    resp.BoardId = other.Id;
                    resp.BoardName = other.DisplayName;
                    entities = TextAnalysis.Extract(norm, board);
                    intent = IntentClassifier.Classify(norm, entities);
                }
            }
            resp.Entities = entities;

            // Circuit wiring ("How do I connect an LED?") and follow-ups about the active circuit
            // ("Why do I need a resistor?", "Where does the wiper go?").
            if (board != null && (intent.Kind == QueryKind.Connect && intent.CircuitId != null
                                  ? Do(() => CircuitGuides.Answer(resp, intent, entities, board, norm))
                                  : CircuitGuides.TryFollowUp(resp, intent, entities, board, context, norm)))
            {
                resp.ConfidenceLabel = Label(resp.Confidence);
                UpdateContext(context, board, resp);
                if (context != null && !intent.CircuitFollowUp && resp.HighlightPins.Count > 0)
                {
                    context.ActiveCircuit = intent.CircuitId;
                    context.ActiveCircuitPin = entities.PinIds.FirstOrDefault();
                }
                return resp;
            }

            var pins = new List<string>(entities.PinIds);
            string contextNote = ResolveFollowUp(norm, entities, intent, pins, board, context);
            resp.UsedContext = contextNote != null;

            var retrieval = board != null ? KnowledgeRetriever.Retrieve(intent, pins, board, norm, entities.FactTags) : new RetrievalResult();
            resp.Kind = intent.Kind;
            resp.Topic = intent.Topic;
            resp.IntentId = intent.Id;
            resp.ResolvedPinIds.AddRange(pins);
            resp.RetrievedFacts.AddRange(retrieval.Facts);
            if (!resp.AboutOtherBoard)
            {
                resp.HighlightPins.AddRange(retrieval.Pins);
                foreach (var kv in retrieval.Roles) resp.PinRoles[kv.Key] = kv.Value;
            }
            resp.Confidence = intent.Confidence * (resp.UsedContext ? 0.9f : 1f);
            if (intent.Kind == QueryKind.Unknown && retrieval.Lexical) resp.Confidence = 0.35f;

            AnswerGenerator.Generate(resp, intent, retrieval, board, contextNote);
            resp.ConfidenceLabel = Label(resp.Confidence);

            UpdateContext(context, board, resp);
            return resp;
        }

        static bool Do(System.Action action) { action(); return true; }

        static string Label(float confidence) => confidence >= 0.8f ? "High" : confidence >= 0.55f ? "Medium" : confidence > 0f ? "Low" : "None";

        /// <summary>The board to compare with: another named board, otherwise the other known board.</summary>
        static BoardProfile OtherBoardFor(BoardProfile board, ExtractedEntities e)
        {
            var named = e.BoardMentions.FirstOrDefault(id => id != board.Id);
            return BoardProfileRegistry.ById(named) ?? BoardProfileRegistry.All.FirstOrDefault(b => b.Id != board.Id);
        }

        /// <summary>
        /// Applies the previous turn only when the question is clearly a follow-up (pronoun, "what about…",
        /// or a bare fragment) AND the previous turn was on the same board. Returns a note describing the
        /// interpretation, or null when no context was used.
        /// </summary>
        static string ResolveFollowUp(string norm, ExtractedEntities e, ClassifiedIntent intent, List<string> pins, BoardProfile board, DialogueContext ctx)
        {
            bool followUpCue = e.HasFollowUpMarker || e.HasPronounReference;
            if (!followUpCue || ctx == null || !ctx.HasPrevious(board)) return null;
            var prevTopic = ctx.LastTopic != null ? Topics.Get(ctx.LastTopic) : null;
            bool prevHasCapTopic = prevTopic != null && prevTopic.Capabilities.Length > 0;
            // Refusals and wiring answers stand on their own: "how do I hook it up" is not "does it support I2C?".
            if (intent.Kind is QueryKind.Unsupported or QueryKind.Connect) return null;

            // "What about pin 5?" after a PWM question -> does D5 support PWM?
            if (pins.Count > 0 && intent.Topic == null)
            {
                if (prevHasCapTopic && ctx.LastKind is QueryKind.List or QueryKind.Explain or QueryKind.CapabilityCheck)
                {
                    SetIntent(intent, ctx.LastTopic, QueryKind.CapabilityCheck, 0.9f);
                    return $"Interpreted as a follow-up to the previous {prevTopic.Title} question.";
                }
                return null; // a plain pin lookup is fine without context
            }

            // "Does it support PWM?" / "Which of those are PWM?" -> check the previous pins.
            if (pins.Count == 0 && intent.Topic != null && intent.Topic != Topics.Board && e.HasPronounReference)
            {
                var previousPins = ctx.LastAskedPins.Count > 0 ? ctx.LastAskedPins : ctx.LastHighlightedPins;
                if (previousPins.Count > 0)
                {
                    pins.AddRange(previousPins);
                    SetIntent(intent, intent.Topic, QueryKind.CapabilityCheck, 0.85f);
                    return $"\"{(norm.Contains("those") || norm.Contains("them") ? "those" : "it")}\" taken as {string.Join(", ", previousPins)} from the previous answer.";
                }
            }

            // "What about I2C?" after a list question -> list I2C pins (same kind as before).
            if (pins.Count == 0 && intent.Topic != null && !intent.KindExplicit && e.HasFollowUpMarker
                && ctx.LastKind is QueryKind.List or QueryKind.Explain)
            {
                intent.Kind = ctx.LastKind;
                return "Answered in the same way as the previous question.";
            }

            // "How do I use it?" / "Tell me more" -> previous topic or pins.
            if (pins.Count == 0 && intent.Topic == null && intent.Kind == QueryKind.Unknown)
            {
                if (ctx.LastAskedPins.Count > 0 && ctx.LastKind == QueryKind.PinLookup)
                {
                    pins.AddRange(ctx.LastAskedPins);
                    SetIntent(intent, null, QueryKind.PinLookup, 0.8f);
                    return $"\"it\" taken as {string.Join(", ", ctx.LastAskedPins)} from the previous question.";
                }
                if (prevTopic != null && prevTopic.Id != Topics.Board)
                {
                    // "How do I hook it up?" after an I2C question asks for the wiring steps.
                    var kind = e.HasConnectCue && prevTopic.Id == Topics.I2c ? QueryKind.Connect
                        : IntentClassifier.HasExplainWording(norm) || norm.Contains("more") ? QueryKind.Explain : IntentClassifier.KindFromWording(norm);
                    SetIntent(intent, prevTopic.Id, kind, 0.75f);
                    return $"Interpreted as a follow-up about {prevTopic.Title}.";
                }
            }
            return null;
        }

        static void SetIntent(ClassifiedIntent intent, string topic, QueryKind kind, float confidence)
        {
            intent.Topic = topic;
            intent.TiedTopics.Clear();
            if (topic != null) intent.TiedTopics.Add(topic);
            intent.Kind = kind;
            intent.Confidence = confidence;
        }

        static void UpdateContext(DialogueContext ctx, BoardProfile board, AssistantResponse resp)
        {
            if (ctx == null || board == null) return;
            if (ctx.BoardId != board.Id) ctx.Reset();
            if (resp.Kind is QueryKind.Unknown or QueryKind.Unsupported) return; // keep the last useful turn
            ctx.BoardId = board.Id;
            ctx.LastTopic = resp.Topic;
            ctx.LastKind = resp.Kind;
            ctx.LastAskedPins.Clear();
            ctx.LastAskedPins.AddRange(resp.ResolvedPinIds);
            ctx.LastHighlightedPins.Clear();
            ctx.LastHighlightedPins.AddRange(resp.HighlightIds);
            ctx.Turns++;
        }
    }

    /// <summary>Builds the answer text strictly from retrieved facts (template-based, no free generation).</summary>
    public static class AnswerGenerator
    {
        public static void Generate(AssistantResponse resp, ClassifiedIntent intent, RetrievalResult r, BoardProfile board, string contextNote)
        {
            var sb = new StringBuilder();
            string boardName = board?.DisplayName ?? "this board";
            var topic = intent.Topic != null ? Topics.Get(intent.Topic) : null;
            string title = intent.TiedTopics.Count > 1 ? string.Join(" + ", intent.TiedTopics.Select(t => Topics.Get(t).Title)) : topic?.Title;
            resp.IntentTitle = intent.Kind switch
            {
                QueryKind.PinLookup => "Pin lookup",
                QueryKind.CapabilityCheck => $"Pin check: {title}",
                QueryKind.BoardInfo => "Board information",
                QueryKind.Connect => $"Wiring: {title?.Replace(" pins", "")} module",
                QueryKind.Unsupported => "Not in verified knowledge",
                QueryKind.Unknown => r.Lexical ? "Closest knowledge" : "Unknown question",
                _ => title,
            };

            switch (intent.Kind)
            {
                case QueryKind.List:
                    if (intent.CapabilityFilter != null)
                    {
                        // "Which one is SDA?" - just that role.
                        sb.Append(r.Pins.Count > 0
                            ? $"{Topics.CapabilityName(intent.CapabilityFilter)} on {boardName}: {Ids(r.Pins)}."
                            : $"No pin with {Topics.CapabilityName(intent.CapabilityFilter)} is recorded for {boardName}.");
                        break;
                    }
                    sb.Append(r.Pins.Count > 0 ? $"{title} on {boardName} ({r.Pins.Count}): {Ids(r.Pins)}." : $"{title} on {boardName}: none.");
                    if (r.Concept != null) sb.Append(' ').Append(FirstSentences(r.Concept.Text, 2));
                    if (r.Pins.Count == 0) sb.Append($" No pins with this function are recorded for {boardName}.");
                    break;

                case QueryKind.Connect:
                    GenerateWiring(sb, resp, r, board);
                    break;

                case QueryKind.Explain:
                    foreach (var concept in r.Facts.Where(f => f.Kind == FactKind.Concept)) sb.Append(concept.Text).Append(' ');
                    sb.Append(r.Pins.Count > 0 ? $"\nOn {boardName} the {title} are: {Ids(r.Pins)}." : $"\nNo pins for this are recorded on {boardName}.");
                    break;

                case QueryKind.CapabilityCheck:
                    foreach (var check in r.Checks)
                    {
                        string what = topic != null ? topic.Title.Replace(" pins", "").Replace(" pin", "") : "that";
                        if (!check.Has)
                        {
                            sb.Append($"No: {check.Pin.Id} is not listed with {what} on {boardName}. ({check.Pin.Description})\n");
                        }
                        else if (check.Evidence.All(f => f.Status == VerificationStatus.Inferred))
                            sb.Append($"Probably: {check.Pin.Id} provides {CapNames(check)} on ATmega328P-compatible boards, but this is UNVERIFIED on {boardName}.\n");
                        else
                            sb.Append($"Yes: {check.Pin.Id} provides {CapNames(check)} on {boardName}.\n");
                    }
                    if (r.Checks.Count == 0) sb.Append($"Those pins are not on {boardName}.");
                    if (r.Checks.Any(c => !c.Has) && topic != null)
                    {
                        var allWith = board.Pins.Where(p => topic.Capabilities.Any(p.Has)).OrderBy(p => Topics.PinOrder(p.Id)).Select(p => p.Id).ToList();
                        if (allWith.Count > 0) sb.Append($"{topic.Title} on {boardName}: {string.Join(", ", allWith)}.");
                    }
                    break;

                case QueryKind.PinLookup:
                    foreach (var pin in r.Pins)
                    {
                        sb.Append($"{pin.Id} (\"{pin.Label}\") on {boardName}: {pin.Description}\n");
                        var capFacts = r.Facts.Where(f => f.Kind == FactKind.PinCapability && f.PinId == pin.Id).ToList();
                        if (capFacts.Count > 0)
                            sb.Append("Functions: ").Append(string.Join("; ", capFacts.Select(f => $"{Topics.CapabilityName(f.Capability)} ({KnowledgeBase.StatusLabel(f.Status)})"))).Append('\n');
                    }
                    if (r.Pins.Count == 0) sb.Append($"That pin is not on {boardName}.");
                    break;

                case QueryKind.BoardInfo:
                    if (intent.AskedFeature != null)
                    {
                        // Absence of a record, not a claim about the hardware beyond the documented features.
                        sb.Append($"I have no record of built-in {intent.AskedFeature} on {boardName}.");
                        if (r.Facts.Count > 0) sb.Append(" Its recorded connectors and features:");
                        foreach (var f in r.Facts) sb.Append("\n• ").Append(f.Text);
                        break;
                    }
                    sb.Append($"About {boardName}:");
                    foreach (var f in r.Facts) sb.Append("\n• ").Append(f.Text);
                    break;

                case QueryKind.Unsupported:
                    sb.Append($"I don't have a verified {intent.UnsupportedReason} for {boardName}, so I won't guess one. ");
                    sb.Append("Check the board's official documentation and the microcontroller datasheet before connecting loads or supplies.");
                    resp.Caveats.Add("Electrical ratings are only given when they are documented or verified for the board.");
                    break;

                default:
                    if (r.Lexical)
                    {
                        sb.Append("I couldn't match that to a specific pin question. The closest knowledge I have:");
                        foreach (var f in r.Facts) sb.Append("\n• ").Append(FirstSentences(f.Text, 2));
                    }
                    else
                    {
                        sb.Append($"I couldn't match that to {boardName} knowledge. Try asking about PWM, digital or analog pins, power and ground, UART, I2C, SPI, interrupts, the on-board LED, a specific pin (e.g. \"What does pin 9 do?\"), or a concept (\"What is PWM?\").");
                    }
                    break;
            }

            resp.Answer = sb.ToString().TrimEnd();
            BuildEvidence(resp, r);
            BuildCaveats(resp, intent, r, board, contextNote);
        }

        static string Ids(IEnumerable<PinRecord> pins) => string.Join(", ", pins.Select(p => p.Id));

        /// <summary>
        /// Numbered I2C wiring steps from the board's own pins. Each signal pin carries its verification status;
        /// the supply voltage is left to the module's datasheet (no ratings are stated).
        /// </summary>
        static void GenerateWiring(StringBuilder sb, AssistantResponse resp, RetrievalResult r, BoardProfile board)
        {
            string name = board?.ShortName ?? "this board";
            string Signal(string role, string cap)
            {
                if (!r.RolePins.TryGetValue(role, out var pin)) return $"{role}: no {role} pin is recorded on {name}";
                string s = $"{role} to {pin.Id}";
                if (r.RoleAlternates.TryGetValue(role, out var alt)) s += $" (same signal as {alt.Id})";
                bool inferred = r.Facts.Any(f => f.PinId == pin.Id && f.Capability == cap && f.Status == VerificationStatus.Inferred);
                if (inferred) s += ", UNVERIFIED on this board";
                return s;
            }
            // Logic level only when a documented/verified board fact states 5 V operation.
            bool fiveVolt = r.Facts.Any(f => f.Kind == FactKind.Board && f.Tags.Contains(FactTags.Voltage) && f.Text.Contains("5 V"));
            sb.Append($"Wiring an I2C module to {name}:");
            sb.Append("\n1. VCC to 5V or 3.3V, whichever supply voltage the module's datasheet specifies.");
            sb.Append("\n2. GND to GND.");
            sb.Append("\n3. ").Append(Signal(PinRoles.Sda, PinCapability.I2cSda)).Append('.');
            sb.Append("\n4. ").Append(Signal(PinRoles.Scl, PinCapability.I2cScl)).Append('.');
            sb.Append("\nSDA and SCL need pull-up resistors (many modules include them). ");
            sb.Append(fiveVolt
                ? "This board's I/O runs at 5 V, so a module that only tolerates 3.3 V may need a logic-level shifter."
                : "Check the board's logic level: a module that only tolerates 3.3 V may need a logic-level shifter.");
            resp.Legend = "Colours: SDA cyan, SCL magenta, power red, GND blue.";
        }

        /// <summary>Attribute-by-attribute comparison of two boards, each value with its own source status.</summary>
        public static void GenerateComparison(AssistantResponse resp, ClassifiedIntent intent, BoardProfile a, BoardProfile b)
        {
            resp.Kind = QueryKind.Compare;
            resp.Topic = Topics.Board;
            resp.IntentId = intent.Id;
            resp.IntentTitle = "Board comparison";
            resp.Confidence = intent.Confidence;
            resp.ConfidenceLabel = resp.Confidence >= 0.8f ? "High" : "Medium";
            var sb = new StringBuilder();
            if (b == null)
            {
                sb.Append($"I only have knowledge for {a.ShortName}, so there is nothing to compare it with.");
                resp.Answer = sb.ToString();
                return;
            }
            var factsA = KnowledgeBase.For(a).Where(f => f.Kind == FactKind.Board).ToList();
            var factsB = KnowledgeBase.For(b).Where(f => f.Kind == FactKind.Board).ToList();
            sb.Append($"{a.ShortName} vs {b.ShortName}:");
            foreach (var tag in FactTags.ComparisonOrder)
            {
                var fa = factsA.Where(f => f.Tags.Contains(tag)).ToList();
                var fb = factsB.Where(f => f.Tags.Contains(tag)).ToList();
                if (fa.Count == 0 && fb.Count == 0) continue;
                sb.Append($"\n• {FactTags.Title(tag)} — {a.ShortName}: {Describe(fa)} | {b.ShortName}: {Describe(fb)}");
                resp.RetrievedFacts.AddRange(fa);
                resp.RetrievedFacts.AddRange(fb);
            }
            resp.Answer = sb.ToString();
            foreach (var f in resp.RetrievedFacts)
                resp.Evidence.Add($"{FirstSentences(f.Text, 1)} Source: {f.Source} [{KnowledgeBase.StatusLabel(f.Status)}]");
            if (resp.RetrievedFacts.Any(f => f.Status == VerificationStatus.Inferred))
                resp.Uncertainty = "Values marked UNVERIFIED are inferred, not confirmed on the board.";
        }

        static string Describe(List<KnowledgeFact> facts) =>
            facts.Count == 0 ? "no record"
                : string.Join(" ", facts.Select(f => f.Text.TrimEnd('.') + $" ({(f.Status == VerificationStatus.Inferred ? "UNVERIFIED" : KnowledgeBase.StatusLabel(f.Status))})."));

        static string CapNames(CapabilityCheckResult c) => string.Join(" and ", c.Evidence.Select(f => Topics.CapabilityName(f.Capability)).Distinct());

        static string FirstSentences(string text, int count)
        {
            var parts = text.Split(new[] { ". " }, System.StringSplitOptions.None);
            var joined = string.Join(". ", parts.Take(count));
            return joined.EndsWith(".") ? joined : joined + ".";
        }

        /// <summary>Evidence lines: pin facts grouped by capability, source and status; other facts individually.</summary>
        /// <summary>Evidence lines and caveats for answers built outside <see cref="Generate"/> (circuit guides).</summary>
        internal static void AddEvidenceAndCaveats(AssistantResponse resp, ClassifiedIntent intent, RetrievalResult r, BoardProfile board, string contextNote)
        {
            BuildEvidence(resp, r);
            BuildCaveats(resp, intent, r, board, contextNote);
        }

        static void BuildEvidence(AssistantResponse resp, RetrievalResult r)
        {
            foreach (var g in r.Facts.Where(f => f.Kind == FactKind.PinCapability).GroupBy(f => (f.Capability, f.Source, f.Status)))
                resp.Evidence.Add($"{string.Join(", ", g.Select(f => f.PinId))}: {Topics.CapabilityName(g.Key.Capability)}. Source: {g.Key.Source} [{KnowledgeBase.StatusLabel(g.Key.Status)}]");
            foreach (var f in r.Facts.Where(f => f.Kind != FactKind.PinCapability))
                resp.Evidence.Add(f.Kind == FactKind.Concept
                    ? $"Concept '{f.Topic}'. Source: {f.Source} [{KnowledgeBase.StatusLabel(f.Status)}]"
                    : $"{FirstSentences(f.Text, 1)} Source: {f.Source} [{KnowledgeBase.StatusLabel(f.Status)}]");
        }

        // Loads that must never hang directly off an I/O pin.
        static readonly System.Text.RegularExpressions.Regex s_LoadWords = new(@"\b(?:motors?|relays?|solenoids?|pumps?|fans?)\b");

        static void BuildCaveats(AssistantResponse resp, ClassifiedIntent intent, RetrievalResult r, BoardProfile board, string contextNote)
        {
            var notes = new List<string>();
            if (contextNote != null) notes.Add(contextNote);
            if (resp.AboutOtherBoard)
                resp.Caveats.Add($"Answered for {board?.DisplayName}, which is not the board in view, so nothing is highlighted.");
            if (intent.ConnectWithoutGuide)
                resp.Caveats.Add("Step-by-step wiring guidance is available for I2C modules only; these are the relevant pins.");

            var inferredPins = r.Facts.Where(f => f.Status == VerificationStatus.Inferred && f.PinId != null && f.Kind == FactKind.PinCapability
                                                  && (intent.Kind != QueryKind.List && intent.Kind != QueryKind.Explain || intent.TiedTopics.Contains(f.Topic)))
                                      .Select(f => f.PinId).Distinct().ToList();
            if (inferredPins.Count > 0)
                notes.Add($"Unverified on {board?.DisplayName}: {string.Join(", ", inferredPins)} ({board?.InferredSource}).");
            if (intent.TiedTopics.Count > 1) notes.Add("The question matched several topics equally; all are answered.");
            if (r.Lexical) notes.Add("Best-match retrieval only; the question did not match a known intent.");
            resp.Uncertainty = string.Join(" ", notes);

            // Safety caveats (general advice; no invented ratings). A refusal only carries its own caveat.
            if (intent.Kind == QueryKind.Unsupported) return;
            var topics = new HashSet<string>(intent.TiedTopics);
            var pinIds = new HashSet<string>(resp.HighlightIds);
            if (topics.Contains(Topics.Power) || pinIds.Overlaps(new[] { "5V", "3V3", "VIN" }))
                resp.Caveats.Add("Do not connect an external supply to the 5V/3.3V pins unless the board documentation allows it, and never short a supply pin to GND.");
            if (topics.Contains(Topics.Pwm) || topics.Contains(Topics.Digital) || s_LoadWords.IsMatch(resp.NormalizedQuestion ?? ""))
                resp.Caveats.Add("Drive motors, relays and high-power LEDs through a transistor or driver, not directly from an I/O pin.");
            if (topics.Contains(Topics.Aref) || pinIds.Contains("AREF"))
                resp.Caveats.Add("Never apply more than the board's operating voltage to AREF.");
            if (topics.Contains(Topics.Serial) || pinIds.Overlaps(new[] { "D0", "D1" }))
                resp.Caveats.Add("D0/D1 are shared with the USB serial link; disconnect external devices while uploading.");
        }
    }

    /// <summary>Wiring roles for highlighted pins and the colours the AR views use for them.</summary>
    public static class PinRoles
    {
        public const string Sda = "SDA", Scl = "SCL", Signal = "Signal", Analog = "Analog", Power = "Power", Ground = "GND";

        /// <summary>Highlight colour for a role; null for pins without a role (the views keep their default).</summary>
        public static UnityEngine.Color? ColorFor(string role) => role switch
        {
            Sda => new UnityEngine.Color(0.0f, 0.9f, 1.0f),    // cyan
            Scl => new UnityEngine.Color(1.0f, 0.3f, 0.9f),    // magenta
            Signal => new UnityEngine.Color(1.0f, 0.85f, 0.0f), // yellow (the usual highlight colour)
            Analog => new UnityEngine.Color(0.2f, 0.9f, 0.2f),  // green
            Power => new UnityEngine.Color(1.0f, 0.2f, 0.15f),  // red
            Ground => new UnityEngine.Color(0.3f, 0.5f, 1.0f),  // blue
            _ => null,
        };

        static readonly (string role, string text)[] s_LegendOrder =
        {
            (Sda, "SDA cyan"), (Scl, "SCL magenta"), (Signal, "signal yellow"), (Analog, "analog input green"), (Power, "power red"), (Ground, "GND blue"),
        };

        /// <summary>Colour key for the roles in use, e.g. "Colours: SDA cyan, SCL magenta, power red, GND blue."; null when none.</summary>
        public static string Legend(IEnumerable<string> roles)
        {
            var used = new HashSet<string>(roles);
            var parts = s_LegendOrder.Where(x => used.Contains(x.role)).Select(x => x.text).ToList();
            return parts.Count == 0 ? null : "Colours: " + string.Join(", ", parts) + ".";
        }
    }

    /// <summary>
    /// Prevents a typed question being processed twice (e.g. keyboard Done followed by the Ask button).
    /// Pure logic so it can be unit-tested; time is passed in.
    /// </summary>
    public sealed class SubmissionGate
    {
        readonly float m_WindowSeconds;
        string m_LastText;
        float m_LastTime = float.NegativeInfinity;

        public SubmissionGate(float windowSeconds = 1.5f) { m_WindowSeconds = windowSeconds; }

        /// <summary>True when the text should be processed; false for empty text or a repeat inside the window.</summary>
        public bool TryAccept(string text, float now)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text == m_LastText && now - m_LastTime < m_WindowSeconds) return false;
            m_LastText = text;
            m_LastTime = now;
            return true;
        }
    }
}
