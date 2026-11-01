using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ArduinoAR.Tests
{
    /// <summary>Voice flow tests with fake recognizer / TTS / permission backends (no device needed).</summary>
    public class VoiceInteractionTests
    {
        sealed class FakeStt : ISpeechToText
        {
            public bool Available = true;
            public readonly Queue<SpeechEvent> Events = new();
            public int StartCount, CancelCount, LastSession;
            public bool IsAvailable => Available;
            public void StartListening(int sessionId) { StartCount++; LastSession = sessionId; }
            public void Cancel() => CancelCount++;
            public bool TryDequeue(out SpeechEvent e) => Events.TryDequeue(out e);
            public void Dispose() { }
            public void Push(SpeechEventType type, params string[] transcripts) => Events.Enqueue(new SpeechEvent(type, LastSession, transcripts));
            public void PushError(int code) => Events.Enqueue(new SpeechEvent(SpeechEventType.Error, LastSession, null, code));
        }

        sealed class FakeTts : ITextToSpeech
        {
            public bool Ready = true, Speaking;
            public readonly List<string> Spoken = new();
            public int StopCount;
            public bool IsReady => Ready;
            public bool IsSpeaking => Speaking;
            public void Speak(string text) { Spoken.Add(text); Speaking = true; }
            public void Stop() { StopCount++; Speaking = false; }
            public void Dispose() { }
        }

        sealed class FakeMic : IMicrophonePermission
        {
            public bool Has = true;
            public Action<MicPermissionResult> Pending;
            public bool HasPermission => Has;
            public void Request(Action<MicPermissionResult> done) => Pending = done;
        }

        FakeStt m_Stt;
        FakeTts m_Tts;
        FakeMic m_Mic;
        List<string> m_Submitted;
        VoiceInteraction m_Voice;

        [SetUp]
        public void SetUp()
        {
            m_Stt = new FakeStt();
            m_Tts = new FakeTts();
            m_Mic = new FakeMic();
            m_Submitted = new List<string>();
            m_Voice = new VoiceInteraction(m_Stt, m_Tts, m_Mic, q => m_Submitted.Add(q));
        }

        // ---------- transcript submission ----------

        [Test]
        public void Transcript_IsSubmittedExactlyOnce()
        {
            m_Voice.StartListening(0f);
            m_Stt.Push(SpeechEventType.ReadyForSpeech);
            m_Stt.Push(SpeechEventType.PartialResults, "which pins");
            m_Stt.Push(SpeechEventType.Results, "which pins support PWM", "which pins support p w m");
            m_Stt.Push(SpeechEventType.Results, "which pins support PWM"); // duplicate callback
            m_Voice.Tick(1f);
            CollectionAssert.AreEqual(new[] { "which pins support PWM" }, m_Submitted);
            Assert.AreEqual(1, m_Voice.SubmittedCount);
            Assert.IsFalse(m_Voice.IsListening);
            StringAssert.Contains("You asked", m_Voice.Status);
        }

        [Test]
        public void ResultsAfterCancel_AreIgnored()
        {
            m_Voice.StartListening(0f);
            m_Voice.ToggleListening(0.5f); // user taps Stop
            m_Stt.Push(SpeechEventType.Results, "what does pin 9 do");
            m_Voice.Tick(1f);
            Assert.AreEqual(0, m_Submitted.Count);
            Assert.AreEqual(1, m_Stt.CancelCount);
        }

        [Test]
        public void ResultsFromAnOlderSession_AreIgnored()
        {
            m_Voice.StartListening(0f);
            int old = m_Stt.LastSession;
            m_Voice.CancelListening(null);
            m_Voice.StartListening(1f);
            m_Stt.Events.Enqueue(new SpeechEvent(SpeechEventType.Results, old, new[] { "stale" }));
            m_Stt.Push(SpeechEventType.Results, "fresh question");
            m_Voice.Tick(2f);
            CollectionAssert.AreEqual(new[] { "fresh question" }, m_Submitted);
        }

        [TestCase("")]
        [TestCase("   ")]
        public void EmptyTranscript_IsNotSubmitted(string transcript)
        {
            m_Voice.StartListening(0f);
            m_Stt.Push(SpeechEventType.Results, transcript);
            m_Voice.Tick(1f);
            Assert.AreEqual(0, m_Submitted.Count);
            StringAssert.Contains("Didn't catch", m_Voice.Status);
        }

        // ---------- unavailable / permission / errors / timeouts ----------

        [Test]
        public void UnavailableService_ShowsMessage_AndDoesNotListen()
        {
            m_Stt.Available = false;
            m_Voice.StartListening(0f);
            Assert.AreEqual(0, m_Stt.StartCount);
            Assert.IsFalse(m_Voice.IsListening);
            StringAssert.Contains("isn't available", m_Voice.Status);
            StringAssert.Contains("Type your question", m_Voice.Status);
        }

        [Test]
        public void NullBackends_AreTreatedAsUnavailable()
        {
            var v = new VoiceInteraction(null, null, null, q => m_Submitted.Add(q));
            v.StartListening(0f);
            v.Tick(1f);
            v.OnAnswer(BoardAssistant.Process("Which pins support PWM?", SpduinoProfile.Profile), 1f, "the board");
            StringAssert.Contains("isn't available", v.Status);
        }

        [TestCase(MicPermissionResult.Denied, "denied")]
        [TestCase(MicPermissionResult.DeniedPermanently, "Settings")]
        [TestCase(MicPermissionResult.Dismissed, "not granted")]
        public void PermissionNotGranted_DoesNotListen(MicPermissionResult result, string expected)
        {
            m_Mic.Has = false;
            m_Voice.StartListening(0f);
            Assert.IsTrue(m_Voice.IsAwaitingPermission);
            m_Mic.Pending(result);
            Assert.AreEqual(0, m_Stt.StartCount);
            Assert.IsFalse(m_Voice.IsListening);
            StringAssert.Contains(expected, m_Voice.Status);
        }

        [Test]
        public void PermissionGranted_StartsListening()
        {
            m_Mic.Has = false;
            m_Voice.StartListening(0f);
            m_Voice.OnPause(); // the permission dialog pauses the app; must keep waiting
            Assert.IsTrue(m_Voice.IsAwaitingPermission);
            m_Mic.Pending(MicPermissionResult.Granted);
            Assert.AreEqual(1, m_Stt.StartCount);
            Assert.IsTrue(m_Voice.IsListening);
        }

        [TestCase(SpeechErrors.NoMatch, "Didn't catch")]
        [TestCase(SpeechErrors.SpeechTimeout, "No speech heard")]
        [TestCase(SpeechErrors.Network, "network")]
        [TestCase(SpeechErrors.InsufficientPermissions, "permission")]
        [TestCase(SpeechErrors.LanguageUnavailable, "language")]
        [TestCase(99, "code 99")]
        public void RecognizerErrors_EndTheSession_WithAClearMessage(int code, string expected)
        {
            m_Voice.StartListening(0f);
            m_Stt.PushError(code);
            m_Voice.Tick(1f);
            Assert.IsFalse(m_Voice.IsListening);
            Assert.AreEqual(0, m_Submitted.Count);
            StringAssert.Contains(expected, m_Voice.Status);
        }

        [Test]
        public void NoResponseFromService_TimesOut()
        {
            m_Voice.StartListening(0f);
            m_Voice.Tick(VoiceInteraction.StartTimeoutSeconds + 0.1f);
            Assert.IsFalse(m_Voice.IsListening);
            Assert.AreEqual(1, m_Stt.CancelCount);
            StringAssert.Contains("did not respond", m_Voice.Status);
        }

        [Test]
        public void ListeningForTooLong_TimesOut()
        {
            m_Voice.StartListening(0f);
            m_Stt.Push(SpeechEventType.ReadyForSpeech);
            m_Voice.Tick(1f);
            m_Voice.Tick(VoiceInteraction.ListenTimeoutSeconds + 0.1f);
            Assert.IsFalse(m_Voice.IsListening);
            StringAssert.Contains("timed out", m_Voice.Status);
        }

        [Test]
        public void Pause_CancelsListening_AndStopsSpeech()
        {
            m_Voice.StartListening(0f);
            m_Tts.Speaking = true;
            m_Voice.OnPause();
            Assert.IsFalse(m_Voice.IsListening);
            Assert.AreEqual(1, m_Stt.CancelCount);
            Assert.GreaterOrEqual(m_Tts.StopCount, 1);
        }

        [Test]
        public void StartingToListen_StopsCurrentSpeech()
        {
            m_Tts.Speaking = true;
            m_Voice.StartListening(0f);
            Assert.AreEqual(1, m_Tts.StopCount, "the mic must not record our own answer");
        }

        // ---------- text-to-speech answer selection ----------

        static AssistantResponse Pwm => BoardAssistant.Process("Which pins support PWM?", SpduinoProfile.Profile);

        [Test]
        public void SpokenText_IsTheAnswerOnly()
        {
            var r = Pwm;
            var spoken = SpokenAnswer.From(r, "the SPDUINO board");
            StringAssert.Contains("D3, D5, D6, D9, D10, D11", spoken);
            StringAssert.Contains("the SPDUINO board", spoken);
            foreach (var excluded in new[] { "Evidence", "Confidence", "Source:", "⚠", "•", "verified on this board", SpduinoProfile.Profile.DisplayName })
                StringAssert.DoesNotContain(excluded, spoken);
            StringAssert.Contains("Evidence", r.ToDisplayText(), "the screen still shows evidence");
        }

        [Test]
        public void SpokenText_IsCappedAtASentenceBoundary()
        {
            var r = BoardAssistant.Process("What is PWM and how is it used?", SpduinoProfile.Profile);
            var spoken = SpokenAnswer.From(r);
            Assert.LessOrEqual(spoken.Length, SpokenAnswer.MaxLength);
            Assert.IsTrue(spoken.EndsWith("."));
        }

        [Test]
        public void Answer_IsSpoken_WhenCurrent()
        {
            m_Voice.OnQuestionAsked(false, 0f);
            m_Voice.OnAnswer(Pwm, 0.1f, "the SPDUINO board");
            Assert.AreEqual(1, m_Tts.Spoken.Count);
            Assert.AreEqual(m_Voice.LastSpokenText, m_Tts.Spoken[0]);
        }

        [Test]
        public void Answer_IsNotSpoken_WhenDisabledOrTtsNotReady()
        {
            m_Voice.SpeakAnswers = false;
            m_Voice.OnQuestionAsked(false, 0f);
            m_Voice.OnAnswer(Pwm, 0.1f, "x");
            m_Voice.SpeakAnswers = true;
            m_Tts.Ready = false;
            m_Voice.OnAnswer(Pwm, 0.2f, "x");
            Assert.AreEqual(0, m_Tts.Spoken.Count);
        }

        [Test]
        public void NewQuestion_StopsTheStaleAnswer()
        {
            m_Voice.OnQuestionAsked(false, 0f);
            m_Voice.OnAnswer(Pwm, 0.1f, "x");
            Assert.IsTrue(m_Tts.Speaking);
            m_Voice.OnQuestionAsked(false, 2f);
            Assert.IsFalse(m_Tts.Speaking);
            Assert.AreEqual(1, m_Tts.StopCount);
        }

        [Test]
        public void PendingReplay_IsSpokenOnlyIfRecent()
        {
            m_Voice.OnQuestionAsked(false, 0f);          // asked before board recognition
            m_Voice.OnQuestionAsked(true, 30f);          // replayed on recognition (keeps original time)
            m_Voice.OnAnswer(Pwm, 30f, "x");
            Assert.AreEqual(1, m_Tts.Spoken.Count, "30 s old: still relevant");

            m_Voice.OnQuestionAsked(false, 100f);
            m_Voice.OnQuestionAsked(true, 100f + SpeechPolicy.MaxAnswerAgeSeconds + 5f);
            m_Voice.OnAnswer(Pwm, 100f + SpeechPolicy.MaxAnswerAgeSeconds + 5f, "x");
            Assert.AreEqual(1, m_Tts.Spoken.Count, "too old: not spoken");
        }

        [Test]
        public void SpeakerButton_StopsSpeech_ThenTogglesSpokenAnswers()
        {
            m_Tts.Speaking = true;
            m_Voice.ToggleSpeech();
            Assert.IsFalse(m_Tts.Speaking);
            Assert.IsTrue(m_Voice.SpeakAnswers, "first press only stops the current answer");
            m_Voice.ToggleSpeech();
            Assert.IsFalse(m_Voice.SpeakAnswers);
        }

        [Test]
        public void ErrorMessages_CoverEveryAndroidCode()
        {
            for (int code = 1; code <= 15; code++)
                Assert.IsFalse(string.IsNullOrEmpty(SpeechErrors.Describe(code)), code.ToString());
        }
    }
}
