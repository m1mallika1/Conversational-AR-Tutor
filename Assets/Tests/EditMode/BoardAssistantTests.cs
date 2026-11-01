using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ArduinoAR.Tests
{
    /// <summary>Tests for the offline NLP + knowledge-retrieval pipeline (no scene or device needed).</summary>
    public class BoardAssistantTests
    {
        static BoardProfile Spduino => SpduinoProfile.Profile;
        static BoardProfile Uno => ArduinoUnoProfile.Profile;

        static AssistantResponse Ask(string q, BoardProfile board, DialogueContext ctx = null) => BoardAssistant.Process(q, board, ctx);
        static string Highlights(AssistantResponse r) => string.Join(",", r.HighlightIds.OrderBy(x => x));
        static string Sorted(params string[] ids) => string.Join(",", ids.OrderBy(x => x));

        const string Pwm = "D10,D11,D3,D5,D6,D9";

        // ---------- Paraphrases of the main intents ----------

        [TestCase("Which pins support PWM?")]
        [TestCase("which pins can do pwm")]
        [TestCase("Where are the PWM pins?")]
        [TestCase("List the pulse width modulation pins")]
        [TestCase("What pins can I use with analogWrite?")]
        [TestCase("Show me the ~ pins")]
        [TestCase("Which pins can I use to dim an LED?")]
        public void Pwm_Paraphrases_ReturnSixPwmPins_OnBothBoards(string q)
        {
            foreach (var board in new[] { Spduino, Uno })
            {
                var r = Ask(q, board);
                Assert.AreEqual(Topics.Pwm, r.Topic, q);
                Assert.AreEqual(Pwm, Highlights(r), $"{q} on {board.Id}");
            }
        }

        [TestCase("Which pins are digital?")]
        [TestCase("List the digital I/O pins")]
        [TestCase("Where are the GPIO pins?")]
        [TestCase("Which pins work with digitalWrite?")]
        public void Digital_Paraphrases_ReturnD0ToD13(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(Topics.Digital, r.Topic, q);
            Assert.AreEqual(Sorted(Enumerable.Range(0, 14).Select(i => "D" + i).ToArray()), Highlights(r));
        }

        [TestCase("Which pins are analog inputs?")]
        [TestCase("Where can I connect a potentiometer?")]
        [TestCase("Which pins use analogRead?")]
        [TestCase("List the ADC pins")]
        public void Analog_Paraphrases_ReturnA0ToA5(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(Topics.Analog, r.Topic, q);
            Assert.AreEqual("A0,A1,A2,A3,A4,A5", Highlights(r));
        }

        [TestCase("Which pins are used for serial communication?")]
        [TestCase("Where are the UART TX and RX pins?")]
        public void Serial_ReturnsD0D1(string q) => Assert.AreEqual("D0,D1", Highlights(Ask(q, Spduino)));

        [TestCase("Which pins are used for SPI?")]
        [TestCase("Where do I connect MOSI and MISO?")]
        public void Spi_ReturnsD10ToD13(string q) => Assert.AreEqual("D10,D11,D12,D13", Highlights(Ask(q, Spduino)));

        [TestCase("Which pins support interrupts?")]
        [TestCase("Where can I use attachInterrupt?")]
        public void Interrupts_ReturnD2D3(string q) => Assert.AreEqual("D2,D3", Highlights(Ask(q, Spduino)));

        [Test]
        public void BuiltInLed_IsD13() => Assert.AreEqual("D13", Highlights(Ask("Which pin is the built-in LED?", Spduino)));

        [Test]
        public void PowerAndGround_ReturnsBothSets()
        {
            var r = Ask("Where are power and ground?", Spduino);
            Assert.AreEqual(Sorted("3V3", "5V", "VIN", "GND_TOP", "GND1", "GND2"), Highlights(r));
            Assert.IsTrue(r.Caveats.Any(c => c.Contains("never short")), "power answers carry a safety caveat");
        }

        [Test]
        public void ResetPin_ByName_IsLookedUp()
        {
            var r = Ask("What does the reset pin do?", Spduino);
            Assert.AreEqual("RESET", Highlights(r));
        }

        // ---------- Explicit pin queries ----------

        [TestCase("What does pin 13 do?", "D13")]
        [TestCase("Tell me about D9", "D9")]
        [TestCase("What is A0 used for?", "A0")]
        [TestCase("What does digital pin 3 do?", "D3")]
        [TestCase("analog pin 2?", "A2")]
        [TestCase("What does the VIN pin do?", "VIN")]
        public void ExplicitPin_Lookup(string q, string pin)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(QueryKind.PinLookup, r.Kind, q);
            Assert.AreEqual(pin, Highlights(r));
            Assert.IsTrue(r.Answer.Contains(pin));
        }

        [Test]
        public void MultiplePins_AreAllExtracted() =>
            Assert.AreEqual("D3,D5", Highlights(Ask("What do pins 3 and 5 do?", Spduino)));

        [Test]
        public void CapabilityCheck_YesAndNo()
        {
            var yes = Ask("Does pin 9 support PWM?", Spduino);
            Assert.AreEqual(QueryKind.CapabilityCheck, yes.Kind);
            StringAssert.StartsWith("Yes", yes.Answer);
            Assert.AreEqual("D9", Highlights(yes));

            var no = Ask("Can pin 4 do PWM?", Spduino);
            StringAssert.StartsWith("No", no.Answer);
            StringAssert.Contains("D3, D5, D6, D9, D10, D11", no.Answer, "a 'no' answer points to the pins that do support it");
        }

        // ---------- Concept explanations ----------

        [TestCase("What is PWM?", Topics.Pwm, "duty cycle")]
        [TestCase("Explain I2C", Topics.I2c, "SDA")]
        [TestCase("How does SPI work?", Topics.Spi, "MOSI")]
        [TestCase("What does UART mean?", Topics.Serial, "baud")]
        [TestCase("What is an interrupt?", Topics.Interrupt, "attachInterrupt")]
        public void Concept_IsExplained_NotJustListed(string q, string topic, string expectedPhrase)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(QueryKind.Explain, r.Kind, q);
            Assert.AreEqual(topic, r.Topic);
            StringAssert.Contains(expectedPhrase, r.Answer);
            Assert.IsTrue(r.RetrievedFacts.Any(f => f.Kind == FactKind.Concept && f.Status == VerificationStatus.General));
        }

        [Test]
        public void ConceptPwm_AlsoNamesTheBoardPins()
        {
            var r = Ask("What is PWM and how is it used?", Spduino);
            Assert.AreEqual(QueryKind.Explain, r.Kind);
            Assert.AreEqual(Pwm, Highlights(r));
            StringAssert.Contains("analogWrite", r.Answer);
        }

        // ---------- Follow-up context ----------

        [Test]
        public void FollowUp_WhatAboutPin_UsesPreviousTopic()
        {
            var ctx = new DialogueContext();
            Ask("Which pins support PWM?", Spduino, ctx);
            var r5 = Ask("What about pin 5?", Spduino, ctx);
            Assert.IsTrue(r5.UsedContext);
            Assert.AreEqual(QueryKind.CapabilityCheck, r5.Kind);
            StringAssert.StartsWith("Yes", r5.Answer);
            var r4 = Ask("What about pin 4?", Spduino, ctx);
            StringAssert.StartsWith("No", r4.Answer);
        }

        [Test]
        public void FollowUp_WhatAboutTopic_KeepsQuestionType()
        {
            var ctx = new DialogueContext();
            Ask("Which pins support PWM?", Spduino, ctx);
            var r = Ask("What about I2C?", Spduino, ctx);
            Assert.IsTrue(r.UsedContext);
            Assert.AreEqual(QueryKind.List, r.Kind);
            Assert.AreEqual("A4,A5", Highlights(r));
        }

        [Test]
        public void FollowUp_PronounRefersToPreviousPin()
        {
            var ctx = new DialogueContext();
            Ask("What does pin 9 do?", Spduino, ctx);
            var r = Ask("Does it support PWM?", Spduino, ctx);
            Assert.IsTrue(r.UsedContext);
            CollectionAssert.AreEqual(new[] { "D9" }, r.ResolvedPinIds);
            StringAssert.StartsWith("Yes", r.Answer);
        }

        [Test]
        public void FollowUp_ThoseRefersToPreviousHighlights()
        {
            var ctx = new DialogueContext();
            Ask("Which pins support PWM?", Spduino, ctx);
            var r = Ask("Which of those can be used for interrupts?", Spduino, ctx);
            Assert.IsTrue(r.UsedContext);
            Assert.AreEqual(QueryKind.CapabilityCheck, r.Kind);
            Assert.AreEqual(Pwm, Highlights(r), "the six previous pins are checked");
            StringAssert.Contains("D3 provides an external interrupt", r.Answer);
        }

        [Test]
        public void FollowUp_WithoutContext_IsAPlainLookup()
        {
            var r = Ask("What about pin 5?", Spduino, new DialogueContext());
            Assert.IsFalse(r.UsedContext);
            Assert.AreEqual(QueryKind.PinLookup, r.Kind);
            Assert.AreEqual("D5", Highlights(r));
        }

        [Test]
        public void FollowUp_NotAppliedAcrossBoards()
        {
            var ctx = new DialogueContext();
            Ask("Which pins support PWM?", Uno, ctx);
            var r = Ask("What about pin 5?", Spduino, ctx);
            Assert.IsFalse(r.UsedContext, "context from another board must not be used");
        }

        [Test]
        public void FollowUp_FullQuestionIgnoresContext()
        {
            var ctx = new DialogueContext();
            Ask("Which pins support PWM?", Spduino, ctx);
            var r = Ask("What is I2C?", Spduino, ctx);
            Assert.IsFalse(r.UsedContext);
            Assert.AreEqual(QueryKind.Explain, r.Kind);
        }

        // ---------- Unknown / unsupported ----------

        [TestCase("What's the weather today?")]
        [TestCase("Tell me a joke")]
        public void Unknown_Questions_HaveNoHighlightsAndZeroConfidence(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(QueryKind.Unknown, r.Kind);
            Assert.AreEqual(0, r.HighlightPins.Count);
            Assert.AreEqual(0f, r.Confidence);
            StringAssert.Contains("couldn't match", r.Answer);
        }

        [Test]
        public void Unknown_WithRelatedKnowledge_UsesLexicalRetrieval_LowConfidence()
        {
            var r = Ask("What is a pull up resistor?", Spduino);
            Assert.AreEqual(QueryKind.Unknown, r.Kind);
            Assert.Greater(r.RetrievedFacts.Count, 0);
            Assert.AreEqual("Low", r.ConfidenceLabel);
            Assert.AreEqual(0, r.HighlightPins.Count, "low-confidence matches do not highlight pins");
        }

        [TestCase("How much current can pin 13 supply?")]
        [TestCase("What is the maximum current of the 5V pin?")]
        [TestCase("Can I connect 12V to VIN?")]
        public void ElectricalRatings_AreNotInvented(string q)
        {
            var r = Ask(q, Spduino);
            Assert.AreEqual(QueryKind.Unsupported, r.Kind, q);
            Assert.IsFalse(Regex.IsMatch(r.Answer, @"\d+\s*(mA|A|V)\b"), "no numeric rating in the answer: " + r.Answer);
            Assert.Greater(r.Caveats.Count, 0);
        }

        // ---------- Board-specific retrieval ----------

        [Test]
        public void I2c_IsBoardSpecific()
        {
            Assert.AreEqual("A4,A5", Highlights(Ask("Where are the I2C pins?", Spduino)), "SPDUINO has no SDA/SCL header pins");
            Assert.AreEqual("A4,A5,SCL,SDA", Highlights(Ask("Where are the I2C pins?", Uno)));
        }

        [Test]
        public void BoardInfo_UsesBoardFacts()
        {
            var r = Ask("Which microcontroller is on this board?", Spduino);
            Assert.AreEqual(QueryKind.BoardInfo, r.Kind);
            StringAssert.Contains("FT232RL", r.Answer);
            StringAssert.Contains("not confirmed", r.Answer);
            StringAssert.Contains("ATmega16U2", Ask("Which microcontroller is on this board?", Uno).Answer);
        }

        [Test]
        public void BoardFacts_CarryTheirOwnStatusAndSource()
        {
            var facts = KnowledgeBase.For(Spduino).Where(f => f.Kind == FactKind.Board).ToList();
            var mcu = facts.Single(f => f.Text.Contains("part number"));
            Assert.AreEqual(VerificationStatus.Inferred, mcu.Status, "MCU part number is not confirmed");
            var ftdi = facts.Single(f => f.Text.Contains("FT232RL"));
            Assert.AreEqual(VerificationStatus.VerifiedOnBoard, ftdi.Status);
            StringAssert.Contains("marking", ftdi.Source);
            Assert.IsTrue(facts.All(f => !f.Source.Contains("silkscreen label")), "board facts are not attributed to pin labels");
        }

        [Test]
        public void Unsupported_CarriesOnlyTheRatingCaveat()
        {
            var r = Ask("How much current can pin 13 supply?", Spduino);
            Assert.AreEqual(1, r.Caveats.Count);
            Assert.AreEqual(0, r.HighlightPins.Count);
        }

        [Test]
        public void LexicalFallback_DoesNotReturnPinFacts()
        {
            var r = Ask("What is a pull up resistor?", Spduino);
            Assert.IsTrue(r.RetrievedFacts.All(f => f.Kind == FactKind.Concept || f.Kind == FactKind.Board));
        }

        [Test]
        public void RetrievedFacts_AreFromTheActiveBoardOrGeneral()
        {
            var r = Ask("Which pins support PWM?", Spduino);
            Assert.IsTrue(r.RetrievedFacts.All(f => f.BoardId == Spduino.Id || (f.BoardId == null && f.Status == VerificationStatus.General)));
        }

        // ---------- Verified vs unverified ----------

        [Test]
        public void SpduinoPwm_IsVerifiedOnBoard_EvenOnPinsWithInferredSpiRoles()
        {
            var r = Ask("Which pins support PWM?", Spduino);
            var pwmFacts = r.RetrievedFacts.Where(f => f.Capability == PinCapability.Pwm).ToList();
            Assert.AreEqual(6, pwmFacts.Count);
            Assert.IsTrue(pwmFacts.All(f => f.Status == VerificationStatus.VerifiedOnBoard), "~ marks are on the silkscreen");
            Assert.IsFalse((r.Uncertainty ?? "").Contains("Unverified"), "no unverified caveat for PWM");
        }

        [Test]
        public void SpduinoSpi_IsMarkedUnverified()
        {
            var r = Ask("Which pins are used for SPI?", Spduino);
            Assert.IsTrue(r.RetrievedFacts.Where(f => f.Kind == FactKind.PinCapability).All(f => f.Status == VerificationStatus.Inferred));
            StringAssert.Contains("Unverified on", r.Uncertainty);
            Assert.IsTrue(r.Evidence.Any(e => e.Contains("UNVERIFIED")));
        }

        [Test]
        public void SpduinoI2cCheck_SaysProbablyNotYes()
        {
            var r = Ask("Is A4 used for I2C?", Spduino);
            StringAssert.StartsWith("Probably", r.Answer);
            StringAssert.StartsWith("Yes", Ask("Is A4 used for I2C?", Uno).Answer);
        }

        [Test]
        public void KnowledgeBase_EveryFactHasSourceAndStatus_AndNoInventedSpduinoRatings()
        {
            foreach (var board in new[] { Spduino, Uno })
                foreach (var f in KnowledgeBase.For(board))
                {
                    Assert.IsFalse(string.IsNullOrEmpty(f.Source), f.Id);
                    Assert.IsFalse(string.IsNullOrEmpty(f.Text), f.Id);
                    if (f.BoardId == null) Assert.AreEqual(VerificationStatus.General, f.Status, f.Id);
                }
            Assert.IsFalse(KnowledgeBase.For(Spduino).Any(f => f.BoardId == Spduino.Id && Regex.IsMatch(f.Text, @"\d+\s*mA")), "no current ratings for SPDUINO");
            // Every SPDUINO pin capability has exactly one fact with the capability's own status.
            foreach (var pin in Spduino.Pins)
                foreach (var cap in pin.Capabilities)
                {
                    var fact = KnowledgeBase.For(Spduino).Single(f => f.Kind == FactKind.PinCapability && f.PinId == pin.Id && f.Capability == cap);
                    Assert.AreEqual(pin.InferredCapabilities.Contains(cap) ? VerificationStatus.Inferred : VerificationStatus.VerifiedOnBoard, fact.Status, fact.Id);
                }
        }

        // ---------- AR highlights ----------

        [Test]
        public void Highlights_AreExactlyThePinsOfTheRetrievedCapabilityFacts()
        {
            foreach (var q in new[] { "Which pins support PWM?", "Where are the I2C pins?", "Which pins are used for SPI?", "What is PWM?" })
            {
                var r = Ask(q, Spduino);
                var fromFacts = r.RetrievedFacts.Where(f => f.Kind == FactKind.PinCapability).Select(f => f.PinId).Distinct().OrderBy(x => x);
                Assert.AreEqual(string.Join(",", fromFacts), Highlights(r), q);
                Assert.IsTrue(r.HighlightPins.All(p => Spduino.Pins.Contains(p)), "highlights are records of the active board");
            }
        }

        [Test]
        public void StructuredResponse_IsComplete()
        {
            var r = Ask("Which pins support PWM?", Spduino);
            Assert.AreEqual("pwm.list", r.IntentId);
            Assert.AreEqual(Spduino.Id, r.BoardId);
            CollectionAssert.Contains(r.Entities.Terms, "pwm");
            Assert.Greater(r.Evidence.Count, 0);
            Assert.AreEqual("High", r.ConfidenceLabel);
            StringAssert.Contains("Confidence: High", r.ToDisplayText());
        }

        [Test]
        public void PinQueryEngine_CompatibilityWrapper_StillWorks()
        {
            var r = PinQueryEngine.Query("Which pins support PWM?");
            Assert.AreEqual("D3,D5,D6,D9,D10,D11", string.Join(",", r.Records.Select(p => p.Id)));
            Assert.AreSame(ArduinoUnoProfile.Profile, r.Profile);
        }

        // ---------- Duplicate submission ----------

        [Test]
        public void SubmissionGate_BlocksSameTextInsideWindow_AllowsOtherwise()
        {
            var gate = new SubmissionGate(1.5f);
            Assert.IsTrue(gate.TryAccept("what does pin 9 do", 10f));
            Assert.IsFalse(gate.TryAccept("what does pin 9 do", 10.2f), "Done then Ask");
            Assert.IsFalse(gate.TryAccept("  what does pin 9 do ", 10.4f), "whitespace-only difference");
            Assert.IsTrue(gate.TryAccept("which pins are digital", 10.5f), "different question");
            Assert.IsTrue(gate.TryAccept("what does pin 9 do", 13f), "same question again later");
            Assert.IsFalse(gate.TryAccept("   ", 20f), "blank ignored");
        }
    }
}
