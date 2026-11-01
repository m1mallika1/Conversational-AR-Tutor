using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ArduinoAR
{
    /// <summary>Used where no speech service exists (Editor, non-Android): reports unavailable.</summary>
    public sealed class UnavailableSpeechToText : ISpeechToText
    {
        public bool IsAvailable => false;
        public void StartListening(int sessionId) { }
        public void Cancel() { }
        public bool TryDequeue(out SpeechEvent e) { e = default; return false; }
        public void Dispose() { }
    }

    public sealed class UnavailableTextToSpeech : ITextToSpeech
    {
        public bool IsReady => false;
        public bool IsSpeaking => false;
        public void Speak(string text) { }
        public void Stop() { }
        public void Dispose() { }
    }

    public sealed class GrantedMicrophonePermission : IMicrophonePermission
    {
        public bool HasPermission => true;
        public void Request(Action<MicPermissionResult> done) => done(MicPermissionResult.Granted);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>Android runtime RECORD_AUDIO permission via Unity's Permission API.</summary>
    public sealed class AndroidMicrophonePermission : IMicrophonePermission
    {
        public bool HasPermission => Permission.HasUserAuthorizedPermission(Permission.Microphone);

        public void Request(Action<MicPermissionResult> done)
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => done(MicPermissionResult.Granted);
            callbacks.PermissionDenied += _ => done(MicPermissionResult.Denied);
            callbacks.PermissionDeniedAndDontAskAgain += _ => done(MicPermissionResult.DeniedPermanently);
            callbacks.PermissionRequestDismissed += _ => done(MicPermissionResult.Dismissed);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
        }
    }

    /// <summary>
    /// Android's built-in android.speech.SpeechRecognizer, driven through AndroidJavaProxy (no native plugin).
    /// SpeechRecognizer must be created and called on the Android UI thread; its callbacks arrive there and
    /// are queued for Unity's main thread. Which engine runs (on-device or Google's online service) is
    /// decided by the device's default recognition service.
    /// </summary>
    public sealed class AndroidSpeechToText : ISpeechToText
    {
        readonly ConcurrentQueue<SpeechEvent> m_Events = new();
        readonly AndroidJavaObject m_Activity;
        AndroidJavaObject m_Recognizer;
        RecognitionListenerProxy m_Listener;
        volatile int m_Session;

        public bool IsAvailable { get; }

        public AndroidSpeechToText()
        {
            try
            {
                m_Activity = AndroidApplication.currentActivity;
                using var cls = new AndroidJavaClass("android.speech.SpeechRecognizer");
                IsAvailable = cls.CallStatic<bool>("isRecognitionAvailable", m_Activity);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Voice] SpeechRecognizer availability check failed: " + ex.Message);
                IsAvailable = false;
            }
            Debug.Log($"[Voice] speech recognition available={IsAvailable}");
        }

        public void StartListening(int sessionId)
        {
            m_Session = sessionId;
            AndroidApplication.InvokeOnUIThread(() =>
            {
                try
                {
                    if (m_Recognizer == null)
                    {
                        using var cls = new AndroidJavaClass("android.speech.SpeechRecognizer");
                        m_Recognizer = cls.CallStatic<AndroidJavaObject>("createSpeechRecognizer", m_Activity);
                        m_Listener = new RecognitionListenerProxy(this);
                        m_Recognizer.Call("setRecognitionListener", m_Listener);
                    }
                    using var intent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH");
                    intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form").Dispose();
                    intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PARTIAL_RESULTS", true).Dispose();
                    intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", 3).Dispose();
                    intent.Call<AndroidJavaObject>("putExtra", "calling_package", m_Activity.Call<string>("getPackageName")).Dispose();
                    m_Recognizer.Call("startListening", intent);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Voice] startListening failed: " + ex.Message);
                    Enqueue(new SpeechEvent(SpeechEventType.Error, sessionId, null, SpeechErrors.Client));
                }
            });
        }

        public void Cancel()
        {
            m_Session = 0;
            AndroidApplication.InvokeOnUIThread(() =>
            {
                try { m_Recognizer?.Call("cancel"); }
                catch (Exception ex) { Debug.LogWarning("[Voice] cancel failed: " + ex.Message); }
            });
        }

        public bool TryDequeue(out SpeechEvent e) => m_Events.TryDequeue(out e);

        void Enqueue(SpeechEvent e) => m_Events.Enqueue(e);

        public void Dispose()
        {
            m_Session = 0;
            var recognizer = m_Recognizer;
            m_Recognizer = null;
            if (recognizer == null) return;
            AndroidApplication.InvokeOnUIThread(() =>
            {
                try { recognizer.Call("destroy"); }
                catch (Exception ex) { Debug.LogWarning("[Voice] destroy failed: " + ex.Message); }
                recognizer.Dispose();
            });
        }

        /// <summary>Implements android.speech.RecognitionListener. Runs on the Android UI thread: only enqueue.</summary>
        sealed class RecognitionListenerProxy : AndroidJavaProxy
        {
            readonly AndroidSpeechToText m_Owner;
            public RecognitionListenerProxy(AndroidSpeechToText owner) : base("android.speech.RecognitionListener") { m_Owner = owner; }

            public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] args)
            {
                int session = m_Owner.m_Session;
                switch (methodName)
                {
                    case "onReadyForSpeech": m_Owner.Enqueue(new SpeechEvent(SpeechEventType.ReadyForSpeech, session)); return null;
                    case "onBeginningOfSpeech": m_Owner.Enqueue(new SpeechEvent(SpeechEventType.BeginningOfSpeech, session)); return null;
                    case "onEndOfSpeech": m_Owner.Enqueue(new SpeechEvent(SpeechEventType.EndOfSpeech, session)); return null;
                    case "onError":
                        int code = args != null && args.Length > 0 && args[0] != null ? args[0].Call<int>("intValue") : SpeechErrors.Client;
                        m_Owner.Enqueue(new SpeechEvent(SpeechEventType.Error, session, null, code));
                        return null;
                    case "onResults":
                        m_Owner.Enqueue(new SpeechEvent(SpeechEventType.Results, session, ReadResults(args)));
                        return null;
                    case "onPartialResults":
                        m_Owner.Enqueue(new SpeechEvent(SpeechEventType.PartialResults, session, ReadResults(args)));
                        return null;
                    case "onRmsChanged":
                    case "onBufferReceived":
                    case "onEvent":
                    case "onSegmentResults":
                    case "onEndOfSegmentedSession":
                    case "onLanguageDetection":
                        return null;
                }
                return base.Invoke(methodName, args); // equals/hashCode/toString
            }

            static List<string> ReadResults(AndroidJavaObject[] args)
            {
                var list = new List<string>();
                var bundle = args != null && args.Length > 0 ? args[0] : null;
                if (bundle == null) return list;
                using var results = bundle.Call<AndroidJavaObject>("getStringArrayList", "results_recognition");
                if (results == null) return list;
                int n = results.Call<int>("size");
                for (int i = 0; i < n; i++) list.Add(results.Call<string>("get", i));
                return list;
            }
        }
    }

    /// <summary>Android's built-in android.speech.tts.TextToSpeech via AndroidJavaObject/AndroidJavaProxy.</summary>
    public sealed class AndroidTextToSpeech : ITextToSpeech
    {
        AndroidJavaObject m_Tts;
        volatile bool m_Ready;
        bool m_SpeakingCache;
        float m_NextSpeakingPoll;

        public bool IsReady => m_Ready && m_Tts != null;

        public bool IsSpeaking
        {
            get
            {
                if (!IsReady) return false;
                if (Time.unscaledTime >= m_NextSpeakingPoll) // JNI call at most 4x per second
                {
                    m_NextSpeakingPoll = Time.unscaledTime + 0.25f;
                    try { m_SpeakingCache = m_Tts.Call<bool>("isSpeaking"); } catch { m_SpeakingCache = false; }
                }
                return m_SpeakingCache;
            }
        }

        public AndroidTextToSpeech()
        {
            var activity = AndroidApplication.currentActivity;
            AndroidApplication.InvokeOnUIThread(() =>
            {
                try { m_Tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener(this)); }
                catch (Exception ex) { Debug.LogWarning("[Voice] TextToSpeech init failed: " + ex.Message); }
            });
        }

        public void Speak(string text)
        {
            if (!IsReady || string.IsNullOrEmpty(text)) return;
            try
            {
                using var parameters = new AndroidJavaObject("android.os.Bundle");
                m_Tts.Call<int>("speak", text, 0 /* QUEUE_FLUSH: replaces anything still playing */, parameters, "board-answer");
                m_SpeakingCache = true;
                m_NextSpeakingPoll = Time.unscaledTime + 0.5f;
            }
            catch (Exception ex) { Debug.LogWarning("[Voice] speak failed: " + ex.Message); }
        }

        public void Stop()
        {
            if (m_Tts == null) return;
            try { m_Tts.Call<int>("stop"); } catch (Exception ex) { Debug.LogWarning("[Voice] stop failed: " + ex.Message); }
            m_SpeakingCache = false;
        }

        public void Dispose()
        {
            m_Ready = false;
            if (m_Tts == null) return;
            try { m_Tts.Call("shutdown"); } catch { }
            m_Tts.Dispose();
            m_Tts = null;
        }

        sealed class InitListener : AndroidJavaProxy
        {
            readonly AndroidTextToSpeech m_Owner;
            public InitListener(AndroidTextToSpeech owner) : base("android.speech.tts.TextToSpeech$OnInitListener") { m_Owner = owner; }

            // Dispatch by name instead of reflection so IL2CPP code stripping cannot remove the handler.
            // Called on the Android UI thread; status 0 == TextToSpeech.SUCCESS.
            public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] args)
            {
                if (methodName != "onInit") return base.Invoke(methodName, args);
                int status = args != null && args.Length > 0 && args[0] != null ? args[0].Call<int>("intValue") : -1;
                m_Owner.m_Ready = status == 0;
                Debug.Log($"[Voice] text-to-speech ready={m_Owner.m_Ready} (status {status})");
                return null;
            }
        }
    }
#endif
}
