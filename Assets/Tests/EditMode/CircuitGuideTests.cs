using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ArduinoAR.Tests
{
    /// <summary>Circuit wiring guides (LED, button, potentiometer, LDR) and their follow-ups, on both boards.</summary>
    public class CircuitGuideTests
    {
        static BoardProfile Spduino => SpduinoProfile.Profile;
        static BoardProfile Uno => ArduinoUnoProfile.Profile;
        static AssistantResponse Ask(string q, BoardProfile board, DialogueContext ctx = null) => BoardAssistant.Process(q, board, ctx);
        static string Ids(AssistantResponse r) => string.Join(",", r.HighlightIds.OrderBy(x => x));
        static string Sorted(params string[] ids) => string.Join(",", ids.OrderBy(x => x));
        static readonly string Gnd = "GND_TOP,GND1,GND2";
        static readonly Regex s_Ratings = new(@"\b\d+ ?ma\b|\b\d+(\.\d+)? ?(mw|w)\b|\b7 ?(-|to) ?12 ?v|\b6 ?(-|to) ?(17|20) ?v", RegexOptions.IgnoreCase);

        static void NoRatings(AssistantResponse r) => Assert.IsFalse(s_Ratings.IsMatch(r.ToDisplayText(50)), r.ToDisplayText(50));

        // ---------- LED ----------

        [TestCase("How do I connect an LED?")]
        [TestCase("How do I wire an LED with a resistor?")]
        [TestCase("Hook up an external LED to the board")]
        [TestCase("How do I wire up a light emitting diode?")]
        public void Led_DefaultD9_BothBoards(string q)
        {
            foreach (var board in new[] { Spduino, Uno })
            {
                var r = Ask(q, board);
                Assert.AreEqual("circuit.led", r.IntentId, q);
                Assert.AreEqual(Sorted("D9", "GND_TOP", "GND1", "GND2"), Ids(r), q);
                Assert.AreEqual(PinRoles.Signal, r.PinRoles["D9"]);
                Assert.AreEqual(PinRoles.Ground, r.PinRoles["GND1"]);
                StringAssert.Contains("anode (longer leg)", r.Answer);
                StringAssert.Contains("cathode (shorter leg", r.Answer);
                StringAssert.Contains("forward voltage", r.Answer);
                StringAssert.Contains("220 ohms to 1 kilohm", r.Answer);
                StringAssert.Contains("not universally suitable", r.Answer);
                StringAssert.Contains("has not been tested as a physical circuit", r.Answer);
                StringAssert.DoesNotContain("UNVERIFIED", r.ToDisplayText(50), "D9 and GND are verified/documented on both boards");
                NoRatings(r);
            }
        }

        [Test]
        public void Led_SpokenAnswer_KeepsTheSafetyText_AndOmitsTheColourKey()
        {
            var r = Ask("How do I connect an LED?", Spduino);
            var spoken = SpokenAnswer.From(r, "the SPDUINO board");
            StringAssert.Contains("not universally suitable", spoken);
            StringAssert.DoesNotContain("Colours", spoken);
            Assert.AreEqual("Colours: signal yellow, GND blue.", r.Legend);
        }

        [Test]
        public void Led_UserChosenPin_UsedWhenCapable()
        {
            Assert.AreEqual(Sorted("D6", "GND_TOP", "GND1", "GND2"), Ids(Ask("How do I connect an LED to pin 6?", Spduino)));
            var d4 = Ask("Connect an LED to pin 4", Spduino);
            Assert.AreEqual(Sorted("D4", "GND_TOP", "GND1", "GND2"), Ids(d4));
            StringAssert.DoesNotContain("analogWrite", d4.Answer, "D4 has no PWM");
        }

        [Test]
        public void Led_OnAnAnalogPin_IsUnverifiedOnSpduino_DocumentedOnUno()
        {
            var spd = Ask("Connect an LED to A0", Spduino);
            StringAssert.Contains("A0 (UNVERIFIED on this board)", spd.Answer);
            var uno = Ask("Connect an LED to A0", Uno);
            StringAssert.DoesNotContain("UNVERIFIED", uno.ToDisplayText(50));
        }

        [Test]
        public void Led_ToAPowerPin_IsRefusedWithAReason()
        {
            var r = Ask("Connect an LED to the 5V pin", Spduino);
            StringAssert.Contains("not an I/O pin", r.Answer);
            Assert.AreEqual(0, r.HighlightPins.Count);
        }

        [TestCase("Hook an LED up to the GND pin")]
        [TestCase("Connect an LED to ground")]
        [TestCase("Wire the LED to GND")]
        public void Led_ToGround_IsCorrected_WithSafeWiring(string q)
        {
            foreach (var board in new[] { Spduino, Uno })
            {
                var r = Ask(q, board);
                Assert.AreEqual("circuit.led", r.IntentId, q);
                StringAssert.StartsWith("GND is not an I/O pin", r.Answer, q);
                StringAssert.Contains("D9 to one end of the resistor", r.Answer);
                StringAssert.Contains("cathode (shorter leg, flat side) to GND", r.Answer);
                Assert.AreEqual(Sorted("D9", "GND_TOP", "GND1", "GND2"), Ids(r));
                Assert.AreEqual(PinRoles.Signal, r.PinRoles["D9"], "GND must not be the signal pin");
                CollectionAssert.DoesNotContain(r.ResolvedPinIds.ToList(), "GND1");
                NoRatings(r);

                var spoken = SpokenAnswer.From(r, "the board");
                StringAssert.StartsWith("GND is not an I/O pin", spoken);
                StringAssert.Contains("not universally suitable", spoken, "the resistor caution must survive the speech limit");
            }
        }

        [TestCase("Connect the LED's short leg to GND")]
        [TestCase("How do I connect an LED?")]
        public void Led_CorrectWiringQuestions_AreNotCorrected(string q)
        {
            StringAssert.DoesNotContain("GND is not an I/O pin", Ask(q, Spduino).Answer, q);
        }

        [TestCase("Connect a button to GND")]
        [TestCase("Connect a potentiometer to ground")]
        public void OtherCircuits_MayConnectToGround(string q)
        {
            StringAssert.DoesNotContain("not an I/O pin", Ask(q, Spduino).Answer, q);
        }

        [Test]
        public void WhatDoesTheResistorDo_IsALedFollowUp()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect an LED?", Uno, ctx);
            var r = Ask("What does the resistor do?", Uno, ctx);
            Assert.AreEqual("circuit.led.followup", r.IntentId);
            StringAssert.Contains("limits the current", r.Answer);
            Assert.AreEqual(0, r.HighlightPins.Count);
            NoRatings(r);
            Assert.AreEqual("unknown", Ask("What does the resistor do?", Uno).IntentId, "no circuit is active or named");
        }

        [Test]
        public void Led_WithoutResistor_SaysNo()
        {
            var r = Ask("Can I connect an LED directly to pin 9 without a resistor?", Spduino);
            StringAssert.StartsWith("No.", r.Answer);
            Assert.AreEqual("D9", Ids(r));
            NoRatings(r);
        }

        // ---------- Button ----------

        [TestCase("How do I connect a push button?")]
        [TestCase("How do I wire a pushbutton switch?")]
        [TestCase("How do I hook up a tactile switch?")]
        public void Button_DefaultD2_InputPullup(string q)
        {
            foreach (var board in new[] { Spduino, Uno })
            {
                var r = Ask(q, board);
                Assert.AreEqual("circuit.button", r.IntentId, q);
                Assert.AreEqual(Sorted("D2", "GND_TOP", "GND1", "GND2"), Ids(r));
                StringAssert.Contains("INPUT_PULLUP", r.Answer);
                StringAssert.Contains("pinMode(2, INPUT_PULLUP)", r.Answer);
                NoRatings(r);
            }
        }

        [Test]
        public void Button_UserChosenPin()
        {
            var r = Ask("Connect a button to pin 7", Spduino);
            Assert.AreEqual(Sorted("D7", "GND_TOP", "GND1", "GND2"), Ids(r));
            StringAssert.Contains("pinMode(7, INPUT_PULLUP)", r.Answer);
        }

        // ---------- Potentiometer / LDR ----------

        [Test]
        public void Potentiometer_DefaultA0_With5VAndGnd()
        {
            foreach (var board in new[] { Spduino, Uno })
            {
                var r = Ask("How do I connect a potentiometer?", board);
                Assert.AreEqual("circuit.potentiometer", r.IntentId);
                Assert.AreEqual(Sorted("A0", "5V", "GND_TOP", "GND1", "GND2"), Ids(r));
                Assert.AreEqual(PinRoles.Analog, r.PinRoles["A0"]);
                Assert.AreEqual(PinRoles.Power, r.PinRoles["5V"]);
                StringAssert.Contains("wiper", r.Answer);
                StringAssert.Contains("never connect an analog pin to VIN", r.Answer);
                StringAssert.DoesNotContain("UNVERIFIED", r.ToDisplayText(50), "A0 analog input is a verified label on the SPDUINO");
                NoRatings(r);
            }
        }

        [Test]
        public void Potentiometer_OnADigitalOnlyPin_IsRefused()
        {
            var r = Ask("Connect a potentiometer to pin 7", Spduino);
            StringAssert.Contains("D7 can't read an analog voltage", r.Answer);
            StringAssert.Contains("A0, A1, A2, A3, A4, A5", r.Answer);
            Assert.AreEqual(0, r.HighlightPins.Count);
        }

        [Test]
        public void WhereCanIConnectAPotentiometer_ListsTheAnalogPins()
        {
            Assert.AreEqual("analog.list", Ask("Where can I connect a potentiometer?", Spduino).IntentId);
            Assert.AreEqual("analog.list", Ask("Which pins can I connect a light sensor to?", Uno).IntentId);
            Assert.AreEqual("circuit.potentiometer", Ask("How do I connect a potentiometer?", Spduino).IntentId);
        }

        [Test]
        public void Ldr_VoltageDivider()
        {
            var r = Ask("How do I connect a light sensor (LDR)?", Uno);
            Assert.AreEqual("circuit.ldr", r.IntentId);
            StringAssert.Contains("voltage divider", r.Answer);
            StringAssert.Contains("fixed resistor from A0 to GND", r.Answer);
        }

        // ---------- Follow-ups ----------

        [Test]
        public void LedFollowUps_InContext()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect an LED to pin 6?", Spduino, ctx);
            var why = Ask("Why do I need a resistor?", Spduino, ctx);
            Assert.AreEqual("circuit.led.followup", why.IntentId);
            StringAssert.Contains("limits the current", why.Answer);
            Assert.AreEqual(0, why.HighlightPins.Count);
            NoRatings(why);

            var rev = Ask("What if I reverse the LED?", Spduino, ctx);
            StringAssert.Contains("won't light", rev.Answer);
            StringAssert.Contains("one direction", rev.Answer);

            var where = Ask("Where does the resistor go?", Spduino, ctx);
            Assert.AreEqual("D6", Ids(where), "the user's chosen pin is remembered");
            Assert.AreEqual(Gnd, Ids(Ask("Which pin does the short leg connect to?", Spduino, ctx)));
            Assert.AreEqual("D6", Ids(Ask("Which pin does that connect to?", Spduino, ctx)));
        }

        [Test]
        public void PotentiometerFollowUps_InContext()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect a potentiometer?", Uno, ctx);
            Assert.AreEqual("A0", Ids(Ask("Where does the wiper go?", Uno, ctx)));
            Assert.AreEqual(Sorted("5V", "GND_TOP", "GND1", "GND2"), Ids(Ask("Where do the outer legs go?", Uno, ctx)));
            StringAssert.Contains("0 to 1023", Ask("What values will I get?", Uno, ctx).Answer);
        }

        [Test]
        public void ButtonFollowUps_InContext()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect a push button?", Uno, ctx);
            StringAssert.Contains("INPUT_PULLUP", Ask("Do I need a resistor?", Uno, ctx).Answer);
            Assert.AreEqual("D2", Ids(Ask("Which pin is the button on?", Uno, ctx)));
            Assert.AreEqual(Gnd, Ids(Ask("Where does the other side go?", Uno, ctx)));
            StringAssert.Contains("LOW while it is pressed", Ask("Why does it read LOW when I press it?", Uno, ctx).Answer);
        }

        [Test]
        public void FollowUps_WithoutContext_WhenThePartIsNamed()
        {
            Assert.AreEqual("A0", Ids(Ask("Where does the wiper go?", Spduino)));
            StringAssert.Contains("limits the current", Ask("Why do I need a resistor for an LED?", Spduino).Answer);
            StringAssert.Contains("one direction", Ask("What if I reverse the LED?", Spduino).Answer);
            Assert.AreEqual("unknown", Ask("Why do I need a resistor?", Spduino).IntentId, "no circuit named and none active");
        }

        // ---------- Voice: choosing a circuit follow-up among recognizer alternatives ----------

        [Test]
        public void Voice_PicksWiperOverViper_RealPhoneTranscript()
        {
            var c = TranscriptSelector.Select(new[] { "where does the viper go", "where does the wiper go" }, TranscriptSelector.ClassifierUnderstands);
            Assert.AreEqual(1, c.Index);
            Assert.AreEqual("where does the wiper go", c.Text);
            Assert.IsTrue(c.Understood);

            var ctx = new DialogueContext();
            Ask("How do I connect a potentiometer?", Uno, ctx);
            Assert.AreEqual("A0", Ids(Ask(c.Text, Uno, ctx)));
        }

        [TestCase("what does the resistor do")]
        [TestCase("why do i need a resistor")]
        [TestCase("where do the outer legs go")]
        [TestCase("which pin does that connect to")]
        public void Voice_CircuitFollowUpWordings_CountAsUnderstood(string text)
        {
            var c = TranscriptSelector.Select(new[] { "good morning", text }, TranscriptSelector.ClassifierUnderstands);
            Assert.AreEqual(1, c.Index, text);
            Assert.IsTrue(c.Understood, text);
        }

        [Test]
        public void Voice_FirstUnderstoodAlternative_StillWins_AndUnrelatedTextIsNotUnderstood()
        {
            var first = TranscriptSelector.Select(new[] { "which pins support PWM", "where does the wiper go" }, TranscriptSelector.ClassifierUnderstands);
            Assert.AreEqual(0, first.Index);

            var none = TranscriptSelector.Select(new[] { "where does the viper go", "good morning" }, TranscriptSelector.ClassifierUnderstands);
            Assert.AreEqual(0, none.Index, "falls back to the first alternative");
            Assert.IsFalse(none.Understood);
            Assert.IsFalse(TranscriptSelector.ClassifierUnderstands("hello there"));
        }

        [Test]
        public void Voice_SelectionCheck_DoesNotChangeTheConversation()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect an LED?", Uno, ctx);
            TranscriptSelector.ClassifierUnderstands("where does the wiper go");
            Assert.AreEqual(CircuitGuides.Led, ctx.ActiveCircuit);
            Assert.AreEqual("unknown", Ask("Why do I need a resistor?", Uno).IntentId, "answering still needs a real circuit context");
        }

        // ---------- D13 wording ----------

        [Test]
        public void D13_Spduino_ConciseAnswer_KeepsItsCaveats()
        {
            const string expected = "D13 on the SPDUINO is a digital I/O pin; its number is printed on the board. On ATmega328 boards it is usually also the SPI clock (SCK) and the on-board LED pin, but both are UNVERIFIED on this board.";
            var r = Ask("What is the function of D13?", Spduino);
            Assert.AreEqual("pin.lookup", r.IntentId);
            Assert.AreEqual("D13", Ids(r));
            Assert.AreEqual(expected, r.Answer);
            Assert.AreEqual(expected, SpokenAnswer.From(r, "the SPDUINO board"));

            StringAssert.Contains("Unverified on", r.Uncertainty);
            StringAssert.Contains("UNVERIFIED (inferred)", r.ToDisplayText(10), "the evidence lines keep their status");
            var d13 = Spduino.Pins.First(p => p.Id == "D13");
            Assert.IsTrue(d13.IsVerified(PinCapability.Digital));
            Assert.IsFalse(d13.IsVerified(PinCapability.SpiSck));
            Assert.IsFalse(d13.IsVerified(PinCapability.BuiltinLed));
        }

        [Test]
        public void D13_OtherPinsAndBoards_KeepTheStandardWording()
        {
            StringAssert.StartsWith("D13 (\"13\") on Arduino Uno Rev3:", Ask("What is the function of D13?", Uno).Answer);
            var d12 = Ask("What is the function of D12?", Spduino);
            StringAssert.StartsWith("D12 (\"12\") on SPDUINO", d12.Answer);
            StringAssert.Contains("Functions:", d12.Answer);
            StringAssert.StartsWith("No: D13", Ask("Does pin 13 support PWM?", Spduino).Answer);
            StringAssert.StartsWith("Probably: D13", Ask("Does pin 13 support SPI?", Spduino).Answer);
        }

        [Test]
        public void ActiveCircuit_DoesNotCaptureOrdinaryQuestions()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect an LED?", Spduino, ctx);
            Assert.AreEqual("pwm.list", Ask("Which pins support PWM?", Spduino, ctx).IntentId);
            Assert.AreEqual("pin.lookup", Ask("What does pin 13 do?", Spduino, ctx).IntentId);
            Assert.AreEqual("led.list", Ask("Which pin is the built-in LED on?", Spduino, ctx).IntentId);
            Assert.AreEqual("i2c.list", Ask("Which pin is SCL?", Spduino, ctx).IntentId);
            Assert.AreEqual("analog.list", Ask("Which pins can read a potentiometer?", Spduino, ctx).IntentId);
        }

        [Test]
        public void NewCircuit_ReplacesTheActiveOne()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect an LED?", Uno, ctx);
            Ask("How do I connect a potentiometer?", Uno, ctx);
            Assert.AreEqual(CircuitGuides.Potentiometer, ctx.ActiveCircuit);
            Assert.AreEqual("A0", Ids(Ask("Which pin does that connect to?", Uno, ctx)));
        }

        [Test]
        public void ModeOrBoardChange_ClearsTheCircuit()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect an LED?", Uno, ctx);
            ctx.Reset();
            Assert.IsNull(ctx.ActiveCircuit);
            Assert.AreEqual("unknown", Ask("Why do I need a resistor?", Uno, ctx).IntentId);
        }

        // ---------- Board selection and preserved behaviour ----------

        [Test]
        public void OtherBoard_CircuitAnsweredWithoutHighlights()
        {
            var r = Ask("How do I connect an LED to the Uno?", Spduino);
            Assert.IsTrue(r.AboutOtherBoard);
            Assert.AreEqual(0, r.HighlightPins.Count);
            StringAssert.Contains("on Arduino Uno", r.Answer);
        }

        [Test]
        public void I2cWiring_Unchanged()
        {
            var r = Ask("How do I connect an I2C sensor?", Spduino);
            Assert.AreEqual("i2c.connect", r.IntentId);
            Assert.AreEqual(Sorted("A4", "A5", "5V", "3V3", "GND_TOP", "GND1", "GND2"), Ids(r));
            StringAssert.Contains("SDA to A4, UNVERIFIED", r.Answer);
            Assert.AreEqual("Colours: SDA cyan, SCL magenta, power red, GND blue.", r.Legend);
        }

        [Test]
        public void MotorQuestion_GetsTheDriverCaveat()
        {
            var r = Ask("Can I power a motor from pin 9?", Spduino);
            Assert.IsTrue(r.Caveats.Any(c => c.Contains("transistor or driver")));
        }

        [Test]
        public void RoleColours_AreDistinct()
        {
            var roles = new[] { PinRoles.Sda, PinRoles.Scl, PinRoles.Signal, PinRoles.Analog, PinRoles.Power, PinRoles.Ground };
            var colours = roles.Select(r => PinRoles.ColorFor(r).Value).ToList();
            for (int i = 0; i < colours.Count; i++)
                for (int j = i + 1; j < colours.Count; j++)
                {
                    var a = colours[i]; var b = colours[j];
                    float d = UnityEngine.Mathf.Abs(a.r - b.r) + UnityEngine.Mathf.Abs(a.g - b.g) + UnityEngine.Mathf.Abs(a.b - b.b);
                    Assert.Greater(d, 0.6f, $"{roles[i]} vs {roles[j]}");
                }
        }
    }
}
