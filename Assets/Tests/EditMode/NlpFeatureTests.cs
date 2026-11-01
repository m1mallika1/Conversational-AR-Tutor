using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ArduinoAR.Tests
{
    /// <summary>
    /// Wiring (connection) guidance, board comparisons, attribute questions, questions about the other board,
    /// built-in feature questions and the follow-up fixes. No scene or device needed.
    /// </summary>
    public class NlpFeatureTests
    {
        static BoardProfile Spduino => SpduinoProfile.Profile;
        static BoardProfile Uno => ArduinoUnoProfile.Profile;
        static AssistantResponse Ask(string q, BoardProfile board, DialogueContext ctx = null) => BoardAssistant.Process(q, board, ctx);
        static string Ids(AssistantResponse r) => string.Join(",", r.HighlightIds.OrderBy(x => x));
        static string Sorted(params string[] ids) => string.Join(",", ids.OrderBy(x => x));

        // Ratings (option b): never stated, even though the manufacturer page lists some.
        static readonly Regex s_Ratings = new(@"\b\d+ ?ma\b|\b7 ?(-|to) ?12\b|\b6 ?(-|to) ?(17|20)\b", RegexOptions.IgnoreCase);

        // ---------- Wiring ----------

        [TestCase("How do I connect an I2C sensor?")]
        [TestCase("How do I wire an OLED display to this board?")]
        [TestCase("How should I hook up a BME280?")]
        [TestCase("Connect an MPU6050 sensor")]
        [TestCase("Which wires go where for an I2C module?")]
        [TestCase("how do I connect an eye to see sensor")]
        public void Wiring_Spduino_RolesAndUnverifiedSignals(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual("i2c.connect", r.IntentId, q);
            Assert.AreEqual(Sorted("A4", "A5", "5V", "3V3", "GND_TOP", "GND1", "GND2"), Ids(r), q);
            Assert.AreEqual(PinRoles.Sda, r.PinRoles["A4"]);
            Assert.AreEqual(PinRoles.Scl, r.PinRoles["A5"]);
            Assert.AreEqual(PinRoles.Power, r.PinRoles["5V"]);
            Assert.AreEqual(PinRoles.Power, r.PinRoles["3V3"]);
            Assert.AreEqual(PinRoles.Ground, r.PinRoles["GND1"]);
            StringAssert.Contains("SDA to A4, UNVERIFIED", r.Answer);
            StringAssert.Contains("SCL to A5, UNVERIFIED", r.Answer);
            StringAssert.Contains("datasheet", r.Answer);
            Assert.IsFalse(s_Ratings.IsMatch(r.ToDisplayText(20)), "no current/voltage ratings");
        }

        [TestCase("How do I connect an I2C sensor to the Uno?")]
        [TestCase("How do I wire an I2C LCD?")]
        public void Wiring_Uno_UsesSdaSclHeader_Documented(string q)
        {
            var r = Ask(q, Uno);
            Assert.AreEqual("i2c.connect", r.IntentId, q);
            Assert.AreEqual(Sorted("SDA", "SCL", "5V", "3V3", "GND_TOP", "GND1", "GND2"), Ids(r));
            StringAssert.Contains("SDA to SDA (same signal as A4)", r.Answer);
            StringAssert.Contains("SCL to SCL (same signal as A5)", r.Answer);
            StringAssert.DoesNotContain("UNVERIFIED", r.ToDisplayText(20));
            Assert.IsFalse(s_Ratings.IsMatch(r.ToDisplayText(20)));
        }

        [Test]
        public void Wiring_SpokenAnswer_FitsAndOmitsColourKey()
        {
            var r = Ask("How do I connect an I2C sensor?", Spduino);
            var spoken = SpokenAnswer.From(r, "the SPDUINO board");
            Assert.LessOrEqual(spoken.Length, SpokenAnswer.MaxLength);
            StringAssert.Contains("UNVERIFIED", spoken);
            StringAssert.DoesNotContain("Colours", spoken);
            StringAssert.Contains("Colours: SDA cyan", r.ToDisplayText());
        }

        [TestCase("How do I use I2C?", "i2c.explain")]
        [TestCase("Which pins are I2C?", "i2c.list")]
        [TestCase("What is I2C?", "i2c.explain")]
        [TestCase("Which pins can I use with the Wire library?", "i2c.list")]
        public void Wiring_NotTriggeredWithoutAConnectionWord(string q, string intent)
        {
            Assert.AreEqual(intent, Ask(q, Spduino).IntentId);
        }

        [Test]
        public void Wiring_OtherTopics_KeepTheirAnswer_WithNote()
        {
            var r = Ask("How do I connect an SPI device?", Spduino);
            Assert.AreNotEqual(QueryKind.Connect, r.Kind);
            Assert.AreEqual(Sorted("D10", "D11", "D12", "D13"), Ids(r));
            Assert.IsTrue(r.Caveats.Any(c => c.Contains("I2C modules only")));
        }

        [Test]
        public void Wiring_WithASpecificPin_StaysAPinQuestion()
        {
            var r = Ask("How do I connect a sensor to pin A0?", Spduino);
            CollectionAssert.AreEqual(new[] { "A0" }, r.ResolvedPinIds.ToList());
            Assert.AreNotEqual(QueryKind.Connect, r.Kind);
        }

        [Test]
        public void Wiring_FollowUps_RoleThenOtherTopic()
        {
            var ctx = new DialogueContext();
            Ask("How do I connect an I2C sensor?", Spduino, ctx);
            var scl = Ask("Which pin is SCL?", Spduino, ctx);
            Assert.AreEqual("A5", Ids(scl));
            var spi = Ask("What about SPI?", Spduino, ctx);
            Assert.AreEqual("spi.list", spi.IntentId);
        }

        // ---------- Single I2C role ----------

        [TestCase("Which one is SDA?", "A4")]
        [TestCase("Where is the SCL pin?", "A5")]
        [TestCase("where is the s d a pin", "A4")]
        public void RoleQuestion_HighlightsOnlyThatSignal(string q, string pin)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(pin, Ids(r), q);
            StringAssert.Contains(pin, r.Answer);
        }

        [Test]
        public void RoleQuestion_Uno_IncludesHeaderPin()
        {
            Assert.AreEqual(Sorted("A4", "SDA"), Ids(Ask("Which pin is SDA?", Uno)));
        }

        // ---------- Comparisons ----------

        [TestCase("What is the difference between the SPDUINO and the Arduino Uno?")]
        [TestCase("Compare this board with the Uno")]
        [TestCase("SPDUINO vs Uno")]
        [TestCase("difference between speed no and arduino")]
        [TestCase("difference between Speedo and arduino")]
        public void Compare_BothBoardsWithStatuses_NoHighlights(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual("board.compare", r.IntentId, q);
            StringAssert.Contains("FT232RL", r.Answer);
            StringAssert.Contains("ATmega16U2", r.Answer);
            StringAssert.Contains("UNVERIFIED", r.Answer, "the SPDUINO MCU marking is only partly legible");
            StringAssert.Contains("verified on this board", r.Answer);
            StringAssert.Contains("documented", r.Answer);
            Assert.AreEqual(0, r.HighlightPins.Count);
            Assert.IsFalse(s_Ratings.IsMatch(r.ToDisplayText(50)));
        }

        [Test]
        public void Compare_FromUnoMode_ListsUnoFirst()
        {
            var r = Ask("How is the Uno different from the SPDUINO?", Uno);
            Assert.AreEqual("board.compare", r.IntentId);
            StringAssert.StartsWith("Arduino Uno vs SPDUINO", r.Answer);
        }

        [TestCase("Is the SPDUINO an Arduino?")]
        [TestCase("What is the difference between PWM and analog input?")]
        public void Compare_NotTriggeredByOneBoardOrNoBoard(string q)
        {
            Assert.AreNotEqual("board.compare", Ask(q, Spduino).IntentId, q);
        }

        // ---------- Attribute questions ----------

        [TestCase("What is the flash memory size?", "32 KB flash")]
        [TestCase("How much SRAM does it have?", "2 KB SRAM")]
        [TestCase("What is the clock speed?", "16 MHz")]
        [TestCase("What is the operating voltage?", "5 V")]
        [TestCase("How big is this board?", "65 x 50 mm")]
        [TestCase("Which USB chip is on the board?", "FT232RL")]
        [TestCase("What connectors does the board have?", "ICSP")]
        public void Attribute_Spduino_ReturnsOnlyThatAttribute(string q, string expected)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual("board.info", r.IntentId, q);
            StringAssert.Contains(expected, r.Answer, q);
            Assert.LessOrEqual(r.RetrievedFacts.Count, 2, "attribute questions should not dump every fact");
        }

        [TestCase("What are the dimensions of the Uno?", "68.6 x 53.4 mm")]
        [TestCase("How much flash does the Uno have?", "0.5 KB")]
        [TestCase("What USB-to-serial chip does the Uno use?", "ATmega16U2")]
        public void Attribute_Uno(string q, string expected)
        {
            var r = Ask(q, Uno);
            Assert.AreEqual("board.info", r.IntentId, q);
            StringAssert.Contains(expected, r.Answer, q);
        }

        [Test]
        public void List_StatesTheCount()
        {
            StringAssert.Contains("(6)", Ask("How many PWM pins are there?", Spduino).Answer);
            StringAssert.Contains("(6)", Ask("How many analog inputs does the Uno have?", Uno).Answer);
        }

        // ---------- Ratings stay refused (option b) ----------

        [TestCase("How much current can a pin supply?")]
        [TestCase("What is the maximum current of the 3.3V pin?")]
        [TestCase("What is the input voltage range?")]
        [TestCase("What voltage can VIN take?")]
        [TestCase("Can I connect a 12V motor directly to pin 9?")]
        public void Ratings_StillRefused(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(QueryKind.Unsupported, r.Kind, q);
            Assert.IsFalse(s_Ratings.IsMatch(r.ToDisplayText(50)), q);
        }

        [Test]
        public void Ratings_NotStoredAsBoardFacts()
        {
            foreach (var board in new[] { Spduino, Uno })
                foreach (var f in board.BoardFacts)
                {
                    Assert.IsFalse(s_Ratings.IsMatch(f.Text), $"{board.Id}: {f.Text}");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(f.Source), $"{board.Id}: fact without a source: {f.Text}");
                }
        }

        // ---------- The other board ----------

        [Test]
        public void OtherBoard_AnsweredForThatBoard_WithoutHighlights()
        {
            var r = Ask("Does the Uno have SDA pins?", Spduino);
            Assert.IsTrue(r.AboutOtherBoard);
            Assert.AreEqual(Uno.Id, r.BoardId);
            Assert.AreEqual(0, r.HighlightPins.Count);
            StringAssert.Contains("SDA", r.Answer);
            Assert.IsTrue(r.Caveats.Any(c => c.Contains("not the board in view")));

            var s = Ask("Where are the I2C pins on the SPDUINO?", Uno);
            Assert.AreEqual(Spduino.Id, s.BoardId);
            Assert.AreEqual(0, s.HighlightPins.Count);
            StringAssert.Contains("A4", s.Answer);
            StringAssert.Contains("Unverified", s.Uncertainty);
        }

        [Test]
        public void OtherBoard_DoesNotChangeTheDialogueContext()
        {
            var ctx = new DialogueContext();
            Ask("Which pins support PWM?", Spduino, ctx);
            Ask("Which pins on the Arduino Uno support SPI?", Spduino, ctx);
            Assert.AreEqual(Topics.Pwm, ctx.LastTopic);
        }

        [Test]
        public void NamingTheBoardInView_IsAnOrdinaryQuestion()
        {
            var r = Ask("Which pins support PWM on the Uno?", Uno);
            Assert.IsFalse(r.AboutOtherBoard);
            Assert.AreEqual("D10,D11,D3,D5,D6,D9", Ids(r));
        }

        // ---------- Built-in features ----------

        [TestCase("Does this board have WiFi?", "WiFi")]
        [TestCase("Does it have an Ethernet port?", "Ethernet")]
        [TestCase("Does the SPDUINO support Bluetooth?", "Bluetooth")]
        public void Feature_NoRecord_NoHighlights(string q, string feature)
        {
            var r = Ask(q, Spduino);
            StringAssert.Contains($"no record of built-in {feature}", r.Answer);
            Assert.AreEqual(0, r.HighlightPins.Count);
        }

        [Test]
        public void Feature_ConnectingAModuleIsNotAFeatureQuestion()
        {
            var r = Ask("How do I connect a GPS module?", Spduino);
            StringAssert.DoesNotContain("no record", r.Answer);
            Assert.AreEqual(Topics.Serial, r.Topic);
        }

        // ---------- Follow-up fixes ----------

        [Test]
        public void ThisBoard_IsNotAPronounReference()
        {
            var ctx = new DialogueContext();
            Ask("Tell me about Bluetooth", Spduino, ctx);
            var r = Ask("Does this board have WiFi?", Spduino, ctx);
            Assert.IsFalse(r.UsedContext);
            Assert.AreEqual(0, r.HighlightPins.Count);

            ctx = new DialogueContext();
            Ask("Which pins support PWM?", Spduino, ctx);
            var a = Ask("Which pins on this board are analog inputs?", Spduino, ctx);
            Assert.AreEqual("analog.list", a.IntentId);
            Assert.IsFalse(a.UsedContext);
        }

        [Test]
        public void TellMeMore_ContinuesThePreviousTopic()
        {
            var ctx = new DialogueContext();
            Ask("Where are the I2C pins?", Spduino, ctx);
            var r = Ask("Tell me more", Spduino, ctx);
            Assert.AreEqual("i2c.explain", r.IntentId);
            Assert.IsTrue(r.UsedContext);
        }

        [Test]
        public void BareWhichPins_ContinuesThePreviousTopic()
        {
            var ctx = new DialogueContext();
            Ask("What is PWM?", Uno, ctx);
            var r = Ask("Which pins?", Uno, ctx);
            Assert.AreEqual("pwm.list", r.IntentId);
            Assert.IsTrue(r.UsedContext);
            Assert.AreEqual("unknown", Ask("Which pins?", Uno).IntentId, "without context it stays unknown");
        }

        // ---------- Held-out regressions (the six held-out misses plus nearby variants) ----------

        [TestCase("Can I feed 15 volts into VIN?")]
        [TestCase("What's the maximum voltage for the analog pins?")]
        [TestCase("Can I put 12 volts on pin 9?")]
        [TestCase("How many volts can A0 take?")]
        [TestCase("What voltage limit do the digital pins have?")]
        public void VoltageRatings_RefusedWithoutPinList(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(QueryKind.Unsupported, r.Kind, $"{q} -> {r.IntentId}");
            Assert.AreEqual(0, r.HighlightPins.Count, q);
            Assert.IsFalse(s_Ratings.IsMatch(r.ToDisplayText(50)), q);
        }

        [TestCase("What is the operating voltage?", "board.info")]
        [TestCase("Which pins are 5V and 3.3V?", "power.list")]
        public void VoltageWords_WithoutALimit_AreNotRefused(string q, string intent)
        {
            Assert.AreEqual(intent, Ask(q, Spduino).IntentId, q);
        }

        [Test]
        public void HowLarge_IsASizeQuestion()
        {
            var r = Ask("How large is the Uno board?", Uno);
            Assert.AreEqual("board.info", r.IntentId);
            StringAssert.Contains("68.6 x 53.4 mm", r.Answer);
            StringAssert.Contains("65 x 50 mm", Ask("How large is the SPDUINO?", Spduino).Answer);
        }

        [Test]
        public void UsbPort_IsAConnectorQuestion()
        {
            var r = Ask("What kind of USB port does it have?", Spduino);
            Assert.AreEqual("board.info", r.IntentId);
            StringAssert.Contains("USB Type-B", r.Answer);
            Assert.AreNotEqual("board.info", Ask("Which pins are shared with USB?", Spduino).IntentId, "a bare 'USB' must not become a board-facts question");
        }

        [Test]
        public void Ioref_OnTheUno_AndOnTheSpduino()
        {
            var uno = Ask("Does the Uno have an IOREF pin?", Spduino);
            Assert.IsTrue(uno.AboutOtherBoard);
            CollectionAssert.AreEqual(new[] { "IOREF" }, uno.ResolvedPinIds.ToList());
            Assert.AreEqual(0, uno.HighlightPins.Count);

            var spd = Ask("Does this board have an IOREF pin?", Spduino);
            Assert.AreEqual("board.info", spd.IntentId);
            StringAssert.Contains("no separate SDA/SCL or IOREF", spd.Answer);
        }

        [Test]
        public void HookItUp_WithPronoun_IsWiring_EvenAfterAnotherTopic()
        {
            const string q = "I have a temperature sensor with SDA and SCL, how do I hook it to the board?";
            Assert.AreEqual("i2c.connect", Ask(q, Spduino).IntentId);

            var ctx = new DialogueContext();
            Ask("What does pin 9 do?", Spduino, ctx);
            var r = Ask(q, Spduino, ctx);
            Assert.AreEqual("i2c.connect", r.IntentId, "the pronoun must not turn it into a check on pin 9");
            Assert.IsFalse(r.UsedContext);
        }

        [Test]
        public void HookItUp_AfterAnI2cQuestion_GivesWiringSteps()
        {
            var ctx = new DialogueContext();
            Ask("Where are the I2C pins?", Spduino, ctx);
            var r = Ask("How do I hook it up?", Spduino, ctx);
            Assert.AreEqual("i2c.connect", r.IntentId);
            StringAssert.Contains("SDA to A4", r.Answer);
        }

        // ---------- Unchanged data ----------

        [Test]
        public void SpduinoI2cMapping_StaysInferred_AndDimensionsUnchanged()
        {
            var a4 = Spduino.Pins.First(p => p.Id == "A4");
            var a5 = Spduino.Pins.First(p => p.Id == "A5");
            Assert.IsFalse(a4.IsVerified(PinCapability.I2cSda));
            Assert.IsFalse(a5.IsVerified(PinCapability.I2cScl));
            Assert.AreEqual(65f, Spduino.WidthMm);
            Assert.AreEqual(50f, Spduino.HeightMm);
            Assert.AreEqual(68.6f, Uno.WidthMm, 0.001f);
            Assert.AreEqual(53.4f, Uno.HeightMm, 0.001f);
        }
    }
}
