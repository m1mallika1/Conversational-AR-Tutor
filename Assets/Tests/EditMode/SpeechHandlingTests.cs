using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ArduinoAR.Tests
{
    /// <summary>
    /// Speech-to-text robustness: spoken/spelled forms of technical terms, number words in pin references,
    /// false-positive guards, and choosing among the recognizer's alternatives. No device needed.
    /// </summary>
    public class SpeechHandlingTests
    {
        static BoardProfile Spduino => SpduinoProfile.Profile;
        static BoardProfile Uno => ArduinoUnoProfile.Profile;

        static string Norm(string q) => TextAnalysis.Normalize(q);
        static bool HasToken(string normalized, string token) => normalized.Split(' ').Contains(token);
        static string Highlights(AssistantResponse r) => string.Join(",", r.HighlightIds.OrderBy(x => x));

        // ---------- I2C variants ----------

        [TestCase("Where are the I2C pins?")]
        [TestCase("Where are the I²C pins?")]
        [TestCase("Where are the i^2c pins?")]
        [TestCase("Where are the I 2 C pins?")]
        [TestCase("Where are the I-2-C pins?")]
        [TestCase("Where are the I.2.C. pins?")]
        [TestCase("Where are the I 2C pins?")]
        [TestCase("Where are the I2 C pins?")]
        [TestCase("Where are the I squared C pins?")]
        [TestCase("Where are the I square C pins?")]
        [TestCase("Where are the I-squared-C pins?")]
        [TestCase("Where are the I two C pins?")]
        [TestCase("Where are the I to C pins?")]
        [TestCase("Where are the I too C pins?")]
        [TestCase("Where are the eye two see pins?")]
        [TestCase("Where are the eye squared see pins?")]
        [TestCase("Where are the I squared sea pins?")]
        [TestCase("Where are the I I C pins?")]
        public void I2cVariants_ListI2cPins(string q)
        {
            var spd = BoardAssistant.Process(q, Spduino);
            Assert.AreEqual("i2c.list", spd.IntentId, $"{q} -> \"{spd.NormalizedQuestion}\"");
            Assert.AreEqual("A4,A5", Highlights(spd), q);

            var uno = BoardAssistant.Process(q, Uno);
            Assert.AreEqual("i2c.list", uno.IntentId, q);
            CollectionAssert.IsSubsetOf(new[] { "A4", "A5" }, uno.HighlightIds.ToList(), q);
        }

        [TestCase("What is I squared C?")]
        [TestCase("what is i two c")]
        [TestCase("Explain eye to see")]
        public void I2cVariants_ExplainQuestions(string q)
        {
            var r = BoardAssistant.Process(q, Spduino);
            Assert.AreEqual("i2c.explain", r.IntentId, $"{q} -> \"{r.NormalizedQuestion}\"");
        }

        // ---------- Spelled-out acronyms ----------

        [TestCase("Which pins support P W M?", "pwm", "pwm.list")]
        [TestCase("which pins support p.w.m.", "pwm", "pwm.list")]
        [TestCase("Which pins are used for S P I?", "spi", "spi.list")]
        [TestCase("Where is S D A?", "sda", "i2c.list")]
        [TestCase("Where is S-C-L?", "scl", "i2c.list")]
        [TestCase("Where are the G N D pins?", "gnd", "ground.list")]
        [TestCase("Which pins are U A R T?", "uart", "serial.list")]
        [TestCase("Which pins use the A D C?", "adc", "analog.list")]
        public void SpelledAcronyms_AreCollapsed(string q, string token, string intent)
        {
            var r = BoardAssistant.Process(q, Spduino);
            Assert.IsTrue(HasToken(r.NormalizedQuestion, token), $"\"{r.NormalizedQuestion}\" should contain {token}");
            Assert.AreEqual(intent, r.IntentId, q);
        }

        // ---------- Number words in pin references ----------

        [TestCase("What does pin thirteen do?", "D13")]
        [TestCase("what does pin number nine do", "D9")]
        [TestCase("What does analog pin zero do?", "A0")]
        [TestCase("What does pin A zero do?", "A0")]
        [TestCase("What does pin a 5 do?", "A5")]
        [TestCase("What does pin one do?", "D1")]
        public void NumberWords_AfterPin_BecomePinReferences(string q, string pin)
        {
            var r = BoardAssistant.Process(q, Uno);
            CollectionAssert.AreEqual(new[] { pin }, r.ResolvedPinIds.ToList(), $"{q} -> \"{r.NormalizedQuestion}\"");
        }

        [Test]
        public void NumberWords_PinList()
        {
            var r = BoardAssistant.Process("Do pins three and five support PWM?", Uno);
            CollectionAssert.AreEquivalent(new[] { "D3", "D5" }, r.ResolvedPinIds.ToList(), r.NormalizedQuestion);
            Assert.AreEqual(QueryKind.CapabilityCheck, r.Kind);
        }

        [Test]
        public void NumberWords_CapabilityCheck_SpokenPin()
        {
            var r = BoardAssistant.Process("Does pin five support PWM?", Uno);
            CollectionAssert.AreEqual(new[] { "D5" }, r.ResolvedPinIds.ToList(), r.NormalizedQuestion);
            StringAssert.StartsWith("Yes", r.Answer);
        }

        // ---------- False-positive guards: these must not change meaning ----------

        [TestCase("Can I see the PWM pins?", "can i see the pwm pins")]
        [TestCase("I want to see the analog pins", "i want to see the analog pins")]
        [TestCase("Do I need to see a diagram?", "do i need to see a diagram")]
        [TestCase("Which two pins are used for I2C?", "which two pins are used for i2c")]
        [TestCase("Connect this pin to ground", "connect this pin to ground")]
        [TestCase("Is one of the pins broken?", "is one of the pins broken")]
        [TestCase("Use pin one of the two headers", "use pin one of the two headers")]
        [TestCase("What's a DC motor?", "what is a dc motor")]
        [TestCase("It's a red board", "it s a red board")]
        [TestCase("Is the LED on pin 13?", "is the led on pin 13")]
        [TestCase("Tell me about the 5V pin", "tell me about the 5v pin")]
        [TestCase("Which pins can I use to dim an LED?", "which pins can i use to dim an led")]
        [TestCase("pins 3, 5 and 6", "pins 3 5 and 6")]
        [TestCase("Where do I plug in the sensor?", "where do i plug in the sensor")]
        public void Guards_LeaveOrdinaryWordingUnchanged(string q, string expected)
        {
            Assert.AreEqual(expected, Norm(q));
        }

        [Test]
        public void Guards_NoI2cFromOrdinarySeeing()
        {
            foreach (var q in new[] { "Can I see the pins?", "I want to see the board", "Do I need to see the datasheet?" })
                Assert.IsFalse(HasToken(Norm(q), "i2c"), q);
        }

        [Test]
        public void Guards_ExistingQuestionsStillAnswerTheSame()
        {
            Assert.AreEqual("D10,D11,D3,D5,D6,D9", Highlights(BoardAssistant.Process("Which pins support PWM?", Spduino)));
            Assert.AreEqual("A0,A1,A2,A3,A4,A5", Highlights(BoardAssistant.Process("Which pins are analog inputs?", Spduino)));
            CollectionAssert.AreEqual(new[] { "D9" }, BoardAssistant.Process("What does pin 9 do?", Uno).ResolvedPinIds.ToList());
        }

        // ---------- Choosing among recognizer alternatives ----------

        [Test]
        public void Selector_PrefersFirstUnderstoodAlternative()
        {
            var c = TranscriptSelector.Select(new[] { "what is high to sea", "what is I two C", "what is i2c" }, TranscriptSelector.ClassifierUnderstands);
            Assert.AreEqual(1, c.Index);
            Assert.AreEqual("what is I two C", c.Text);
            Assert.IsTrue(c.Understood);
        }

        [Test]
        public void Selector_FirstAlternativeWhenItIsUnderstood()
        {
            var c = TranscriptSelector.Select(new[] { "which pins support PWM", "which pins support p w m" }, TranscriptSelector.ClassifierUnderstands);
            Assert.AreEqual(0, c.Index);
            Assert.IsTrue(c.Understood);
        }

        [Test]
        public void Selector_FallsBackToFirstNonEmpty_WhenNoneUnderstood()
        {
            var c = TranscriptSelector.Select(new[] { "  ", "hello there", "good morning" }, TranscriptSelector.ClassifierUnderstands);
            Assert.AreEqual(1, c.Index);
            Assert.AreEqual("hello there", c.Text);
            Assert.IsFalse(c.Understood);
        }

        [Test]
        public void Selector_EmptyOrMissingAlternatives()
        {
            Assert.IsNull(TranscriptSelector.Select(new[] { "", null, " " }, TranscriptSelector.ClassifierUnderstands).Text);
            Assert.AreEqual(-1, TranscriptSelector.Select(new string[0], TranscriptSelector.ClassifierUnderstands).Index);
            Assert.IsNull(TranscriptSelector.Select(null, null).Text);
        }

        [Test]
        public void Selector_WithoutCheck_KeepsOldFirstNonEmptyBehaviour()
        {
            var c = TranscriptSelector.Select(new[] { "", "hello there", "what is i2c" }, null);
            Assert.AreEqual(1, c.Index);
            Assert.IsFalse(c.Understood);
        }

        [TestCase("what is I squared C", true)]
        [TestCase("does pin five support PWM", true)]
        [TestCase("what is the maximum current on pin 9", true)] // recognised (as unsupported), not unknown
        [TestCase("hello there", false)]
        [TestCase("", false)]
        public void ClassifierUnderstands(string text, bool expected)
        {
            Assert.AreEqual(expected, TranscriptSelector.ClassifierUnderstands(text));
        }

        // ---------- End to end through VoiceInteraction (fake recognizer) ----------

        sealed class FakeStt : ISpeechToText
        {
            public readonly Queue<SpeechEvent> Events = new();
            public int LastSession;
            public bool IsAvailable => true;
            public void StartListening(int sessionId) => LastSession = sessionId;
            public void Cancel() { }
            public bool TryDequeue(out SpeechEvent e) => Events.TryDequeue(out e);
            public void Dispose() { }
        }

        [Test]
        public void Voice_SubmitsTheUnderstoodAlternative_Once()
        {
            var stt = new FakeStt();
            var submitted = new List<string>();
            var voice = new VoiceInteraction(stt, null, null, submitted.Add, TranscriptSelector.ClassifierUnderstands);
            voice.StartListening(0f);
            stt.Events.Enqueue(new SpeechEvent(SpeechEventType.Results, stt.LastSession, new[] { "where are the high to sea pins", "where are the I two C pins" }));
            stt.Events.Enqueue(new SpeechEvent(SpeechEventType.Results, stt.LastSession, new[] { "duplicate callback" }));
            voice.Tick(1f);
            CollectionAssert.AreEqual(new[] { "where are the I two C pins" }, submitted);
            Assert.AreEqual(1, voice.LastChoice.Index);
            Assert.IsTrue(voice.LastChoice.Understood);
            Assert.AreEqual("i2c.list", BoardAssistant.Process(submitted[0], Spduino).IntentId);
        }

        [Test]
        public void Voice_FallsBackToFirstAlternative_WhenNoneUnderstood()
        {
            var stt = new FakeStt();
            var submitted = new List<string>();
            var voice = new VoiceInteraction(stt, null, null, submitted.Add, TranscriptSelector.ClassifierUnderstands);
            voice.StartListening(0f);
            stt.Events.Enqueue(new SpeechEvent(SpeechEventType.Results, stt.LastSession, new[] { "good morning", "good mourning" }));
            voice.Tick(1f);
            CollectionAssert.AreEqual(new[] { "good morning" }, submitted);
            Assert.IsFalse(voice.LastChoice.Understood);
        }
    }
}
