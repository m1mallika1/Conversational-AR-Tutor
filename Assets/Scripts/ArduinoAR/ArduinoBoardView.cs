using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ArduinoAR
{
    /// <summary>
    /// Lightweight procedural Arduino Uno: board, headers, one marker + label per profile pin.
    /// Lies flat in local XZ; local +Z is the digital-header (far) edge.
    /// </summary>
    public class ArduinoBoardView : MonoBehaviour
    {
        [Tooltip("Opaque URP Unlit material used as a template for all board parts.")]
        [SerializeField] Material m_BaseMaterial;
        [Tooltip("Scale relative to a real Uno (68.6 x 53.4 mm). 3 = about 20 cm wide.")]
        [SerializeField] float m_Scale = 3f;

        static readonly Color k_BoardColor = new(0.0f, 0.42f, 0.52f);
        static readonly Color k_HeaderColor = new(0.08f, 0.08f, 0.08f);
        static readonly Color k_PinColor = new(0.75f, 0.75f, 0.75f);
        static readonly Color k_HighlightColor = new(1.0f, 0.85f, 0.0f);
        static readonly Color k_LabelColor = Color.white;

        sealed class PinVisual
        {
            public PinRecord Record;
            public Transform Marker;
            public Material Material;
            public Text Label;
        }

        readonly Dictionary<string, PinVisual> m_Pins = new();
        readonly HashSet<string> m_Highlighted = new();
        Font m_Font;
        Transform m_Root;
        bool m_Built;

        public void Build()
        {
            if (m_Built) return;
            m_Built = true;
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Root in millimetres: 1 unit = 1 mm * scale / 1000.
            m_Root = new GameObject("Board (mm)").transform;
            m_Root.SetParent(transform, false);
            m_Root.localScale = Vector3.one * (m_Scale / 1000f);

            float w = ArduinoUnoProfile.BoardWidthMm, h = ArduinoUnoProfile.BoardHeightMm;
            Box("PCB", new Vector3(0, 0.8f, 0), new Vector3(w, 1.6f, h), k_BoardColor);
            Box("USB", ToLocal(new Vector2(3f, 38f)) + Vector3.up * 5.5f, new Vector3(16f, 11f, 12f), new Color(0.7f, 0.7f, 0.72f));
            Box("Power jack", ToLocal(new Vector2(4f, 8f)) + Vector3.up * 5.5f, new Vector3(14f, 11f, 9f), k_HeaderColor);
            Box("ATmega328P", ToLocal(new Vector2(48f, 20f)) + Vector3.up * 2f, new Vector3(35f, 4f, 9f), new Color(0.12f, 0.12f, 0.12f));
            Box("Header top", ToLocal(new Vector2(41.5f, 50.8f)) + Vector3.up * 4f, new Vector3(48f, 8f, 2.6f), k_HeaderColor);
            Box("Header power", ToLocal(new Vector2(35.5f, 2.54f)) + Vector3.up * 4f, new Vector3(19f, 8f, 2.6f), k_HeaderColor);
            Box("Header analog", ToLocal(new Vector2(57.15f, 2.54f)) + Vector3.up * 4f, new Vector3(16f, 8f, 2.6f), k_HeaderColor);

            var canvas = CreateLabelCanvas();
            Label(canvas, "ARDUINO UNO", ToLocal(new Vector2(30f, 27f)), 70, 0f, Color.white, FontStyle.Bold, new Vector2(600, 100));
            Label(canvas, "DIGITAL (PWM ~)", ToLocal(new Vector2(46f, 39f)), 30, 0f, k_LabelColor, FontStyle.Normal, new Vector2(400, 50));
            Label(canvas, "POWER", ToLocal(new Vector2(35.5f, 13f)), 30, 0f, k_LabelColor, FontStyle.Normal, new Vector2(200, 50));
            Label(canvas, "ANALOG IN", ToLocal(new Vector2(57f, 13f)), 30, 0f, k_LabelColor, FontStyle.Normal, new Vector2(250, 50));

            foreach (var record in ArduinoUnoProfile.Pins)
            {
                var marker = Box(record.Id, ToLocal(record.PositionMm) + Vector3.up * 8.6f, new Vector3(1.6f, 1.2f, 1.6f), k_PinColor);
                bool top = record.Header == PinHeader.DigitalTop;
                var labelPos = ToLocal(record.PositionMm + new Vector2(0, top ? -5.5f : 5.5f));
                var label = Label(canvas, record.Label, labelPos, 22, 90f, k_LabelColor, FontStyle.Bold, new Vector2(80, 30));
                m_Pins[record.Id] = new PinVisual { Record = record, Marker = marker, Material = marker.GetComponent<MeshRenderer>().sharedMaterial, Label = label };
            }
        }

        /// <summary>Highlight exactly the given records; everything else returns to normal.</summary>
        /// <param name="roles">Optional wiring role per pin id (see <see cref="PinRoles"/>); role pins get the role colour.</param>
        public void Highlight(IReadOnlyCollection<PinRecord> records, IReadOnlyDictionary<string, string> roles = null)
        {
            if (!m_Built) Build();
            m_Highlighted.Clear();
            m_HighlightColors.Clear();
            if (records != null)
                foreach (var r in records)
                {
                    m_Highlighted.Add(r.Id);
                    m_HighlightColors[r.Id] = roles != null && roles.TryGetValue(r.Id, out var role) && PinRoles.ColorFor(role) is Color c ? c : k_HighlightColor;
                }

            foreach (var pin in m_Pins.Values)
            {
                bool on = m_Highlighted.Contains(pin.Record.Id);
                var color = on ? m_HighlightColors[pin.Record.Id] : k_PinColor;
                pin.Material.SetColor("_BaseColor", color);
                pin.Marker.localScale = on ? new Vector3(2.6f, 6f, 2.6f) : new Vector3(1.6f, 1.2f, 1.6f);
                pin.Marker.localPosition = ToLocal(pin.Record.PositionMm) + Vector3.up * (on ? 11f : 8.6f);
                pin.Label.color = on ? color : k_LabelColor;
                pin.Label.fontSize = on ? 30 : 22;
            }
        }

        readonly Dictionary<string, Color> m_HighlightColors = new();

        void Update()
        {
            if (m_Highlighted.Count == 0) return;
            // Gentle pulse on highlighted pins so they read clearly on the phone.
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);
            foreach (var id in m_Highlighted)
                if (m_Pins.TryGetValue(id, out var pin))
                {
                    var c = m_HighlightColors[id] * pulse; c.a = 1f;
                    pin.Material.SetColor("_BaseColor", c);
                }
        }

        static Vector3 ToLocal(Vector2 mm) =>
            new(mm.x - ArduinoUnoProfile.BoardWidthMm * 0.5f, 0f, mm.y - ArduinoUnoProfile.BoardHeightMm * 0.5f);

        Transform Box(string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(m_Root, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = m_BaseMaterial != null ? new Material(m_BaseMaterial) : mr.material;
            mat.SetColor("_BaseColor", color);
            mr.sharedMaterial = mat;
            return go.transform;
        }

        RectTransform CreateLabelCanvas()
        {
            // World-space canvas lying flat just above the board, in 0.1 mm units for crisp text.
            var go = new GameObject("Labels", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var rt = (RectTransform)go.transform;
            rt.SetParent(m_Root, false);
            rt.localPosition = Vector3.up * 9.5f;
            rt.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rt.localScale = Vector3.one * 0.1f;
            rt.sizeDelta = new Vector2(ArduinoUnoProfile.BoardWidthMm * 10f, ArduinoUnoProfile.BoardHeightMm * 10f);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            return rt;
        }

        Text Label(RectTransform canvas, string text, Vector3 boardLocalMm, int fontSize, float rotation, Color color, FontStyle style, Vector2 size)
        {
            var go = new GameObject("Label " + text, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas, false);
            rt.sizeDelta = size;
            // Canvas units are 0.1 mm; canvas Y maps to board +Z.
            rt.anchoredPosition = new Vector2(boardLocalMm.x * 10f, boardLocalMm.z * 10f);
            rt.localRotation = Quaternion.Euler(0, 0, rotation);
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
