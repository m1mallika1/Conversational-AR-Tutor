using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArduinoAR
{
    public enum SpeechEventType { ReadyForSpeech, BeginningOfSpeech, EndOfSpeech, PartialResults, Results, Error }

    /// <summary>One recognizer callback, queued so it can be handled on Unity's main thread.</summary>
    public readonly struct SpeechEvent
    {
        public readonly SpeechEventType Type;
        public readonly int SessionId;
        public readonly IReadOnlyList<string> Transcripts;
        public readonly int ErrorCode;

        public SpeechEvent(SpeechEventType type, int sessionId, IReadOnlyList<string> transcripts = null, int errorCode = 0)
        {
            Type = type;
            SessionId = sessionId;
            Transcripts = transcripts ?? new string[0];
            ErrorCode = errorCode;
        }
    }

    /// <summary>Speech-to-text backend (Android SpeechRecognizer on device, fakes in tests).</summary>
    public interface ISpeechToText
    {
        /// <summary>False when no recognition service exists on this device/platform.</summary>
        bool IsAvailable { get; }
        /// <summary>Start listening; events for this session carry <paramref name="sessionId"/>.</summary>
        void StartListening(int sessionId);
        /// <summary>Stop listening and discard the session.</summary>
        void Cancel();
        /// <summary>Dequeue the next pending event (call from the main thread).</summary>
        bool TryDequeue(out SpeechEvent e);
        void Dispose();
    }

    /// <summary>Text-to-speech backend (Android TextToSpeech on device, fakes in tests).</summary>
    public interface ITextToSpeech
    {
        bool IsReady { get; }
        bool IsSpeaking { get; }
        void Speak(string text);
        void Stop();
        void Dispose();
    }

    /// <summary>Android SpeechRecognizer error codes mapped to user-facing messages.</summary>
    public static class SpeechErrors
    {
        public const int NetworkTimeout = 1, Network = 2, Audio = 3, Server = 4, Client = 5, SpeechTimeout = 6, NoMatch = 7,
            RecognizerBusy = 8, InsufficientPermissions = 9, TooManyRequests = 10, ServerDisconnected = 11,
            LanguageNotSupported = 12, LanguageUnavailable = 13, CannotCheckSupport = 14, CannotListenToDownloadEvents = 15;

        public static string Describe(int code) => code switch
        {
            NetworkTimeout or Network or Server or ServerDisconnected or TooManyRequests =>
                "Speech service needs a network connection on this device and could not reach it. Type your question instead.",
            Audio => "The microphone could not be read. Type your question instead.",
            SpeechTimeout => "No speech heard. Tap the mic and ask again.",
            NoMatch => "Didn't catch that. Tap the mic and try again, or type your question.",
            RecognizerBusy => "Speech service is busy. Try again in a moment.",
            InsufficientPermissions => "Microphone permission is required for voice questions.",
            LanguageNotSupported or LanguageUnavailable => "The speech service does not support this language on this device. Type your question instead.",
            Client => "Listening stopped.",
            _ => $"Speech recognition failed (code {code}). Type your question instead.",
        };
    }

    /// <summary>
    /// Ensures each listening session submits at most one transcript, and that results arriving after a
    /// cancel (or from an older session) are ignored.
    /// </summary>
    public sealed class VoiceSessionTracker
    {
        int m_NextId = 1;
        public int ActiveSession { get; private set; }
        public bool IsListening => ActiveSession != 0;

        public int Begin()
        {
            ActiveSession = m_NextId++;
            return ActiveSession;
        }

        public void Cancel() => ActiveSession = 0;

        /// <summary>
        /// Returns the transcript to submit for a final result, or null when the event is stale, a duplicate,
        /// or empty (<paramref name="empty"/> is then true for the empty case so the UI can say so).
        /// With <paramref name="understands"/>, the recognizer's alternatives are checked in order (see
        /// <see cref="TranscriptSelector"/>); without it the first non-empty alternative is used.
        /// </summary>
        public string TryConsumeResult(SpeechEvent e, out bool empty, System.Func<string, bool> understands = null) =>
            TryConsumeResult(e, out empty, out _, understands);

        public string TryConsumeResult(SpeechEvent e, out bool empty, out TranscriptChoice choice, System.Func<string, bool> understands = null)
        {
            empty = false;
            choice = default;
            if (e.Type != SpeechEventType.Results || e.SessionId == 0 || e.SessionId != ActiveSession) return null;
            ActiveSession = 0; // a session produces at most one submission
            choice = TranscriptSelector.Select(e.Transcripts, understands);
            if (choice.Text == null) { empty = true; return null; }
            return choice.Text;
        }

        /// <summary>True when an error/end event belongs to the current session (and ends it).</summary>
        public bool TryConsumeError(SpeechEvent e)
        {
            if (e.Type != SpeechEventType.Error || e.SessionId != ActiveSession || ActiveSession == 0) return false;
            ActiveSession = 0;
            return true;
        }
    }

    /// <summary>Which recognizer alternative was chosen, and why.</summary>
    public readonly struct TranscriptChoice
    {
        public readonly string Text;
        /// <summary>Index into the recognizer's alternatives, or -1 when none was usable.</summary>
        public readonly int Index;
        /// <summary>True when the classifier understood the chosen alternative; false for the fallback (or when not checked).</summary>
        public readonly bool Understood;

        public TranscriptChoice(string text, int index, bool understood)
        {
            Text = text;
            Index = index;
            Understood = understood;
        }
    }

    /// <summary>
    /// Picks one transcript from the recognizer's alternatives (Android returns them best-first in
    /// "results_recognition"): the first one the question classifier understands, otherwise the first non-empty one.
    /// </summary>
    public static class TranscriptSelector
    {
        public static TranscriptChoice Select(IReadOnlyList<string> alternatives, System.Func<string, bool> understands)
        {
            int fallback = -1;
            for (int i = 0; i < (alternatives?.Count ?? 0); i++)
            {
                var t = alternatives[i]?.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (fallback < 0) fallback = i;
                if (understands == null) break;
                if (understands(t)) return new TranscriptChoice(t, i, true);
            }
            return fallback < 0 ? new TranscriptChoice(null, -1, false) : new TranscriptChoice(alternatives[fallback].Trim(), fallback, false);
        }

        /// <summary>
        /// True when the existing rule-based classifier maps the text to a known intent (any kind except Unknown).
        /// Board-independent, so it can run before the question is answered.
        /// </summary>
        public static bool ClassifierUnderstands(string text)
        {
            string norm = TextAnalysis.Normalize(text);
            return IntentClassifier.Classify(norm, TextAnalysis.Extract(norm, null)).Kind != QueryKind.Unknown;
        }
    }

    /// <summary>Selects what gets spoken: the answer only, never evidence, confidence, caveats or diagnostics.</summary>
    public static class SpokenAnswer
    {
        public const int MaxLength = 600;

        public static string From(AssistantResponse response, string spokenBoardName = "this board")
        {
            if (response == null || string.IsNullOrWhiteSpace(response.Answer)) return null;
            string text = response.Answer;
            if (!string.IsNullOrEmpty(response.BoardName)) text = text.Replace(response.BoardName, spokenBoardName);
            text = Regex.Replace(text, @"[•⚠~]", " ");
            text = Regex.Replace(text, @"\s*\n+\s*", ". ");
            text = Regex.Replace(text, @"\.\s*\.", ".");
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();
            if (text.Length > MaxLength)
            {
                int cut = text.LastIndexOf(". ", MaxLength);
                text = cut > 100 ? text.Substring(0, cut + 1) : text.Substring(0, MaxLength);
            }
            return text;
        }
    }

    /// <summary>Decides whether an answer is still current enough to speak.</summary>
    public static class SpeechPolicy
    {
        /// <summary>Answers to questions asked longer ago than this (e.g. waiting for board recognition) are not spoken.</summary>
        public const float MaxAnswerAgeSeconds = 60f;

        public static bool ShouldSpeak(bool speechEnabled, int answerSequence, int latestSequence, float askedAt, float now) =>
            speechEnabled && answerSequence == latestSequence && now - askedAt <= MaxAnswerAgeSeconds;
    }
}
