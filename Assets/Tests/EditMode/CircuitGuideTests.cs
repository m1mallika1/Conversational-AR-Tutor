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
