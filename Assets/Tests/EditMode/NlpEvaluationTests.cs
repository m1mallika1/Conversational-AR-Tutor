using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ArduinoAR.Tests
{
    /// <summary>
    /// Labelled NLP evaluation set with per-category metrics. Labels describe the intended behaviour
    /// (written from the board profiles, documented facts and the approved feature scope before the
    /// features were implemented), so the same set measures the baseline and later versions.
    /// This test REPORTS metrics (in the test output) and does not fail on them; feature behaviour is
    /// asserted by the regular unit tests.
    /// Answer checks are case-insensitive regular expressions over the full display text.
    /// </summary>
    public class NlpEvaluationTests
    {
        const string Spd = "spd", Uno = "uno";

        sealed class Turn
        {
            public string Board, Category, Question;
            public string Intent;          // expected intent id; "a|b" = any of; null = not checked
            public string[] Pins;          // expected resolved pin ids (set); null = not checked
            public string[] Highlight;     // expected highlight ids (set); null = not checked
            public string[] Has = new string[0];     // regexes the answer must match
            public string[] HasNot = new string[0];  // regexes the answer must not match
            public bool OutOfScope;        // must be refused/unknown or say "no record", with no highlights
            public int Chain = -1;         // turns with the same chain id share dialogue context, in order
        }

        static readonly string[] k_Pwm = { "D3", "D5", "D6", "D9", "D10", "D11" };
        static readonly string[] k_Analog = { "A0", "A1", "A2", "A3", "A4", "A5" };
        static readonly string[] k_Spi = { "D10", "D11", "D12", "D13" };
        static readonly string[] k_Gnd = { "GND_TOP", "GND1", "GND2" };
        static readonly string[] k_None = new string[0];
        static readonly string[] k_NoRatings = { @"\b40 ?ma\b", @"\b50 ?ma\b", @"\b7 ?(-|to) ?12\b", @"\b6 ?(-|to) ?17\b", @"\b6 ?(-|to) ?20\b" };

        static Turn T(string board, string cat, string q, string intent = null, string[] pins = null, string[] hl = null,
            string[] has = null, string[] hasNot = null, bool oos = false, int chain = -1) =>
            new() { Board = board, Category = cat, Question = q, Intent = intent, Pins = pins, Highlight = hl,
                    Has = has ?? new string[0], HasNot = hasNot ?? new string[0], OutOfScope = oos, Chain = chain };

        static string[] S(params string[] s) => s;
        static string[] Cat(params string[][] parts) => parts.SelectMany(p => p).ToArray();

        static readonly Turn[] k_Set =
        {
            // ---------- intent: paraphrases not used in the unit tests ----------
            T(Spd, "intent", "Which pins can I use for PWM output?", "pwm.list", hl: k_Pwm),
            T(Spd, "intent", "Which pins give analog output?", "pwm.list", hl: k_Pwm),
            T(Spd, "intent", "Which pins can read a potentiometer?", "analog.list", hl: k_Analog),
            T(Spd, "intent", "Where are the analog input pins?", "analog.list", hl: k_Analog),
            T(Spd, "intent", "Which pins are the ground pins?", "ground.list", hl: k_Gnd),
            T(Spd, "intent", "Where is the reset pin?", "reset.list|pin.lookup", hl: S("RESET")),
            T(Spd, "intent", "Which pins are used for serial communication?", "serial.list", hl: S("D0", "D1")),
            T(Spd, "intent", "Which pins are for UART?", "serial.list", hl: S("D0", "D1")),
            T(Spd, "intent", "What pins does SPI use?", "spi.list", hl: k_Spi),
            T(Spd, "intent", "Which pins support external interrupts?", "interrupt.list", hl: S("D2", "D3")),
            T(Spd, "intent", "Which pin is the built-in LED on?", "led.list", hl: S("D13")),
            T(Spd, "intent", "Where is AREF?", "aref.list", hl: S("AREF")),
            T(Spd, "intent", "Explain PWM", "pwm.explain"),
            T(Spd, "intent", "What is SPI?", "spi.explain"),
            T(Spd, "intent", "What does UART mean?", "serial.explain"),
            T(Spd, "intent", "How does an interrupt work?", "interrupt.explain"),
            T(Spd, "intent", "What is analogRead used for?", "analog.explain"),
            T(Spd, "intent", "What microcontroller does this board use?", "board.info", has: S("atmega")),
            T(Spd, "intent", "Which USB chip is on the board?", "board.info", has: S("ft232rl")),
            T(Uno, "intent", "Which pins support PWM on the Uno?", "pwm.list", hl: k_Pwm),
            T(Uno, "intent", "Where are the SDA and SCL pins?", "i2c.list", hl: S("A4", "A5", "SDA", "SCL")),
            T(Uno, "intent", "Which pins are 5V and 3.3V?", "power.list"),
            T(Uno, "intent", "What is the USB-to-serial chip on the Uno?", "board.info", has: S("16u2")),
            T(Uno, "intent", "Which pins can drive a servo?", "pwm.list", hl: k_Pwm),
            T(Uno, "intent", "Which pins can I use with the Wire library?", "i2c.list", hl: S("A4", "A5", "SDA", "SCL")),

            // ---------- entity: pin extraction ----------
            T(Spd, "entity", "What does pin 7 do?", "pin.lookup", pins: S("D7"), hl: S("D7")),
            T(Spd, "entity", "What does A3 do?", "pin.lookup", pins: S("A3"), hl: S("A3")),
            T(Spd, "entity", "Tell me about digital pin 12", "pin.lookup", pins: S("D12")),
            T(Spd, "entity", "Do pins 10 and 11 support PWM?", "pwm.capabilitycheck", pins: S("D10", "D11"), hl: S("D10", "D11"), has: S(@"\byes\b")),
            T(Spd, "entity", "Is pin 4 a PWM pin?", "pwm.capabilitycheck", pins: S("D4"), has: S(@"\bno\b")),
            T(Spd, "entity", "Can analog pin 2 read a sensor?", "analog.capabilitycheck", pins: S("A2"), has: S(@"\byes\b")),
            T(Uno, "entity", "What does the VIN pin do?", "pin.lookup", pins: S("VIN")),
            T(Uno, "entity", "What is the 5V pin for?", "pin.lookup", pins: S("5V")),
            T(Uno, "entity", "Does pin 13 have an LED?", "led.capabilitycheck", pins: S("D13"), has: S(@"\byes\b")),
            T(Uno, "entity", "Is A0 an analog input?", "analog.capabilitycheck", pins: S("A0"), has: S(@"\byes\b")),
            T(Spd, "entity", "what does pin thirteen do", "pin.lookup", pins: S("D13")),
            T(Spd, "entity", "does pin three support interrupts", "interrupt.capabilitycheck", pins: S("D3"), has: S("probably", "unverified")),

            // ---------- voice: speech-to-text forms (the first and last two are real phone transcripts) ----------
            T(Uno, "voice", "where are the I to see pins", "i2c.list", hl: S("A4", "A5", "SDA", "SCL")),
            T(Spd, "voice", "what is I squared C", "i2c.explain"),
            T(Spd, "voice", "which pins support p w m", "pwm.list", hl: k_Pwm),
            T(Spd, "voice", "where is the s d a pin", hl: S("A4"), has: S(@"\ba4\b")),
            T(Uno, "voice", "what does pin a zero do", "pin.lookup", pins: S("A0")),
            T(Spd, "voice", "difference between speed no and arduino", "board.compare"),
            T(Spd, "voice", "difference between Speedo and arduino", "board.compare"),

            // ---------- connect: wiring an I2C sensor ----------
            T(Spd, "connect", "How do I connect an I2C sensor?", "i2c.connect", hl: Cat(S("A4", "A5", "5V", "3V3"), k_Gnd),
                has: S(@"\bsda\b", @"\bscl\b", @"\ba4\b", @"\ba5\b", @"\bgnd\b", "unverified", "datasheet"), hasNot: k_NoRatings),
            T(Spd, "connect", "How do I wire an OLED display to this board?", "i2c.connect", hl: Cat(S("A4", "A5", "5V", "3V3"), k_Gnd), hasNot: k_NoRatings),
            T(Spd, "connect", "How should I hook up a BME280?", "i2c.connect", hl: Cat(S("A4", "A5", "5V", "3V3"), k_Gnd)),
            T(Spd, "connect", "Connect an MPU6050 sensor", "i2c.connect", hl: Cat(S("A4", "A5", "5V", "3V3"), k_Gnd)),
            T(Spd, "connect", "how do I connect an eye to see sensor", "i2c.connect"),
            T(Spd, "connect", "Which wires go where for an I2C module?", "i2c.connect"),
            T(Uno, "connect", "How do I connect an I2C sensor to the Uno?", "i2c.connect", hl: Cat(S("SDA", "SCL", "5V", "3V3"), k_Gnd),
                has: S(@"\bsda\b", @"\bscl\b", @"\ba4\b", @"\ba5\b", "datasheet"), hasNot: Cat(k_NoRatings, S("unverified"))),
            T(Uno, "connect", "How do I wire an I2C LCD?", "i2c.connect", hl: Cat(S("SDA", "SCL", "5V", "3V3"), k_Gnd)),
            T(Spd, "connect", "How do I use I2C?", "i2c.explain"),
            T(Spd, "connect", "Which pins are I2C?", "i2c.list", hl: S("A4", "A5")),
            T(Spd, "connect", "How do I connect an SPI device?", "spi.list|spi.explain", has: S(@"\bd10\b")),
            T(Spd, "connect", "How do I connect a sensor to pin A0?", pins: S("A0"), hasNot: S(@"\bsda\b")),

            // ---------- compare / attributes ----------
            T(Spd, "compare", "What is the difference between the SPDUINO and the Arduino Uno?", "board.compare", has: S("ft232rl", "16u2")),
            T(Spd, "compare", "Compare this board with the Uno", "board.compare", has: S("ft232rl", "16u2")),
            T(Uno, "compare", "How is the Uno different from the SPDUINO?", "board.compare", has: S("ft232rl", "16u2")),
            T(Spd, "compare", "SPDUINO vs Uno", "board.compare"),
            T(Spd, "compare", "Does the SPDUINO have SDA and SCL pins like the Uno?", "board.compare|board.info", has: S(@"\bsda\b")),
            T(Spd, "attribute", "What is the flash memory size?", "board.info", has: S(@"\b32 ?kb\b")),
            T(Spd, "attribute", "How much SRAM does it have?", "board.info", has: S(@"\b2 ?kb\b")),
            T(Spd, "attribute", "What is the clock speed?", "board.info", has: S(@"\b16 ?mhz\b")),
            T(Uno, "attribute", "What is the clock speed of the Uno?", "board.info", has: S(@"\b16 ?mhz\b")),
            T(Spd, "attribute", "What is the operating voltage?", "board.info", has: S("operating", @"\b5 ?v\b"), hasNot: k_NoRatings),
            T(Uno, "attribute", "How many analog inputs does the Uno have?", "analog.list", hl: k_Analog, has: S(@"\b6\b|\bsix\b")),
            T(Spd, "attribute", "How many PWM pins are there?", "pwm.list", hl: k_Pwm, has: S(@"\b6\b|\bsix\b")),
            T(Spd, "attribute", "What connectors does the board have?", "board.info", has: S(@"\busb\b")),
            T(Uno, "attribute", "What are the dimensions of the Uno?", "board.info", has: S(@"68\.6")),
            T(Spd, "attribute", "How big is this board?", "board.info", has: S(@"\b65\b")),

            // ---------- wrong board: the question names the board that is not in view ----------
            T(Spd, "wrongboard", "Does the Uno have SDA pins?", hl: k_None, has: S(@"\buno\b", @"\bsda\b")),
            T(Uno, "wrongboard", "Where are the I2C pins on the SPDUINO?", hl: k_None, has: S("spduino", @"\ba4\b", "unverified")),
            T(Spd, "wrongboard", "Which pins on the Arduino Uno support PWM?", hl: k_None, has: S(@"\buno\b", @"\bd9\b")),
            T(Spd, "wrongboard", "What USB chip does the Uno use?", hl: k_None, has: S("16u2")),

            // ---------- out of scope / unsupported: no confident in-scope answer ----------
            T(Spd, "outofscope", "What is the weather today?", oos: true),
            T(Spd, "outofscope", "How do I make a robot?", oos: true),
            T(Spd, "outofscope", "What is a capacitor?", oos: true),
            T(Spd, "outofscope", "Play some music", oos: true),
            T(Spd, "outofscope", "Can this board run Python?", oos: true),
            T(Uno, "outofscope", "What is the price of the Uno?", oos: true),
            T(Spd, "outofscope", "Does this board have WiFi?", oos: true, has: S("no record")),
            T(Uno, "outofscope", "Does the Uno have Bluetooth?", oos: true, has: S("no record")),
            T(Spd, "outofscope", "Does it have an Ethernet port?", oos: true, has: S("no record")),
            T(Spd, "outofscope", "How much current can a pin supply?", "unsupported", hasNot: k_NoRatings),
            T(Spd, "outofscope", "What is the maximum current of the 3.3V pin?", "unsupported", hasNot: k_NoRatings),
            T(Spd, "outofscope", "What is the input voltage range?", "unsupported", hasNot: k_NoRatings),
            T(Spd, "outofscope", "Can I connect a 12V motor directly to pin 9?", "unsupported", hasNot: k_NoRatings),
            T(Uno, "outofscope", "Is it safe to put 9V into the 5V pin?", "unsupported", hasNot: k_NoRatings),

            // ---------- follow-up chains ----------
            T(Spd, "followup", "Which pins support PWM?", "pwm.list", hl: k_Pwm, chain: 1),
            T(Spd, "followup", "What about pin 4?", "pwm.capabilitycheck", pins: S("D4"), has: S(@"\bno\b"), chain: 1),
            T(Spd, "followup", "And pin 5?", "pwm.capabilitycheck", pins: S("D5"), has: S(@"\byes\b"), chain: 1),
            T(Spd, "followup", "Does it support interrupts?", "interrupt.capabilitycheck", pins: S("D5"), has: S(@"\bno\b"), chain: 1),

            T(Spd, "followup", "Where are the I2C pins?", "i2c.list", hl: S("A4", "A5"), chain: 2),
            T(Spd, "followup", "Which one is SDA?", hl: S("A4"), has: S(@"\ba4\b"), chain: 2),
            T(Spd, "followup", "Tell me more", "i2c.explain", chain: 2),

            T(Uno, "followup", "What does pin 13 do?", "pin.lookup", pins: S("D13"), chain: 3),
            T(Uno, "followup", "Is it PWM?", "pwm.capabilitycheck", pins: S("D13"), has: S(@"\bno\b"), chain: 3),
            T(Uno, "followup", "Does it support SPI?", "spi.capabilitycheck", pins: S("D13"), has: S(@"\byes\b"), chain: 3),

            T(Spd, "followup", "Tell me about Bluetooth", chain: 4),
            T(Spd, "followup", "Does this board have WiFi?", hl: k_None, has: S("no record"), oos: true, chain: 4),

            T(Spd, "followup", "How do I connect an I2C sensor?", "i2c.connect", chain: 5),
            T(Spd, "followup", "Which pin is SCL?", hl: S("A5"), has: S(@"\ba5\b"), chain: 5),
            T(Spd, "followup", "What about SPI?", "spi.list", hl: k_Spi, chain: 5),

            T(Uno, "followup", "What is PWM?", "pwm.explain", chain: 6),
            T(Uno, "followup", "Which pins?", "pwm.list", hl: k_Pwm, chain: 6),
            T(Uno, "followup", "What about analog?", "analog.list", hl: k_Analog, chain: 6),

            T(Spd, "followup", "Which pins support PWM?", "pwm.list", chain: 7),
            T(Spd, "followup", "What is the weather today?", oos: true, chain: 7),
            T(Spd, "followup", "Which pins are used for SPI?", "spi.list", hl: k_Spi, chain: 7),

            T(Spd, "followup", "What does pin 2 do?", "pin.lookup", pins: S("D2"), chain: 8),
            T(Spd, "followup", "How do I use it?", "pin.lookup", pins: S("D2"), chain: 8),
        };

        sealed class Score
        {
            public int Total, Pass;
            public void Add(bool ok) { Total++; if (ok) Pass++; }
            public override string ToString() => Total == 0 ? "n/a" : $"{Pass}/{Total} ({100.0 * Pass / Total:0.0}%)";
        }

        static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) =>
            new HashSet<string>(a).SetEquals(b);

        [Test]
        public void Evaluate_ReportsMetrics()
        {
            var intent = new Score(); var pins = new Score(); var highlight = new Score(); var content = new Score(); var oos = new Score();
            var turns = new Score();
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

                if (t.Intent != null)
                {
                    bool ok = t.Intent.Split('|').Contains(r.IntentId);
                    intent.Add(ok);
                    if (!ok) why.Add($"intent {r.IntentId} != {t.Intent}");
                }
                if (t.Pins != null)
                {
                    bool ok = SameSet(r.ResolvedPinIds, t.Pins);
                    pins.Add(ok);
                    if (!ok) why.Add($"pins [{string.Join(",", r.ResolvedPinIds)}] != [{string.Join(",", t.Pins)}]");
                }
                if (t.Highlight != null)
                {
                    bool ok = SameSet(r.HighlightIds, t.Highlight);
                    highlight.Add(ok);
                    if (!ok) why.Add($"highlight [{string.Join(",", r.HighlightIds)}] != [{string.Join(",", t.Highlight)}]");
                }
                foreach (var re in t.Has)
                {
                    bool ok = Regex.IsMatch(text, re, RegexOptions.IgnoreCase);
                    content.Add(ok);
                    if (!ok) why.Add($"missing /{re}/");
                }
                foreach (var re in t.HasNot)
                {
                    bool ok = !Regex.IsMatch(text, re, RegexOptions.IgnoreCase);
                    content.Add(ok);
                    if (!ok) why.Add($"must not contain /{re}/");
                }
                if (t.OutOfScope)
                {
                    bool declined = r.Kind == QueryKind.Unknown || r.Kind == QueryKind.Unsupported
                                    || Regex.IsMatch(r.Answer ?? "", "no record", RegexOptions.IgnoreCase);
                    bool ok = declined && !r.HighlightIds.Any();
                    oos.Add(ok);
                    if (!ok) why.Add($"out-of-scope answered as {r.IntentId} (highlights [{string.Join(",", r.HighlightIds)}])");
                }

                bool pass = why.Count == 0;
                turns.Add(pass);
                if (!byCategory.TryGetValue(t.Category, out var cs)) byCategory[t.Category] = cs = new Score();
                cs.Add(pass);
                if (!pass) failures.Add($"[{t.Category}/{t.Board}] \"{t.Question}\": {string.Join("; ", why)}");
            }

            var report = new StringBuilder();
            report.AppendLine("NLP EVALUATION");
            report.AppendLine($"Turns fully correct : {turns}");
            report.AppendLine($"Intent accuracy     : {intent}");
            report.AppendLine($"Pin extraction      : {pins}");
            report.AppendLine($"Highlight set       : {highlight}");
            report.AppendLine($"Answer content      : {content}");
            report.AppendLine($"Out-of-scope safety : {oos}");
            report.AppendLine("By category (turns fully correct):");
            foreach (var kv in byCategory) report.AppendLine($"  {kv.Key,-11} {kv.Value}");
            report.AppendLine($"Failures ({failures.Count}):");
            foreach (var f in failures) report.AppendLine("  " + f);
            TestContext.WriteLine(report.ToString());
            UnityEngine.Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
