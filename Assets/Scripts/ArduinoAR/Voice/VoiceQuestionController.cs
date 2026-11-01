using UnityEngine;

namespace ArduinoAR
{
    /// <summary>
    /// Optional voice layer for <see cref="ArduinoQueryUI"/>: speech-to-text questions and spoken answers.
    /// Recognised transcripts go through <see cref="ArduinoQueryUI.SubmitTypedQuestion"/> (same path,
    /// duplicate gate and NLP pipeline as typed questions). All decisions live in <see cref="VoiceInteraction"/>.
    /// </summary>
    public class VoiceQuestionController : MonoBehaviour
    {
        [SerializeField] ArduinoQueryUI m_UI;
        [Tooltip("Status messages from voice are shown for this long, unless replaced.")]
        [SerializeField] float m_StatusSeconds = 6f;

        VoiceInteraction m_Voice;

        public VoiceInteraction Interaction => m_Voice;

        /// <summary>Voice message for the status line while relevant (listening, or for a few seconds after an event).</summary>
        public string StatusMessage
        {
            get
            {
                if (m_Voice == null || string.IsNullOrEmpty(m_Voice.Status)) return null;
                if (m_Voice.IsListening || m_Voice.IsAwaitingPermission) return m_Voice.Status;
                return Time.unscaledTime - m_Voice.StatusTime < m_StatusSeconds ? m_Voice.Status : null;
            }
        }

        public string MicButtonLabel => m_Voice != null && (m_Voice.IsListening || m_Voice.IsAwaitingPermission) ? "Stop" : "Mic";
        public string SpeakerButtonLabel => m_Voice == null ? "Voice: n/a" : m_Voice.IsSpeaking ? "Stop voice" : m_Voice.SpeakAnswers ? "Voice: On" : "Voice: Off";

        void Awake()
        {
            if (m_UI == null) m_UI = GetComponent<ArduinoQueryUI>();
        }

        void Start()
        {
            if (m_Voice == null) UseBackends(CreateSpeechToText(), CreateTextToSpeech(), CreatePermission());
            m_UI.QuestionAsked += OnQuestionAsked;
            m_UI.AnswerShown += OnAnswerShown;
        }

        /// <summary>Replace the backends (used by tests with fakes).</summary>
        public void UseBackends(ISpeechToText stt, ITextToSpeech tts, IMicrophonePermission mic)
        {
            m_Voice?.Dispose();
            m_Voice = new VoiceInteraction(stt, tts, mic, q => m_UI.SubmitTypedQuestion(q), TranscriptSelector.ClassifierUnderstands);
        }

        static ISpeechToText CreateSpeechToText()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSpeechToText();
#else
            return new UnavailableSpeechToText();
#endif
        }

        static ITextToSpeech CreateTextToSpeech()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidTextToSpeech();
#else
            return new UnavailableTextToSpeech();
#endif
        }

        static IMicrophonePermission CreatePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidMicrophonePermission();
#else
            return new GrantedMicrophonePermission();
#endif
        }

        public void ToggleListening() => m_Voice?.ToggleListening(Time.unscaledTime);
        public void ToggleSpeech() => m_Voice?.ToggleSpeech();

        void Update() => m_Voice?.Tick(Time.unscaledTime);

        void OnQuestionAsked(bool isPendingReplay) => m_Voice?.OnQuestionAsked(isPendingReplay, Time.unscaledTime);

        void OnAnswerShown(AssistantResponse response) =>
            m_Voice?.OnAnswer(response, Time.unscaledTime, response.BoardId == SpduinoProfile.Id ? "the SPDUINO board" : "the Arduino Uno");

        void OnApplicationPause(bool paused)
        {
            if (paused) m_Voice?.OnPause();
        }

        void OnDestroy()
        {
            if (m_UI != null)
            {
                m_UI.QuestionAsked -= OnQuestionAsked;
                m_UI.AnswerShown -= OnAnswerShown;
            }
            m_Voice?.Dispose();
            m_Voice = null;
        }
    }
}
