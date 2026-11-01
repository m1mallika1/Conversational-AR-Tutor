using System;

namespace ArduinoAR
{
    public enum MicPermissionResult { Granted, Denied, DeniedPermanently, Dismissed }

    /// <summary>Microphone permission (Android runtime permission on device, fakes in tests).</summary>
    public interface IMicrophonePermission
    {
        bool HasPermission { get; }
        /// <summary>Ask the user; <paramref name="done"/> is called on the main thread with the outcome.</summary>
        void Request(Action<MicPermissionResult> done);
    }

    /// <summary>
    /// Voice question flow, independent of Unity so it can be unit-tested:
    /// mic permission → listen → one transcript per session → submitted through the existing question path;
    /// answers are spoken (answer text only) if still current. Time is passed in by the caller.
    /// </summary>
    public sealed class VoiceInteraction
    {
        /// <summary>No "ready for speech" from the service within this time → give up.</summary>
        public const float StartTimeoutSeconds = 8f;
        /// <summary>Overall cap for one listening session.</summary>
        public const float ListenTimeoutSeconds = 20f;

        readonly ISpeechToText m_Stt;
        readonly ITextToSpeech m_Tts;
        readonly IMicrophonePermission m_Mic;
        readonly Action<string> m_Submit;
        readonly Func<string, bool> m_Understands;
        readonly VoiceSessionTracker m_Session = new();

        float m_Now;
        float m_ListenStartedAt;
        bool m_GotReady;
        bool m_AwaitingPermission;
        int m_QuestionSeq;
        float m_LatestAskedAt;

        public bool SpeakAnswers = true;
        public string Status { get; private set; } = "";
        public float StatusTime { get; private set; }
        public bool IsListening => m_Session.IsListening;
        public bool IsAwaitingPermission => m_AwaitingPermission;
        public bool IsSpeaking => m_Tts != null && m_Tts.IsSpeaking;
        public bool VoiceInputAvailable => m_Stt != null && m_Stt.IsAvailable;
        /// <summary>Number of transcripts handed to the question path (diagnostics/tests).</summary>
        public int SubmittedCount { get; private set; }
        /// <summary>Last text sent to text-to-speech (diagnostics/tests).</summary>
        public string LastSpokenText { get; private set; }
        /// <summary>Which recognizer alternative the last final result submitted (diagnostics/tests).</summary>
        public TranscriptChoice LastChoice { get; private set; }

        /// <param name="understands">Optional check used to prefer a recognizer alternative the question
        /// classifier understands (e.g. <see cref="TranscriptSelector.ClassifierUnderstands"/>); without it the
        /// first non-empty alternative is submitted.</param>
        public VoiceInteraction(ISpeechToText stt, ITextToSpeech tts, IMicrophonePermission mic, Action<string> submitQuestion,
            Func<string, bool> understands = null)
        {
            m_Stt = stt;
            m_Tts = tts;
            m_Mic = mic;
            m_Submit = submitQuestion;
            m_Understands = understands;
        }

        void SetStatus(string s) { Status = s; StatusTime = m_Now; }

        public void ToggleListening(float now)
        {
            m_Now = now;
            if (IsListening || m_AwaitingPermission) CancelListening("Listening cancelled.");
            else StartListening(now);
        }

        public void StartListening(float now)
        {
            m_Now = now;
            if (IsListening || m_AwaitingPermission) return;
            if (!VoiceInputAvailable)
            {
                SetStatus("Voice input isn't available on this device (no speech recognition service). Type your question instead.");
                return;
            }
            StopSpeaking(); // don't record our own answer
            if (m_Mic != null && !m_Mic.HasPermission)
            {
                m_AwaitingPermission = true;
                SetStatus("Allow microphone access to ask by voice.");
                m_Mic.Request(OnPermissionResult);
                return;
            }
            BeginSession();
        }

        void OnPermissionResult(MicPermissionResult result)
        {
            if (!m_AwaitingPermission) return; // cancelled meanwhile
            m_AwaitingPermission = false;
            switch (result)
            {
                case MicPermissionResult.Granted: BeginSession(); break;
                case MicPermissionResult.DeniedPermanently:
                    SetStatus("Microphone permission is blocked. Enable it in Android Settings > Apps, or type your question."); break;
                case MicPermissionResult.Dismissed:
                    SetStatus("Microphone permission was not granted. Tap the mic to try again, or type your question."); break;
                default:
                    SetStatus("Microphone permission denied. Type your question instead."); break;
            }
        }

        void BeginSession()
        {
            int id = m_Session.Begin();
            m_ListenStartedAt = m_Now;
            m_GotReady = false;
            SetStatus("Starting microphone...");
            try { m_Stt.StartListening(id); }
            catch (Exception ex)
            {
                m_Session.Cancel();
                SetStatus("Could not start speech recognition. Type your question instead.");
                UnityEngine.Debug.LogWarning("[Voice] start failed: " + ex.Message);
            }
        }

        public void CancelListening(string message)
        {
            bool wasActive = IsListening || m_AwaitingPermission;
            m_AwaitingPermission = false;
            m_Session.Cancel();
            if (wasActive) { m_Stt?.Cancel(); if (message != null) SetStatus(message); }
        }

        /// <summary>Drain recognizer events and enforce timeouts. Call once per frame on the main thread.</summary>
        public void Tick(float now)
        {
            m_Now = now;
            while (m_Stt != null && m_Stt.TryDequeue(out var e))
            {
                switch (e.Type)
                {
                    case SpeechEventType.ReadyForSpeech:
                        if (e.SessionId == m_Session.ActiveSession) { m_GotReady = true; SetStatus("Listening... ask your question."); }
                        break;
                    case SpeechEventType.PartialResults:
                        if (e.SessionId == m_Session.ActiveSession && e.Transcripts.Count > 0 && !string.IsNullOrWhiteSpace(e.Transcripts[0]))
                            SetStatus("Heard: " + e.Transcripts[0]);
                        break;
                    case SpeechEventType.Results:
                        bool current = e.SessionId != 0 && e.SessionId == m_Session.ActiveSession;
                        var transcript = m_Session.TryConsumeResult(e, out bool empty, out var choice, m_Understands);
                        if (current) LogChoice(e.Transcripts, choice, m_Understands != null);
                        if (empty) SetStatus("Didn't catch that. Tap the mic and try again, or type your question.");
                        else if (transcript != null)
                        {
                            LastChoice = choice;
                            SetStatus($"You asked: \"{transcript}\"");
                            SubmittedCount++;
                            m_Submit?.Invoke(transcript); // the existing typed-question path; no separate NLP
                        }
                        break;
                    case SpeechEventType.Error:
                        if (m_Session.TryConsumeError(e)) SetStatus(SpeechErrors.Describe(e.ErrorCode));
                        break;
                }
            }

            if (IsListening)
            {
                float elapsed = now - m_ListenStartedAt;
                if (!m_GotReady && elapsed > StartTimeoutSeconds)
                    CancelListening("The speech service did not respond. Type your question instead.");
                else if (elapsed > ListenTimeoutSeconds)
                    CancelListening("Listening timed out. Tap the mic and try again.");
            }
        }

        /// <summary>
        /// One concise line per final result: the recognizer's alternatives (each shortened) and which one was
        /// submitted. Only the recognized question text is logged.
        /// </summary>
        static void LogChoice(System.Collections.Generic.IReadOnlyList<string> alternatives, TranscriptChoice choice, bool checkedAlternatives)
        {
            static string Short(string s) => s == null ? "" : s.Length <= 80 ? s.Trim() : s.Substring(0, 80).Trim() + "...";
            var heard = new System.Text.StringBuilder();
            for (int i = 0; i < alternatives.Count; i++) heard.Append(i == 0 ? "" : " | ").Append('"').Append(Short(alternatives[i])).Append('"');
            string chose = choice.Text == null ? "none (empty)"
                : $"#{choice.Index + 1} \"{Short(choice.Text)}\" ({(choice.Understood ? "understood" : checkedAlternatives ? "fallback: none understood" : "first alternative")})";
            UnityEngine.Debug.Log($"[Voice] heard {alternatives.Count} alternative(s): {heard} -> chose {chose}");
        }

        /// <summary>Call whenever a question is asked (typed, preset or voice). Replays of a pending question keep their original time.</summary>
        public void OnQuestionAsked(bool isPendingReplay, float now)
        {
            m_Now = now;
            if (isPendingReplay) return;
            m_QuestionSeq++;
            m_LatestAskedAt = now;
            StopSpeaking(); // the previous answer is now stale
        }

        /// <summary>Speak the answer text only, if it answers the latest question and isn't too old.</summary>
        public void OnAnswer(AssistantResponse response, float now, string spokenBoardName)
        {
            m_Now = now;
            if (m_Tts == null || !m_Tts.IsReady) return;
            if (!SpeechPolicy.ShouldSpeak(SpeakAnswers, m_QuestionSeq, m_QuestionSeq, m_LatestAskedAt, now)) return;
            var text = SpokenAnswer.From(response, spokenBoardName);
            if (string.IsNullOrEmpty(text)) return;
            LastSpokenText = text;
            m_Tts.Speak(text);
        }

        public void StopSpeaking() { if (m_Tts != null && m_Tts.IsSpeaking) m_Tts.Stop(); }

        /// <summary>Speaker button: stops current speech, otherwise toggles spoken answers on/off.</summary>
        public void ToggleSpeech()
        {
            if (IsSpeaking) { m_Tts.Stop(); return; }
            SpeakAnswers = !SpeakAnswers;
        }

        /// <summary>App paused (Home, permission dialog, screen off): stop mic and speech.</summary>
        public void OnPause()
        {
            if (m_AwaitingPermission) return; // the permission dialog itself pauses the app; keep waiting
            CancelListening("Listening stopped because the app was paused.");
            m_Tts?.Stop();
        }

        public void Dispose()
        {
            m_Session.Cancel();
            m_Stt?.Dispose();
            m_Tts?.Dispose();
        }
    }
}
