using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace ArduinoAR.EditorTools
{
    /// <summary>
    /// Adds what Android voice features need to the generated unityLibrary manifest:
    /// - RECORD_AUDIO permission (requested at runtime by the app when the user taps Mic);
    /// - &lt;queries&gt; for the speech-recognition and text-to-speech services, without which Android 11+
    ///   (API 30+) hides those services and SpeechRecognizer reports "unavailable".
    /// Idempotent; does nothing for other platforms.
    /// </summary>
    public class AndroidVoiceManifest : IPostGenerateGradleAndroidProject
    {
        const string k_AndroidNs = "http://schemas.android.com/apk/res/android";
        public static readonly string[] QueryActions = { "android.speech.RecognitionService", "android.intent.action.TTS_SERVICE" };
        public const string RecordAudio = "android.permission.RECORD_AUDIO";

        public int callbackOrder => 10;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("[Voice] AndroidManifest.xml not found at " + manifestPath);
                return;
            }
            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.Load(manifestPath);
            if (Patch(doc))
            {
                doc.Save(manifestPath);
                Debug.Log("[Voice] Added RECORD_AUDIO and speech/TTS <queries> to " + manifestPath);
            }
        }

        /// <summary>Adds the permission and queries if missing. Returns true when the document changed.</summary>
        public static bool Patch(XmlDocument doc)
        {
            var manifest = doc.DocumentElement;
            if (manifest == null || manifest.Name != "manifest") return false;
            bool changed = false;

            bool hasPermission = false;
            foreach (XmlElement e in manifest.GetElementsByTagName("uses-permission"))
                if (e.GetAttribute("name", k_AndroidNs) == RecordAudio) hasPermission = true;
            if (!hasPermission)
            {
                var perm = doc.CreateElement("uses-permission");
                perm.SetAttributeNode(AndroidAttr(doc, RecordAudio));
                manifest.PrependChild(perm);
                changed = true;
            }

            XmlElement queries = null;
            foreach (XmlNode n in manifest.ChildNodes)
                if (n is XmlElement el && el.Name == "queries") queries = el;
            if (queries == null)
            {
                queries = doc.CreateElement("queries");
                manifest.AppendChild(queries);
            }
            foreach (var action in QueryActions)
            {
                bool present = false;
                foreach (XmlElement a in queries.GetElementsByTagName("action"))
                    if (a.GetAttribute("name", k_AndroidNs) == action) present = true;
                if (present) continue;
                var intent = doc.CreateElement("intent");
                var actionEl = doc.CreateElement("action");
                actionEl.SetAttributeNode(AndroidAttr(doc, action));
                intent.AppendChild(actionEl);
                queries.AppendChild(intent);
                changed = true;
            }
            return changed;
        }

        static XmlAttribute AndroidAttr(XmlDocument doc, string value)
        {
            var attr = doc.CreateAttribute("android", "name", k_AndroidNs);
            attr.Value = value;
            return attr;
        }
    }
}
