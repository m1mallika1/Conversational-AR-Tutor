using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ArduinoAR
{
    /// <summary>
    /// Screen-space question interface built at runtime: preset questions, free-text input,
    /// answer panel and status line. Every query goes through the <see cref="BoardAssistant"/> NLP pipeline
    /// and the same structured response drives both the answer text and the board highlighting.
    /// Two separate modes: Physical board (image recognition; highlights only on a recognized,
    /// tracked board) and Virtual demo (the tap-to-place virtual Uno, kept as a fallback/test mode).
    /// </summary>
    public class ArduinoQueryUI : MonoBehaviour
    {
        public enum AppMode { PhysicalBoard, VirtualDemo }

        [SerializeField] ArduinoBoardPlacer m_Placer;
        [SerializeField] ArduinoBoardView m_Board;
        [SerializeField] PhysicalBoardTracker m_Tracker;
        [SerializeField] PhysicalBoardOverlay m_Overlay;
        [SerializeField] AppMode m_StartMode = AppMode.PhysicalBoard;
        [Tooltip("Optional: the temporary AR diagnostics overlay, toggled by the Diag button.")]
        [SerializeField] Behaviour m_Diagnostics;
        [Tooltip("Optional voice input / spoken answers. Typed questions work without it.")]
        [SerializeField] VoiceQuestionController m_Voice;

        static readonly string[] k_Presets =
        {
            "Which pins support PWM?",
            "How do I connect an I2C sensor?",
            "Which pins are analog inputs?",
            "Where are the I2C pins?",
            "Which pins are used for SPI?",
            "Where are power and ground?",
        };

        Font m_Font;
        Text m_Status;
        Text m_Answer;
        Text m_MoveLabel;
        InputField m_Input;
        AssistantResponse m_LastResult;
        Text m_ModeLabel;
        GameObject m_PlaceHereButton, m_MoveButton, m_AlignButton;
        Text m_AlignLabel;
        string m_PendingQuestion;
        readonly SubmissionGate m_SubmitGate = new(1.5f);
        // Conversation memory for follow-ups ("What about pin 5?"); reset when the mode changes.
        readonly DialogueContext m_Dialogue = new();

        Text m_MicLabel, m_SpeakerLabel;

        /// <summary>Structured result of the most recent question (null if none).</summary>
        public AssistantResponse LastResponse => m_LastResult;

        /// <summary>Raised for every question asked; the flag is true when a pending question is replayed after board recognition.</summary>
        public event System.Action<bool> QuestionAsked;
        /// <summary>Raised when an answer is shown (the same response that drives the highlights).</summary>
        public event System.Action<AssistantResponse> AnswerShown;

        public AppMode Mode { get; private set; }

        void Start()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUi();
            m_Placer.Placed += () => { if (Mode == AppMode.VirtualDemo && m_LastResult != null) m_Board.Highlight(m_LastResult.HighlightPins, m_LastResult.PinRoles); };
            if (m_Tracker != null) m_Tracker.StateChanged += OnBoardStateChanged;
            SetMode(m_StartMode);
        }

        public void SetMode(AppMode mode)
        {
            Mode = mode;
            bool physical = mode == AppMode.PhysicalBoard;
            m_LastResult = null;
            m_PendingQuestion = null;
            m_Dialogue.Reset();
            if (m_Voice != null && m_Voice.Interaction != null)
            {
                m_Voice.Interaction.StopSpeaking();
                m_Voice.Interaction.CancelListening("Listening cancelled: mode changed.");
            }
            m_Board.Highlight(null);
            if (m_Overlay != null) { m_Overlay.SetHighlights(null); m_Overlay.enabled = physical; }

            // A detected plane never implies a board: in physical mode tap placement is off and planes are hidden.
            m_Placer.enabled = !physical;
            m_Placer.SetPlanesVisible(!physical && m_Placer.IsRepositioning);
            m_Board.gameObject.SetActive(!physical && m_Placer.IsPlaced);
            m_PlaceHereButton.SetActive(!physical);
            if (m_AlignButton != null) m_AlignButton.SetActive(physical && m_Overlay != null);
            m_MoveButton.SetActive(!physical);
            // The button names the mode it switches TO; the current mode is shown in the answer text below.
            m_ModeLabel.text = physical ? "Switch to Virtual Uno" : "Switch to SPDUINO board";
            m_Answer.text = physical
                ? "Physical board mode: point the camera at your SPDUINO board, then ask a question."
                : "Virtual demo mode: place the virtual Arduino Uno on a plane, then ask a question.";
        }

        void OnBoardStateChanged(BoardRecognitionState state)
        {
            if (Mode == AppMode.PhysicalBoard && state == BoardRecognitionState.Recognized && m_PendingQuestion != null)
            {
                var q = m_PendingQuestion;
                m_PendingQuestion = null;
                AskInternal(q, isPendingReplay: true);
            }
        }

        void Update()
        {
            if (m_Voice != null)
            {
                m_MicLabel.text = m_Voice.MicButtonLabel;
                m_SpeakerLabel.text = m_Voice.SpeakerButtonLabel;
                var voiceStatus = m_Voice.StatusMessage;
                if (!string.IsNullOrEmpty(voiceStatus)) { m_Status.text = voiceStatus; return; }
            }
            if (Mode == AppMode.PhysicalBoard)
            {
                // Alignment diagnostics replace the status line so the mode is unmistakable (and never left on unnoticed).
                if (m_Overlay != null && m_Overlay.DiagnosticMode) m_Status.text = m_Overlay.DiagnosticSummary;
                else m_Status.text = m_Tracker != null ? m_Tracker.StatusText() : "Image tracking is not configured.";
                return;
            }
            m_MoveLabel.text = m_Placer.IsPlaced && m_Placer.IsRepositioning ? "Cancel move" : "Move board";
            if (!string.IsNullOrEmpty(m_Placer.LastRejection))
                m_Status.text = m_Placer.LastRejection;
            else if (m_Placer.IsPlaced && m_Placer.IsRepositioning)
                m_Status.text = "Move mode: tap a cyan surface to move the board.";
            else if (m_Placer.IsPlaced)
                m_Status.text = m_Placer.PlacedOnPlane
                    ? "Board placed. Press \"Move board\" to reposition it."
                    : "Board placed in front of camera. Press \"Move board\" to put it on a plane.";
            else if (m_Placer.TrackedHorizontalPlanes > 0)
                m_Status.text = "Plane detected: tap the cyan surface to place the Arduino Uno.";
            else
                m_Status.text = "Scanning: aim at a textured floor/table and move the phone slowly...";
        }

        /// <summary>
        /// Typed-question entry point (Ask button and keyboard Done/Enter). Processes the question once:
        /// the same text submitted again within 1.5 s (e.g. Done followed by Ask) is ignored.
        /// </summary>
        public void SubmitTypedQuestion(string question)
        {
            if (!m_SubmitGate.TryAccept(question, Time.unscaledTime)) return;
            Ask(question.Trim());
        }

        public void Ask(string question) => AskInternal(question, isPendingReplay: false);

        void AskInternal(string question, bool isPendingReplay)
        {
            if (string.IsNullOrWhiteSpace(question)) return;
            QuestionAsked?.Invoke(isPendingReplay);
            if (Mode == AppMode.PhysicalBoard)
            {
                AskPhysical(question);
                return;
            }
            m_LastResult = BoardAssistant.Process(question, ArduinoUnoProfile.Profile, m_Dialogue);
            m_Answer.text = "Q: " + question + "\n" + m_LastResult.ToDisplayText();
            m_Board.Highlight(m_LastResult.HighlightPins, m_LastResult.PinRoles);
            LogResponse(m_LastResult);
            AnswerShown?.Invoke(m_LastResult);
        }

        static void LogResponse(AssistantResponse r) =>
            Debug.Log($"[ArduinoQuery] ({r.BoardId}) \"{r.Question}\" -> intent={r.IntentId} pins=[{string.Join(",", r.ResolvedPinIds)}] " +
                      $"terms=[{string.Join(",", r.Entities.Terms)}] highlight=[{string.Join(",", r.HighlightIds)}] facts={r.RetrievedFacts.Count} " +
                      $"confidence={r.Confidence:0.00} context={r.UsedContext} roles=[{string.Join(",", r.PinRoles.Select(kv => kv.Key + ":" + kv.Value))}] otherBoard={r.AboutOtherBoard}");

        void AskPhysical(string question)
        {
            // No answer or highlights until a known board is recognized and tracked.
            if (m_Tracker == null || !m_Tracker.HasValidBoard)
            {
                m_PendingQuestion = question;
                m_LastResult = null;
                if (m_Overlay != null) m_Overlay.SetHighlights(null);
                m_Answer.text = "Q: " + question + "\nNo board recognized yet. Point the camera at the SPDUINO board; the answer will appear once it is recognized.";
                return;
            }
            var profile = m_Tracker.Board.Profile;
            m_LastResult = BoardAssistant.Process(question, profile, m_Dialogue);
            m_Answer.text = "Q: " + question + "\n[" + profile.DisplayName + "]\n" + m_LastResult.ToDisplayText();
            if (m_Overlay != null) m_Overlay.SetHighlights(m_LastResult.HighlightPins, m_LastResult.PinRoles);
            LogResponse(m_LastResult);
            AnswerShown?.Invoke(m_LastResult);
        }

        // ---------- UI construction ----------

        void BuildUi()
        {
            var canvasGo = new GameObject("Arduino Query UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            // Status line just below the diagnostics area.
            var statusBg = Panel(root, "Status", new Vector2(0, 0.66f), new Vector2(1, 0.71f), new Color(0, 0, 0, 0.55f));
            m_Status = Label(statusBg, "", 30, TextAnchor.MiddleCenter, Color.white);

            // Bottom panel.
            var panel = Panel(root, "Panel", new Vector2(0, 0), new Vector2(1, 0.46f), new Color(0.05f, 0.08f, 0.12f, 0.82f));

            var answerBg = Panel(panel, "Answer", new Vector2(0.02f, 0.53f), new Vector2(0.98f, 0.98f), new Color(1, 1, 1, 0.08f));
            m_Answer = Label(answerBg, "", 30, TextAnchor.UpperLeft, Color.white);
            m_Answer.resizeTextForBestFit = true;
            m_Answer.resizeTextMinSize = 16;
            m_Answer.resizeTextMaxSize = 32;

            // Preset questions: 2 columns x 3 rows.
            for (int i = 0; i < k_Presets.Length; i++)
            {
                int col = i % 2, row = i / 2;
                float x0 = 0.02f + col * 0.49f, y1 = 0.51f - row * 0.11f;
                string q = k_Presets[i];
                Button(panel, q, new Vector2(x0, y1 - 0.10f), new Vector2(x0 + 0.47f, y1), i < 2 ? new Color(0.0f, 0.45f, 0.65f) : new Color(0.15f, 0.3f, 0.4f), () => Ask(q));
            }

            // Free-text question.
            m_Input = InputBox(panel, new Vector2(0.02f, 0.06f), new Vector2(0.46f, 0.17f));
            m_MicLabel = Button(panel, "Mic", new Vector2(0.48f, 0.06f), new Vector2(0.62f, 0.17f), new Color(0.55f, 0.15f, 0.15f),
                () => { if (m_Voice != null) m_Voice.ToggleListening(); else m_Answer.text = "Voice input is not set up; type your question."; });
            Button(panel, "Ask", new Vector2(0.64f, 0.06f), new Vector2(0.78f, 0.17f), new Color(0.1f, 0.55f, 0.25f), () => SubmitTypedQuestion(m_Input.text));
            m_PlaceHereButton = Button(panel, "Place here", new Vector2(0.80f, 0.06f), new Vector2(0.98f, 0.17f), new Color(0.4f, 0.3f, 0.1f), () => m_Placer.PlaceInFrontOfCamera()).transform.parent.gameObject;

            Button(panel, "Clear", new Vector2(0.02f, 0.0f), new Vector2(0.15f, 0.05f), new Color(0.3f, 0.3f, 0.3f), () =>
            {
                m_LastResult = null;
                m_PendingQuestion = null;
                m_Board.Highlight(null);
                if (m_Overlay != null) m_Overlay.SetHighlights(null);
                m_Answer.text = "Highlights cleared.";
            }, 22);
            if (m_Diagnostics != null)
                Button(panel, "Diag", new Vector2(0.17f, 0.0f), new Vector2(0.30f, 0.05f), new Color(0.3f, 0.3f, 0.3f),
                    () => m_Diagnostics.enabled = !m_Diagnostics.enabled, 22);
            m_SpeakerLabel = Button(panel, "Voice: On", new Vector2(0.32f, 0.0f), new Vector2(0.45f, 0.05f), new Color(0.35f, 0.2f, 0.45f),
                () => { if (m_Voice != null) m_Voice.ToggleSpeech(); }, 22);
            m_MoveLabel = Button(panel, "Move board", new Vector2(0.70f, 0.0f), new Vector2(0.98f, 0.05f), new Color(0.45f, 0.25f, 0.55f),
                () => m_Placer.SetRepositioning(!m_Placer.IsRepositioning), 22);
            m_MoveButton = m_MoveLabel.transform.parent.gameObject;
            // Alignment diagnostics toggle (physical mode only; off by default).
            if (m_Overlay != null)
            {
                m_AlignLabel = Button(panel, "Align: Off", new Vector2(0.47f, 0.0f), new Vector2(0.68f, 0.05f), new Color(0.3f, 0.3f, 0.3f), () =>
                {
                    m_Overlay.DiagnosticMode = !m_Overlay.DiagnosticMode;
                    m_AlignLabel.text = m_Overlay.DiagnosticMode ? "Align: ON" : "Align: Off";
                }, 22);
                m_AlignButton = m_AlignLabel.transform.parent.gameObject;
            }

            // Mode switch: a large button just above the panel (screen 47-52%), clear of the system navigation
            // area at the bottom edge and of the status line (66-71%) and diagnostics text above it.
            // Its label names the action (see SetMode).
            m_ModeLabel = Button(root, "Switch mode", new Vector2(0.18f, 0.47f), new Vector2(0.82f, 0.52f), new Color(0.75f, 0.42f, 0.08f, 0.92f),
                () => SetMode(Mode == AppMode.PhysicalBoard ? AppMode.VirtualDemo : AppMode.PhysicalBoard), 34);
        }

        RectTransform Panel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            return rt;
        }

        Text Label(RectTransform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(16, 8);
            rt.offsetMax = new Vector2(-16, -8);
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        Text Button(RectTransform parent, string text, Vector2 anchorMin, Vector2 anchorMax, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 28)
        {
            var rt = Panel(parent, "Button " + text, anchorMin, anchorMax, color);
            rt.offsetMin = new Vector2(4, 4);
            rt.offsetMax = new Vector2(-4, -4);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            button.onClick.AddListener(onClick);
            var label = Label(rt, text, fontSize, TextAnchor.MiddleCenter, Color.white);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = fontSize;
            return label;
        }

        InputField InputBox(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rt = Panel(parent, "Question Input", anchorMin, anchorMax, new Color(1, 1, 1, 0.92f));
            rt.offsetMin = new Vector2(4, 4);
            rt.offsetMax = new Vector2(-4, -4);
            var text = Label(rt, "", 28, TextAnchor.MiddleLeft, Color.black);
            text.supportRichText = false;
            var placeholder = Label(rt, "Type a question, e.g. \"What does pin 13 do?\"", 24, TextAnchor.MiddleLeft, new Color(0.4f, 0.4f, 0.4f));
            placeholder.fontStyle = FontStyle.Italic;
            var input = rt.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            // onSubmit fires on keyboard Done/Enter only, not when focus moves to the Ask button
            // (onEndEdit did, which answered typed questions twice).
            input.onSubmit.AddListener(SubmitTypedQuestion);
            return input;
        }
    }
}
