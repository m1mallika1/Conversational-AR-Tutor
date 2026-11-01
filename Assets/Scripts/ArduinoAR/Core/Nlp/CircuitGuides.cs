using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ArduinoAR
{
    /// <summary>
    /// Board-specific wiring guides for simple circuits (LED + series resistor, push button, potentiometer,
    /// light sensor) and their follow-up questions. Pins come from the active board's profile: a default pin per
    /// circuit (D9, D2, A0) or a pin the user names, used only when the profile gives that pin the required
    /// capability. Each pin keeps the verification status of that capability, so SPDUINO mappings that are only
    /// inferred are shown as UNVERIFIED. The guidance is general wiring advice: it states no board ratings and is
    /// never presented as a tested physical circuit. (I2C wiring is handled by the existing Connect path.)
    /// </summary>
    public static class CircuitGuides
    {
        public const string Led = "led", Button = "button", Potentiometer = "potentiometer", Ldr = "ldr";

        const string k_NotTested = "This is wiring guidance; it has not been tested as a physical circuit on this board.";
        const string k_Source = "General electronics / Arduino reference";

        // ---------- Recognising the component ----------

        static readonly (Regex pattern, string id)[] s_Components =
        {
            // The board's own LED ("built-in LED", "LED_BUILTIN", "on-board LED") is a pin question, not a circuit.
            (new Regex(@"\b(?:ldr|ldrs|photoresistor|photo resistor|light dependent resistor|light sensor)\b"), Ldr),
            (new Regex(@"\b(?:potentiometer|potentiometers|pot|pots|trimpot|trimmer|knob|variable resistor|wiper)\b"), Potentiometer),
            (new Regex(@"\b(?:push ?buttons?|pushbuttons?|buttons?|tactile switch(?:es)?|tact switch(?:es)?|momentary switch(?:es)?)\b"), Button),
            (new Regex(@"(?<!built in )(?<!builtin )(?<!on board )(?<!onboard )\b(?:leds?|light emitting diodes?)\b(?!_builtin)"), Led),
        };

        /// <summary>The circuit component a normalised question names, or null.</summary>
        public static string DetectComponent(string normalized)
        {
            foreach (var (pattern, id) in s_Components)
                if (pattern.IsMatch(normalized)) return id;
            return null;
        }

        // ---------- Pin plan ----------

        public sealed class Plan
        {
            public string CircuitId;
            public BoardProfile Board;
            /// <summary>The controlled pin: LED/button signal, or the potentiometer/LDR analog input.</summary>
            public PinRecord Main;
            public string MainCapability;
            public PinRecord Supply;              // 5V for the potentiometer and LDR
            public readonly List<PinRecord> Grounds = new();
            /// <summary>Why the requested pin cannot be used (no wiring is given then).</summary>
            public string Problem;
            public bool MainInferred;
        }

        static string MainRole(string circuit) => circuit is Potentiometer or Ldr ? PinRoles.Analog : PinRoles.Signal;

        public static Plan BuildPlan(string circuit, BoardProfile board, string requestedPinId)
        {
            var plan = new Plan { CircuitId = circuit, Board = board };
            bool analog = circuit is Potentiometer or Ldr;
            string defaultPin = circuit switch { Led => "D9", Button => "D2", _ => "A0" };
            var pin = board.Pins.FirstOrDefault(p => p.Id == (requestedPinId ?? defaultPin));
            if (pin == null)
            {
                plan.Problem = $"{requestedPinId ?? defaultPin} is not a pin on {board.ShortName}.";
                return plan;
            }

            string cap = analog ? PinCapability.AnalogInput
                : pin.Has(PinCapability.Digital) ? PinCapability.Digital
                : pin.Has(PinCapability.DigitalAlt) ? PinCapability.DigitalAlt : null;
            if (cap == null || !pin.Has(cap))
            {
                var options = board.Pins.Where(p => analog ? p.Has(PinCapability.AnalogInput) : p.Has(PinCapability.Digital))
                                        .OrderBy(p => Topics.PinOrder(p.Id)).Select(p => p.Id);
                plan.Problem = analog
                    ? $"{pin.Id} can't read an analog voltage, so it can't measure a {(circuit == Ldr ? "light sensor" : "potentiometer")}.\nUse an analog input instead: {string.Join(", ", options)}."
                    : $"{pin.Id} is not an I/O pin, so it can't {(circuit == Led ? "switch an LED" : "read a button")}.\nUse a digital pin instead, for example {defaultPin}.";
                return plan;
            }
            plan.Main = pin;
            plan.MainCapability = cap;
            plan.MainInferred = pin.InferredCapabilities.Contains(cap);
            if (analog) plan.Supply = board.Pins.FirstOrDefault(p => p.Id == "5V");
            plan.Grounds.AddRange(board.Pins.Where(p => p.Has(PinCapability.Ground)));
            return plan;
        }

        // ---------- Wiring answers ----------

        /// <summary>Answers a wiring question for <paramref name="intent"/>.CircuitId on <paramref name="board"/>.</summary>
        public static void Answer(AssistantResponse resp, ClassifiedIntent intent, ExtractedEntities e, BoardProfile board, string normalized)
        {
            string requested = e.PinIds.FirstOrDefault();
            var plan = BuildPlan(intent.CircuitId, board, requested);
            resp.Kind = QueryKind.Connect;
            resp.Topic = intent.Topic;
            resp.IntentId = intent.Id;
            resp.IntentTitle = "Wiring: " + Title(intent.CircuitId);
            resp.Confidence = intent.Confidence;
            resp.ResolvedPinIds.AddRange(e.PinIds);

            var r = new RetrievalResult();
            var sb = new StringBuilder();
            if (plan.Problem != null)
            {
                sb.Append(plan.Problem);
            }
            else if (intent.CircuitId == Led && s_NoResistor.IsMatch(normalized))
            {
                // "Can I connect an LED directly to pin 9 without a resistor?"
                sb.Append($"No. Always put a series resistor between {Pin(plan)} and the LED. Without it the LED draws too much current, which can damage the LED and the pin. ");
                sb.Append("Choose the resistor from the supply voltage, the LED's forward voltage and the current you want.");
                AddPin(r, plan.Main, MainRole(plan.CircuitId));
            }
            else
            {
                switch (plan.CircuitId)
                {
                    case Led: WriteLed(sb, plan); break;
                    case Button: WriteButton(sb, plan); break;
                    case Potentiometer: WritePotentiometer(sb, plan); break;
                    case Ldr: WriteLdr(sb, plan); break;
                }
                sb.Append('\n').Append(k_NotTested);
                AddPlanPins(r, plan);
            }
            AddFacts(r, plan);
            Finish(resp, intent, r, board, sb.ToString());
        }

        static readonly Regex s_NoResistor = new(@"\bwithout (?:a |any |the )?resistor\b|\bno resistor\b|\bdirectly\b");

        static string Title(string circuit) => circuit switch
        {
            Led => "LED with series resistor",
            Button => "push button",
            Potentiometer => "potentiometer (analog input)",
            Ldr => "light sensor (LDR, analog input)",
            _ => circuit,
        };

        static string Pin(Plan plan) => plan.Main.Id + (plan.MainInferred ? " (UNVERIFIED on this board)" : "");

        /// <summary>Name used in code: pinMode(9, ...) for D9, analogRead(A0) for A0.</summary>
        static string CodeName(PinRecord pin) => pin.Id.StartsWith("D") ? pin.Id.Substring(1) : pin.Id;

        static string LogicVoltage(BoardProfile board) =>
            KnowledgeBase.For(board).Any(f => f.Kind == FactKind.Board && f.Tags.Contains(FactTags.Voltage) && f.Text.Contains("5 V"))
                ? "5 V on this board's I/O" : "the board's logic voltage";

        static void WriteLed(StringBuilder sb, Plan p)
        {
            sb.Append($"Wiring an LED with a series resistor on {p.Board.ShortName}:");
            sb.Append($"\n1. {Pin(p)} to one end of the resistor.");
            sb.Append("\n2. The resistor's other end to the LED's anode (longer leg).");
            sb.Append("\n3. The LED's cathode (shorter leg, flat side) to GND.");
            sb.Append($"\nChoose the resistor from the supply voltage ({LogicVoltage(p.Board)}), the LED's forward voltage and the current you want. ");
            sb.Append("220 ohms to 1 kilohm is a common illustrative range for typical indicator LEDs, but it is not universally suitable: check the LED's datasheet and your circuit.");
            if (p.Main.Has(PinCapability.Pwm))
                sb.Append($"\n{p.Main.Id} also supports PWM, so analogWrite({CodeName(p.Main)}, value) can dim the LED.");
        }

        static void WriteButton(StringBuilder sb, Plan p)
        {
            sb.Append($"Wiring a push button on {p.Board.ShortName}:");
            sb.Append($"\n1. {Pin(p)} to one side of the button.");
            sb.Append("\n2. The other side of the button to GND.");
            sb.Append($"\nUse pinMode({CodeName(p.Main)}, INPUT_PULLUP): the internal pull-up resistor keeps the pin HIGH and pressing the button pulls it LOW, so no external resistor is needed. ");
            sb.Append("A four-leg tactile button joins its legs in pairs, so connect across the switch, for example to diagonally opposite legs.");
        }

        static void WritePotentiometer(StringBuilder sb, Plan p)
        {
            sb.Append($"Wiring a potentiometer on {p.Board.ShortName}:");
            sb.Append("\n1. One outer leg to 5V.");
            sb.Append("\n2. The other outer leg to GND.");
            sb.Append($"\n3. The middle leg (wiper) to {Pin(p)}.");
            sb.Append($"\nRead it with analogRead({CodeName(p.Main)}). Power it from the board's own 5V so the voltage on {p.Main.Id} stays between GND and 5V; never connect an analog pin to VIN or an external supply.");
        }

        static void WriteLdr(StringBuilder sb, Plan p)
        {
            sb.Append($"Wiring a light sensor (LDR) on {p.Board.ShortName} as a voltage divider:");
            sb.Append("\n1. One leg of the LDR to 5V.");
            sb.Append($"\n2. The LDR's other leg to {Pin(p)}.");
            sb.Append($"\n3. A fixed resistor from {p.Main.Id} to GND. 10 kilohms is a common illustrative value; choose one that suits your LDR.");
            sb.Append($"\nRead it with analogRead({CodeName(p.Main)}); the value changes with the light level.");
        }

        // ---------- Follow-ups ----------

        sealed class FollowUp
        {
            public Regex Pattern;
            public System.Func<Plan, string> Text;
            public System.Func<Plan, IEnumerable<(PinRecord pin, string role)>> Pins;
        }

        static IEnumerable<(PinRecord, string)> None(Plan p) => new (PinRecord, string)[0];
        static IEnumerable<(PinRecord, string)> MainPin(Plan p) => new[] { (p.Main, MainRole(p.CircuitId)) };
        static IEnumerable<(PinRecord, string)> GroundPins(Plan p) => p.Grounds.Select(g => (g, PinRoles.Ground));

        static FollowUp F(string pattern, System.Func<Plan, string> text, System.Func<Plan, IEnumerable<(PinRecord, string)>> pins) =>
            new() { Pattern = new Regex(pattern), Text = text, Pins = pins };

        // "Which pin does that connect to?" / "where does it go?" - only with a pronoun, so "Which pin is SCL?" or
        // "Which pin is the built-in LED on?" stay ordinary questions while a circuit is active.
        const string k_WhichPinPronoun = @"\b(?:which|what) pin (?:does|do|is) (?:that|it|this|they)\b|\bwhere does (?:that|it) (?:go|connect)\b";

        // Checked in order; the first match answers. Patterns name a part of the circuit or a specific concern,
        // so ordinary questions ("Which pins support PWM?") are never captured by an active circuit.
        static readonly Dictionary<string, FollowUp[]> s_FollowUps = new()
        {
            [Led] = new[]
            {
                F(@"\b(?:where|which pin)\b.*\bresistor\b|\bresistor\b.*\b(?:go|goes|connect|connects)\b",
                  p => $"The resistor goes between {Pin(p)} and the LED's anode (longer leg). It can also sit between the cathode and GND; either way it limits the current.", MainPin),
                F(@"\bwhy\b.*\bresistor\b|\bresistor\b.*\b(?:for|needed|necessary|purpose)\b|\bneed (?:a |the )?resistor\b",
                  p => "The resistor limits the current through the LED. Without it the LED would draw as much current as the pin can push, which can damage both the LED and the pin. Its value depends on the supply voltage, the LED's forward voltage and the current you want.", None),
                F(@"\b(?:revers\w*|backwards?|wrong way|other way round|upside down|flip|flipped|swap|swapped)\b",
                  p => "It won't light. An LED only conducts in one direction, from the anode (longer leg) to the cathode (shorter leg), so turn it round so the longer leg faces the resistor.", None),
                F(@"\b(?:cathode|short(?:er)? leg|negative leg|flat side)\b",
                  p => "The LED's cathode (shorter leg, flat side) connects to GND.", GroundPins),
                F(@"\b(?:anode|long(?:er)? leg|positive leg)\b",
                  p => $"The LED's anode (longer leg) connects through the resistor to {Pin(p)}.", MainPin),
                F(k_WhichPinPronoun + @"|\b(?:which|what) pin (?:does|do|is) the led\b|\bwhere does the led (?:go|connect)\b",
                  p => $"The LED is driven from {Pin(p)}, through the resistor; its cathode goes to GND.", MainPin),
            },
            [Button] = new[]
            {
                F(@"\bresistor\b|\bpull ?(?:up|down)\b|\binput_pullup\b",
                  p => $"Not with INPUT_PULLUP: the board's internal pull-up resistor holds {p.Main.Id} HIGH until the button pulls it LOW. Without INPUT_PULLUP you would need an external pull-up or pull-down resistor so the pin doesn't float.", None),
                F(@"\bother (?:side|leg|pin|end)\b|\bsecond (?:side|leg)\b",
                  p => "The other side of the button connects to GND.", GroundPins),
                F(@"\b(?:low|high|inverted|backwards|reads? (?:0|1|zero|one))\b.*\bpress|\bpress\w*\b.*\b(?:low|high)\b",
                  p => "With INPUT_PULLUP the pin reads HIGH when the button is released and LOW while it is pressed; that is expected.", None),
                F(@"\bbutton\b.*\b(?:on|pin|connect\w*|go|goes)\b|" + k_WhichPinPronoun,
                  p => $"The button connects to {Pin(p)} on one side and GND on the other.", MainPin),
            },
            [Potentiometer] = new[]
            {
                F(@"\b(?:wiper|middle (?:leg|pin|terminal)|cent(?:re|er) (?:leg|pin|terminal))\b",
                  p => $"The wiper (middle leg) goes to {Pin(p)}.", MainPin),
                F(@"\b(?:outer|outside|end|side) (?:legs|pins|terminals)\b|\bother (?:two )?(?:legs|pins)\b",
                  p => "The outer legs go to 5V and GND. Swapping them only reverses the direction of the reading.",
                  p => (p.Supply != null ? new[] { (p.Supply, PinRoles.Power) } : new (PinRecord, string)[0]).Concat(GroundPins(p))),
                F(@"\b(?:what values?|range|1023|how do i read|what number)\b",
                  p => $"analogRead({CodeName(p.Main)}) returns 0 to 1023 on ATmega328P-based boards (10-bit ADC): about 0 with the wiper at the GND end and about 1023 at the 5V end.", MainPin),
                F(k_WhichPinPronoun,
                  p => $"The wiper (middle leg) goes to {Pin(p)}; the outer legs go to 5V and GND.", MainPin),
            },
            [Ldr] = new[]
            {
                F(@"\bresistor\b",
                  p => $"The fixed resistor goes from {p.Main.Id} to GND; together with the LDR it forms a voltage divider, so the voltage on {p.Main.Id} changes with the light.",
                  p => MainPin(p).Concat(GroundPins(p))),
                F(k_WhichPinPronoun + @"|\bwhere does the ldr (?:go|connect)\b",
                  p => $"The LDR connects between 5V and {Pin(p)}.", MainPin),
            },
        };

        /// <summary>
        /// Answers a follow-up about the active circuit (or about a circuit the question names, e.g. "the wiper").
        /// Returns false when the question is not a circuit follow-up, so the normal pipeline handles it.
        /// </summary>
        public static bool TryFollowUp(AssistantResponse resp, ClassifiedIntent intent, ExtractedEntities e, BoardProfile board, DialogueContext ctx, string normalized)
        {
            if (board == null || intent.Kind is QueryKind.Unsupported or QueryKind.Compare || (e.HasConnectCue && e.Component != null)) return false;
            string circuit = e.Component;
            string pinId = null;
            if (circuit == null && ctx != null && ctx.ActiveCircuit != null && ctx.BoardId == board.Id)
            {
                circuit = ctx.ActiveCircuit;
                pinId = ctx.ActiveCircuitPin;
            }
            else if (circuit != null && ctx != null && ctx.ActiveCircuit == circuit && ctx.BoardId == board.Id)
                pinId = ctx.ActiveCircuitPin;
            if (circuit == null || !s_FollowUps.TryGetValue(circuit, out var followUps)) return false;

            var match = followUps.FirstOrDefault(f => f.Pattern.IsMatch(normalized));
            if (match == null) return false;
            var plan = BuildPlan(circuit, board, pinId);
            if (plan.Problem != null) return false;

            intent.CircuitId = circuit;
            intent.CircuitFollowUp = true;
            intent.Kind = QueryKind.Connect;
            intent.Confidence = 0.8f;
            resp.Kind = QueryKind.Connect;
            resp.Topic = intent.Topic;
            resp.IntentId = intent.Id;
            resp.IntentTitle = "Follow-up: " + Title(circuit);
            resp.Confidence = intent.Confidence;
            resp.UsedContext = e.Component == null;

            var r = new RetrievalResult();
            foreach (var (pin, role) in match.Pins(plan)) AddPin(r, pin, role);
            AddFacts(r, plan);
            Finish(resp, intent, r, board, match.Text(plan), resp.UsedContext ? $"Interpreted as a follow-up about the {Title(circuit)} circuit." : null);
            return true;
        }

        // ---------- Shared ----------

        static void AddPin(RetrievalResult r, PinRecord pin, string role)
        {
            if (pin == null || r.Pins.Contains(pin)) return;
            r.Pins.Add(pin);
            r.Roles[pin.Id] = role;
        }

        static void AddPlanPins(RetrievalResult r, Plan p)
        {
            AddPin(r, p.Main, MainRole(p.CircuitId));
            if (p.Supply != null) AddPin(r, p.Supply, PinRoles.Power);
            foreach (var g in p.Grounds) AddPin(r, g, PinRoles.Ground);
        }

        /// <summary>Evidence: the capability facts of the pins used (with their own status) and the general guidance source.</summary>
        static void AddFacts(RetrievalResult r, Plan p)
        {
            if (p.Main == null) return;
            var facts = KnowledgeBase.For(p.Board);
            r.Facts.AddRange(facts.Where(f => f.Kind == FactKind.PinCapability && f.PinId == p.Main.Id &&
                                              (f.Capability == p.MainCapability || (p.CircuitId == Led && f.Capability == PinCapability.Pwm))));
            r.Facts.Add(new KnowledgeFact
            {
                Id = "circuit." + p.CircuitId, Kind = FactKind.Concept, Topic = "circuit_" + p.CircuitId,
                Text = $"{Title(p.CircuitId)} wiring guidance.", Source = k_Source, Status = VerificationStatus.General,
            });
        }

        static void Finish(AssistantResponse resp, ClassifiedIntent intent, RetrievalResult r, BoardProfile board, string answer, string contextNote = null)
        {
            resp.RetrievedFacts.AddRange(r.Facts);
            if (!resp.AboutOtherBoard)
            {
                resp.HighlightPins.AddRange(r.Pins);
                foreach (var kv in r.Roles) resp.PinRoles[kv.Key] = kv.Value;
                resp.Legend = PinRoles.Legend(resp.PinRoles.Values);
            }
            resp.Answer = answer;
            AnswerGenerator.AddEvidenceAndCaveats(resp, intent, r, board, contextNote);
        }
    }
}
