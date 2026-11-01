using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ArduinoAR.Tests
{
    /// <summary>
    /// Evaluation set v2: circuit wiring guides (LED + series resistor, push button, potentiometer/analog input,
    /// I2C), circuit follow-ups, board selection, pin highlighting and safety. Written and frozen BEFORE the
    /// circuit guides were implemented, from the approved scope and the board profiles; run first against the
    /// unchanged implementation as a baseline. Reports metrics only (never fails); behaviour is asserted by the
    /// unit tests. The v1 set (NlpEvaluationTests) is unchanged.
    ///
    /// Intended intent ids: circuit.led, circuit.button, circuit.potentiometer; I2C keeps i2c.connect.
    /// Circuit highlights: the signal pin plus every GND pin (and 5V for the potentiometer), as for I2C wiring.
    /// Defaults: LED on D9, button on D2, potentiometer wiper on A0 (both boards). Answer checks are
    /// case-insensitive regular expressions over the full display text.
    /// </summary>
    public class NlpEvaluationV2Tests
    {
        const string Spd = "spd", Uno = "uno";

        sealed class Turn
        {
            public string Board, Category, Question;
            public string Intent;          // expected intent id; "a|b" = any of; null = not checked
            public string[] Pins;          // expected resolved pin ids (set); null = not checked
            public string[] Highlight;     // expected highlight ids (set); null = not checked
            public string[] Has = new string[0];
            public string[] HasNot = new string[0];
            public int Chain = -1;
        }

        static readonly string[] k_Gnd = { "GND_TOP", "GND1", "GND2" };
        static readonly string[] k_None = new string[0];
        static readonly string[] k_NoRatings = { @"\b\d+ ?ma\b", @"\b\d+(\.\d+)? ?(mw|w)\b", @"\b7 ?(-|to) ?12 ?v", @"\b6 ?(-|to) ?(17|20) ?v" };
        static readonly string[] k_NotTested = { @"not (been )?(physically )?tested|untested|not verified on a (real|physical) circuit" };

        static string[] S(params string[] s) => s;
        static string[] Cat(params string[][] parts) => parts.SelectMany(p => p).ToArray();

        static Turn T(string board, string cat, string q, string intent = null, string[] pins = null, string[] hl = null,
            string[] has = null, string[] hasNot = null, int chain = -1) =>
            new() { Board = board, Category = cat, Question = q, Intent = intent, Pins = pins, Highlight = hl,
                    Has = has ?? new string[0], HasNot = hasNot ?? new string[0], Chain = chain };

        static readonly string[] k_LedHas = { @"\bresistor\b", @"\bd9\b", @"anode|long(er)? leg", @"cathode|short(er)? leg", @"\bgnd\b",
                                              @"forward voltage", @"220", @"1 ?k", @"not (universally|always) suitable|check" };

        static readonly Turn[] k_Set =
        {
            // ---------- LED + series resistor ----------
            T(Spd, "led", "How do I connect an LED?", "circuit.led", hl: Cat(S("D9"), k_Gnd), has: Cat(k_LedHas, k_NotTested), hasNot: Cat(k_NoRatings, S("unverified"))),
            T(Spd, "led", "How do I wire an LED with a resistor?", "circuit.led", hl: Cat(S("D9"), k_Gnd), hasNot: k_NoRatings),
            T(Spd, "led", "Hook up an external LED to the board", "circuit.led", hl: Cat(S("D9"), k_Gnd)),
            T(Spd, "led", "How do I connect an LED to pin 6?", "circuit.led", pins: S("D6"), hl: Cat(S("D6"), k_Gnd)),
            T(Spd, "led", "Connect an LED to pin 4", "circuit.led", pins: S("D4"), hl: Cat(S("D4"), k_Gnd)),
            T(Uno, "led", "How do I connect an LED?", "circuit.led", hl: Cat(S("D9"), k_Gnd), has: k_LedHas, hasNot: Cat(k_NoRatings, S("unverified"))),
            T(Uno, "led", "How do I wire up a light emitting diode?", "circuit.led", hl: Cat(S("D9"), k_Gnd)),

            // ---------- Push button ----------
            T(Spd, "button", "How do I connect a push button?", "circuit.button", hl: Cat(S("D2"), k_Gnd),
                has: Cat(S(@"\bd2\b", @"\bgnd\b", @"input_pullup|internal pull-?up"), k_NotTested), hasNot: k_NoRatings),
            T(Spd, "button", "How do I wire a pushbutton switch?", "circuit.button", hl: Cat(S("D2"), k_Gnd)),
            T(Spd, "button", "Connect a button to pin 7", "circuit.button", pins: S("D7"), hl: Cat(S("D7"), k_Gnd)),
            T(Uno, "button", "How do I hook up a tactile switch?", "circuit.button", hl: Cat(S("D2"), k_Gnd)),

            // ---------- Potentiometer / analog sensor ----------
            T(Spd, "pot", "How do I connect a potentiometer?", "circuit.potentiometer", hl: Cat(S("A0", "5V"), k_Gnd),
                has: Cat(S(@"\ba0\b", @"wiper", @"\b5v\b", @"\bgnd\b"), k_NotTested), hasNot: Cat(k_NoRatings, S("unverified"))),
            T(Spd, "pot", "How do I wire a pot to the board?", "circuit.potentiometer", hl: Cat(S("A0", "5V"), k_Gnd)),
            T(Spd, "pot", "Connect a potentiometer to A2", "circuit.potentiometer", pins: S("A2"), hl: Cat(S("A2", "5V"), k_Gnd)),
            T(Spd, "pot", "Connect a potentiometer to pin 7", has: S(@"analog"), hasNot: S(@"\bd7\b.*wiper")),
            T(Uno, "pot", "How do I connect a potentiometer?", "circuit.potentiometer", hl: Cat(S("A0", "5V"), k_Gnd)),
            T(Uno, "pot", "How do I connect a light sensor (LDR)?", "circuit.potentiometer|circuit.ldr", has: S(@"\ba0\b", @"resistor")),

            // ---------- I2C must be preserved ----------
            T(Spd, "i2c", "How do I connect an I2C sensor?", "i2c.connect", hl: Cat(S("A4", "A5", "5V", "3V3"), k_Gnd), has: S("unverified")),
            T(Uno, "i2c", "How do I wire an OLED display?", "i2c.connect", hl: Cat(S("SDA", "SCL", "5V", "3V3"), k_Gnd), hasNot: S("unverified")),

            // ---------- Follow-ups inside a circuit ----------
            T(Spd, "followup", "How do I connect an LED?", "circuit.led", chain: 1),
            T(Spd, "followup", "Why do I need a resistor?", hl: k_None, has: S(@"current"), hasNot: k_NoRatings, chain: 1),
            T(Spd, "followup", "What if I reverse the LED?", hl: k_None, has: S(@"(won't|will not|doesn't|does not) light|not light", @"one direction"), hasNot: k_NoRatings, chain: 1),
            T(Spd, "followup", "Where does the resistor go?", hl: S("D9"), has: S(@"\bd9\b"), chain: 1),
            T(Spd, "followup", "Which pin does the short leg connect to?", hl: k_Gnd, has: S(@"\bgnd\b"), chain: 1),

            T(Spd, "followup", "How do I connect a potentiometer?", "circuit.potentiometer", chain: 2),
            T(Spd, "followup", "Where does the wiper go?", hl: S("A0"), has: S(@"\ba0\b"), chain: 2),
            T(Spd, "followup", "Where do the outer legs go?", hl: Cat(S("5V"), k_Gnd), has: S(@"\b5v\b", @"\bgnd\b"), chain: 2),

            T(Uno, "followup", "How do I connect a push button?", "circuit.button", chain: 3),
            T(Uno, "followup", "Do I need a resistor?", has: S(@"input_pullup|internal pull-?up"), chain: 3),
            T(Uno, "followup", "Which pin is the button on?", hl: S("D2"), has: S(@"\bd2\b"), chain: 3),

            // a circuit context must not hijack ordinary questions
            T(Spd, "followup", "How do I connect an LED?", "circuit.led", chain: 4),
            T(Spd, "followup", "Which pins support PWM?", "pwm.list", hl: S("D3", "D5", "D6", "D9", "D10", "D11"), chain: 4),
            T(Spd, "followup", "What does pin 13 do?", "pin.lookup", pins: S("D13"), chain: 4),

            // ---------- Questions without circuit context ----------
            T(Spd, "nocontext", "Where does the wiper go?", hl: S("A0"), has: S(@"\ba0\b")),
            T(Spd, "nocontext", "Why do I need a resistor for an LED?", hl: k_None, has: S(@"current"), hasNot: k_NoRatings),
            T(Spd, "nocontext", "What if I reverse the LED?", hl: k_None, has: S(@"one direction")),

            // ---------- Board selection ----------
            T(Spd, "board", "How do I connect an LED to the Uno?", hl: k_None, has: S(@"\bd9\b", @"\buno\b")),
            T(Uno, "board", "How do I connect a potentiometer to the SPDUINO?", hl: k_None, has: S(@"\ba0\b", "spduino")),
            T(Uno, "board", "How do I connect a button?", "circuit.button", hl: Cat(S("D2"), k_Gnd), hasNot: S("unverified")),

            // ---------- Safety ----------
            T(Spd, "safety", "Can I connect an LED directly to pin 9 without a resistor?", hl: S("D9"), has: S(@"resistor"), hasNot: Cat(k_NoRatings, S(@"^yes"))),
            T(Spd, "safety", "What current does an LED need?", hasNot: k_NoRatings),
            T(Spd, "safety", "Can I power a motor from pin 9?", has: S(@"driver|transistor"), hasNot: k_NoRatings),
            T(Spd, "safety", "Can I feed 15 volts into VIN?", "unsupported", hl: k_None, hasNot: k_NoRatings),
            T(Spd, "safety", "How do I connect an SPI device?", "spi.list|spi.explain", hl: S("D10", "D11", "D12", "D13")),
        };

        sealed class Score
        {
            public int Total, Pass;
            public void Add(bool ok) { Total++; if (ok) Pass++; }
            public override string ToString() => Total == 0 ? "n/a" : $"{Pass}/{Total} ({100.0 * Pass / Total:0.0}%)";
        }

        [Test]
        public void EvaluateV2_ReportsMetrics()
        {
            var intent = new Score(); var pins = new Score(); var highlight = new Score(); var content = new Score(); var turns = new Score();
            var byCategory = new SortedDictionary<string, Score>();
            var failures = new List<string>();
            var contexts = new Dictionary<int, DialogueContext>();

            foreach (var t in k_Set)
            {
                var board = t.Board == Spd ? SpduinoProfile.Profile : ArduinoUnoProfile.Profile;
                DialogueContext ctx = null;
                if (t.Chain >= 0 && !contexts.TryGetValue(t.Chain, out ctx)) contexts[t.Chain] = ctx = new DialogueContext();
                var r = BoardAssistant.Process(t.Question, board, ctx);
                string text = r.ToDisplayText(10);
                var why = new List<string>();
                if (t.Intent != null) { bool ok = t.Intent.Split('|').Contains(r.IntentId); intent.Add(ok); if (!ok) why.Add($"intent {r.IntentId} != {t.Intent}"); }
                if (t.Pins != null) { bool ok = new HashSet<string>(r.ResolvedPinIds).SetEquals(t.Pins); pins.Add(ok); if (!ok) why.Add($"pins [{string.Join(",", r.ResolvedPinIds)}] != [{string.Join(",", t.Pins)}]"); }
                if (t.Highlight != null) { bool ok = new HashSet<string>(r.HighlightIds).SetEquals(t.Highlight); highlight.Add(ok); if (!ok) why.Add($"highlight [{string.Join(",", r.HighlightIds)}] != [{string.Join(",", t.Highlight)}]"); }
                foreach (var re in t.Has) { bool ok = Regex.IsMatch(text, re, RegexOptions.IgnoreCase); content.Add(ok); if (!ok) why.Add($"missing /{re}/"); }
                foreach (var re in t.HasNot) { bool ok = !Regex.IsMatch(text, re, RegexOptions.IgnoreCase | RegexOptions.Multiline); content.Add(ok); if (!ok) why.Add($"must not contain /{re}/"); }

                bool pass = why.Count == 0;
                turns.Add(pass);
                if (!byCategory.TryGetValue(t.Category, out var cs)) byCategory[t.Category] = cs = new Score();
                cs.Add(pass);
                if (!pass) failures.Add($"[{t.Category}/{t.Board}] \"{t.Question}\": {string.Join("; ", why)}");
            }

            var report = new StringBuilder();
            report.AppendLine("NLP EVALUATION V2 (circuits)");
            report.AppendLine($"Turns fully correct : {turns}");
            report.AppendLine($"Intent accuracy     : {intent}");
            report.AppendLine($"Pin extraction      : {pins}");
            report.AppendLine($"Highlight set       : {highlight}");
            report.AppendLine($"Answer content      : {content}");
            report.AppendLine("By category (turns fully correct):");
            foreach (var kv in byCategory) report.AppendLine($"  {kv.Key,-10} {kv.Value}");
            report.AppendLine($"Failures ({failures.Count}):");
            foreach (var f in failures) report.AppendLine("  " + f);
            TestContext.WriteLine(report.ToString());
            UnityEngine.Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
