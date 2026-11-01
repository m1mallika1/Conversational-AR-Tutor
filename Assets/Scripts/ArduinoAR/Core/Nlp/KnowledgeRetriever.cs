using System;
using System.Collections.Generic;
using System.Linq;

namespace ArduinoAR
{
    /// <summary>One "does pin X have capability Y" outcome.</summary>
    public sealed class CapabilityCheckResult
    {
        public PinRecord Pin;
        public bool Has;
        /// <summary>Capability facts that support "yes" (empty when the answer is no).</summary>
        public readonly List<KnowledgeFact> Evidence = new();
    }

    public sealed class RetrievalResult
    {
        public readonly List<KnowledgeFact> Facts = new();
        public readonly List<PinRecord> Pins = new();
        public readonly List<CapabilityCheckResult> Checks = new();
        public KnowledgeFact Concept;
        /// <summary>Role of each highlighted pin for wiring answers (see <see cref="PinRoles"/>); empty otherwise.</summary>
        public readonly Dictionary<string, string> Roles = new();
        /// <summary>Wiring answers: the pin carrying each role, and another header pin with the same signal (Uno A4 = SDA).</summary>
        public readonly Dictionary<string, PinRecord> RolePins = new();
        public readonly Dictionary<string, PinRecord> RoleAlternates = new();
        /// <summary>True when results come from the lexical fallback instead of a structured intent.</summary>
        public bool Lexical;
        public float LexicalScore;
    }

    /// <summary>
    /// Retrieves knowledge facts for an intent. Structured retrieval (topic/capability/pin filters over
    /// the board's facts) is used for recognised intents; a BM25 lexical search over all facts is the
    /// fallback for questions no intent matched.
    /// </summary>
    public static class KnowledgeRetriever
    {
        public const float LexicalThreshold = 2.0f;

        public static RetrievalResult Retrieve(ClassifiedIntent intent, IReadOnlyList<string> pinIds, BoardProfile board, string normalizedQuestion,
            IReadOnlyList<string> factTags = null)
        {
            var r = new RetrievalResult();
            var facts = KnowledgeBase.For(board);
            var topic = intent.Topic != null ? Topics.Get(intent.Topic) : null;
            var caps = intent.CapabilityFilter != null
                ? new HashSet<string> { intent.CapabilityFilter }
                : new HashSet<string>(intent.TiedTopics.SelectMany(t => Topics.Get(t).Capabilities));

            switch (intent.Kind)
            {
                case QueryKind.List:
                case QueryKind.Explain:
                    foreach (var t in intent.TiedTopics)
                    {
                        var concept = KnowledgeBase.ConceptFor(t);
                        if (concept != null) { r.Facts.Add(concept); r.Concept ??= concept; }
                    }
                    AddCapabilityFacts(r, facts, board, caps);
                    break;

                case QueryKind.CapabilityCheck:
                    if (topic != null) r.Concept = KnowledgeBase.ConceptFor(topic.Id);
                    foreach (var id in pinIds)
                    {
                        var pin = board.Pins.FirstOrDefault(p => p.Id == id);
                        if (pin == null) continue;
                        var check = new CapabilityCheckResult { Pin = pin };
                        check.Evidence.AddRange(facts.Where(f => f.Kind == FactKind.PinCapability && f.PinId == id && caps.Contains(f.Capability)));
                        check.Has = check.Evidence.Count > 0;
                        r.Checks.Add(check);
                        r.Facts.AddRange(check.Evidence);
                        if (!check.Has) r.Facts.AddRange(facts.Where(f => f.Kind == FactKind.PinSummary && f.PinId == id));
                        r.Pins.Add(pin); // highlight the pin that was asked about
                    }
                    break;

                case QueryKind.PinLookup:
                    foreach (var id in pinIds)
                    {
                        var pin = board.Pins.FirstOrDefault(p => p.Id == id);
                        if (pin == null) continue;
                        r.Facts.AddRange(facts.Where(f => f.PinId == id && (f.Kind == FactKind.PinSummary || f.Kind == FactKind.PinCapability)));
                        r.Pins.Add(pin);
                    }
                    break;

                case QueryKind.BoardInfo:
                    var boardFacts = facts.Where(f => f.Kind == FactKind.Board).ToList();
                    // A built-in feature question lists what the board does have; an attribute question
                    // ("flash size?") returns only the facts tagged with that attribute.
                    var wanted = (intent.AskedFeature != null ? new[] { FactTags.Connectors } : factTags ?? new string[0]).ToList();
                    // Microcontroller questions also name the USB interface chip: the Uno's ATmega16U2 is a second
                    // processor, and on the SPDUINO the FT232RL is the other main chip.
                    if (wanted.Contains(FactTags.Mcu) && !wanted.Contains(FactTags.Usb)) wanted.Add(FactTags.Usb);
                    var tagged = boardFacts.Where(f => f.Tags.Intersect(wanted).Any()).ToList();
                    r.Facts.AddRange(tagged.Count > 0 ? tagged : boardFacts);
                    break;

                case QueryKind.Connect:
                    RetrieveWiring(r, facts, board);
                    break;

                case QueryKind.Unknown:
                    LexicalSearch(r, facts, normalizedQuestion);
                    break;
            }
            return r;
        }

        /// <summary>
        /// I2C wiring: SDA/SCL pins from the board's own capability facts (keeping their verification status),
        /// the 5V and 3.3V supply pins and every GND pin. Where a board has dedicated SDA/SCL header pins they are
        /// used, and the analog pins carrying the same signal are kept as alternates.
        /// </summary>
        static void RetrieveWiring(RetrievalResult r, IReadOnlyList<KnowledgeFact> facts, BoardProfile board)
        {
            if (board == null) return;
            var wiring = KnowledgeBase.ConceptFor(KnowledgeBase.I2cWiringTopic);
            if (wiring != null) { r.Facts.Add(wiring); r.Concept = wiring; }

            void Signal(string cap, string role)
            {
                // Dedicated "SDA"/"SCL" header pins first (Uno), then the analog pins with the same signal.
                var pins = board.Pins.Where(p => p.Has(cap)).OrderBy(p => p.Id == "SDA" || p.Id == "SCL" ? 0 : 1).ToList();
                if (pins.Count == 0) return;
                r.RolePins[role] = pins[0];
                if (pins.Count > 1) r.RoleAlternates[role] = pins[1];
                foreach (var p in pins)
                    r.Facts.AddRange(facts.Where(f => f.Kind == FactKind.PinCapability && f.PinId == p.Id && f.Capability == cap));
                r.Pins.Add(pins[0]);
                r.Roles[pins[0].Id] = role;
            }
            Signal(PinCapability.I2cSda, PinRoles.Sda);
            Signal(PinCapability.I2cScl, PinRoles.Scl);

            foreach (var id in new[] { "5V", "3V3" })
            {
                var pin = board.Pins.FirstOrDefault(p => p.Id == id && p.Has(PinCapability.Power));
                if (pin == null) continue;
                r.Pins.Add(pin);
                r.Roles[pin.Id] = PinRoles.Power;
                r.Facts.AddRange(facts.Where(f => f.Kind == FactKind.PinCapability && f.PinId == pin.Id));
            }
            foreach (var pin in board.Pins.Where(p => p.Has(PinCapability.Ground)))
            {
                r.Pins.Add(pin);
                r.Roles[pin.Id] = PinRoles.Ground;
            }
            r.Facts.AddRange(facts.Where(f => f.Kind == FactKind.Board && f.Tags.Contains(FactTags.Voltage)));
        }

        static void AddCapabilityFacts(RetrievalResult r, IReadOnlyList<KnowledgeFact> facts, BoardProfile board, HashSet<string> caps)
        {
            if (board == null) return;
            // Natural pin order (D3, D5 ... A0 ...) keeps answers readable and stable.
            foreach (var pin in board.Pins.OrderBy(p => Topics.PinOrder(p.Id)))
            {
                var pinFacts = facts.Where(f => f.Kind == FactKind.PinCapability && f.PinId == pin.Id && caps.Contains(f.Capability)).ToList();
                if (pinFacts.Count == 0) continue;
                r.Facts.AddRange(pinFacts);
                r.Pins.Add(pin);
            }
        }

        static readonly HashSet<string> s_Stopwords = new()
        {
            "what", "is", "are", "a", "an", "the", "of", "to", "for", "on", "in", "and", "or", "do", "does", "i", "my", "me",
            "how", "can", "with", "this", "that", "it", "be", "which", "where", "use", "used", "using", "about", "board",
            "pin", "pins", "tell", "please", "you", "your", "should", "would", "will", "there", "from", "get", "make",
        };

        /// <summary>BM25 over fact text + keywords. Only returns facts above <see cref="LexicalThreshold"/>.</summary>
        static void LexicalSearch(RetrievalResult r, IReadOnlyList<KnowledgeFact> facts, string normalizedQuestion)
        {
            var query = TextAnalysis.Tokens(normalizedQuestion).Where(t => !s_Stopwords.Contains(t)).Distinct().ToList();
            if (query.Count == 0) return;
            // Only concept and board facts: pins are reached through explicit pin questions, and a weak
            // word overlap with a pin description ("pull" in "pull LOW") is not a meaningful match.
            facts = facts.Where(f => f.Kind == FactKind.Concept || f.Kind == FactKind.Board).ToList();

            var docs = facts.Select(f => TextAnalysis.Tokens(TextAnalysis.Normalize(f.Text)).Concat(f.Keywords).ToList()).ToList();
            double avgLen = docs.Average(d => d.Count);
            int n = docs.Count;
            var df = query.ToDictionary(q => q, q => docs.Count(d => d.Contains(q)));
            const double k1 = 1.2, b = 0.75;

            var ranked = new List<(KnowledgeFact fact, double score)>();
            for (int i = 0; i < n; i++)
            {
                double score = 0;
                foreach (var q in query)
                {
                    int tf = docs[i].Count(t => t == q);
                    if (tf == 0) continue;
                    double idf = Math.Log(1 + (n - df[q] + 0.5) / (df[q] + 0.5));
                    score += idf * tf * (k1 + 1) / (tf + k1 * (1 - b + b * docs[i].Count / avgLen));
                }
                if (score > 0) ranked.Add((facts[i], score));
            }
            var top = ranked.OrderByDescending(x => x.score).Where(x => x.score >= LexicalThreshold).Take(3).ToList();
            if (top.Count == 0) return;
            r.Lexical = true;
            r.LexicalScore = (float)top[0].score;
            r.Facts.AddRange(top.Select(x => x.fact));
        }
    }
}
