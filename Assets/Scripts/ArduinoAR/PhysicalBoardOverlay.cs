using System.Collections.Generic;
using UnityEngine;

namespace ArduinoAR
{
    /// <summary>
    /// Lightweight overlay on the recognized physical board: an outline plus small markers on the
    /// highlighted pins. No full virtual board is drawn. Visible only while the tracker reports a
    /// known board with live tracking; pin positions come from the board's profile.
    /// </summary>
    public class PhysicalBoardOverlay : MonoBehaviour
    {
        [SerializeField] PhysicalBoardTracker m_Tracker;
        [Tooltip("Opaque URP Unlit material used as a template for overlay parts.")]
        [SerializeField] Material m_BaseMaterial;

        static readonly Color k_OutlineColor = new(0.1f, 1f, 0.4f);
        static readonly Color k_HighlightColor = new(1f, 0.85f, 0f);
        const float k_MarkerSizeM = 0.0022f, k_MarkerHeightM = 0.006f;

        // Alignment diagnostics (off by default): orange outline, a grey marker on EVERY pin (same size and
        // height as normal markers), white corner markers, and a once-per-second [AlignDiag] log line with the
        // pose, tilt, distance and the screen positions of the corners and pins.
        static readonly Color k_DiagOutlineColor = new(1f, 0.45f, 0f);
        static readonly Color k_DiagPinColor = new(0.75f, 0.75f, 0.75f);
        static readonly Color k_DiagCornerColor = Color.white;
        [Tooltip("Alignment diagnostics; off for normal use and demos.")]
        [SerializeField] bool m_DiagnosticMode;
        Material m_OutlineMaterial, m_DiagOutlineMaterial, m_DiagPinMaterial, m_DiagCornerMaterial;
        Camera m_Camera;
        float m_NextDiagLog;

        public bool DiagnosticMode
        {
            get => m_DiagnosticMode;
            set
            {
                if (m_DiagnosticMode == value) return;
                m_DiagnosticMode = value;
                m_BuiltFor = null; // rebuild with or without the diagnostic markers
                Debug.Log($"[AlignDiag] diagnostic mode {(value ? "ON" : "OFF")}");
            }
        }

        /// <summary>Short live readout for the status line while diagnostics are on.</summary>
        public string DiagnosticSummary { get; private set; } = "";

        readonly List<PinRecord> m_Highlighted = new();
        readonly List<Transform> m_Markers = new();
        readonly Dictionary<string, string> m_Roles = new();
        // One pulsing material per colour: the default highlight plus one per wiring role in use.
        readonly Dictionary<string, (Material material, Color color)> m_RoleMaterials = new();
        Transform m_Root;
        LineRenderer m_Outline;
        Material m_MarkerMaterial;
        Transform m_AttachedTo;
        BoardProfile m_BuiltFor;

        /// <param name="roles">Optional wiring role per pin id (see <see cref="PinRoles"/>); role pins get the role colour.</param>
        public void SetHighlights(IReadOnlyList<PinRecord> records, IReadOnlyDictionary<string, string> roles = null)
        {
            m_Highlighted.Clear();
            if (records != null) m_Highlighted.AddRange(records);
            m_Roles.Clear();
            if (roles != null) foreach (var kv in roles) m_Roles[kv.Key] = kv.Value;
            m_BuiltFor = null; // rebuild markers on next update
        }

        Material MaterialFor(string pinId)
        {
            if (!m_Roles.TryGetValue(pinId, out var role) || PinRoles.ColorFor(role) is not Color color) return m_MarkerMaterial;
            if (!m_RoleMaterials.TryGetValue(role, out var entry)) m_RoleMaterials[role] = entry = (Tinted(color), color);
            return entry.material;
        }

        void Awake()
        {
            if (m_Tracker == null) m_Tracker = FindAnyObjectByType<PhysicalBoardTracker>();
            m_Root = new GameObject("Physical Board Overlay").transform;
            m_Outline = m_Root.gameObject.AddComponent<LineRenderer>();
            m_Outline.useWorldSpace = false;
            m_Outline.loop = true;
            m_Outline.widthMultiplier = 0.0015f;
            m_Outline.positionCount = 4;
            m_OutlineMaterial = Tinted(k_OutlineColor);
            m_Outline.sharedMaterial = m_OutlineMaterial;
            m_MarkerMaterial = Tinted(k_HighlightColor);
            m_Root.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (m_Root != null) m_Root.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            bool show = m_Tracker != null && m_Tracker.HasValidBoard;
            m_Root.gameObject.SetActive(show);
            if (m_DiagnosticMode) UpdateDiagnostics(show);
            if (!show) return;

            // Copy the tracked image pose (not parented, so removal of the image can't destroy the overlay).
            // The image's local XZ plane is the image: +X = image right, +Z = image top, +Y = out of the image.
            var image = m_Tracker.TrackedImage;
            m_Root.SetPositionAndRotation(image.transform.position, image.transform.rotation);
            if (m_AttachedTo != image.transform)
            {
                m_AttachedTo = image.transform;
                m_BuiltFor = null;
            }

            var profile = m_Tracker.Board.Profile;
            if (m_BuiltFor != profile) Rebuild(profile, image.size);

            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f);
            var c = k_HighlightColor * pulse; c.a = 1f;
            m_MarkerMaterial.SetColor("_BaseColor", c);
            foreach (var (material, color) in m_RoleMaterials.Values)
            {
                var rc = color * pulse; rc.a = 1f;
                material.SetColor("_BaseColor", rc);
            }
        }

        void Rebuild(BoardProfile profile, Vector2 size)
        {
            m_BuiltFor = profile;
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            m_Outline.SetPositions(new[] { new Vector3(-hx, 0.001f, -hz), new Vector3(-hx, 0.001f, hz), new Vector3(hx, 0.001f, hz), new Vector3(hx, 0.001f, -hz) });
            m_Outline.sharedMaterial = m_DiagnosticMode ? (m_DiagOutlineMaterial ??= Tinted(k_DiagOutlineColor)) : m_OutlineMaterial;

            foreach (var m in m_Markers) Destroy(m.gameObject);
            m_Markers.Clear();
            var drawn = new HashSet<string>();
            foreach (var record in m_Highlighted)
            {
                // Only pins that exist on this board's profile can be drawn on it.
                var pin = FindPin(profile, record.Id);
                if (pin == null) continue;
                AddMarker(pin, profile, size, MaterialFor(pin.Id));
                drawn.Add(pin.Id);
            }
            if (!m_DiagnosticMode) return;

            // Diagnostics: every other pin in grey (same marker geometry), plus a marker on each PCB corner.
            m_DiagPinMaterial ??= Tinted(k_DiagPinColor);
            m_DiagCornerMaterial ??= Tinted(k_DiagCornerColor);
            foreach (var pin in profile.Pins)
                if (!drawn.Contains(pin.Id)) AddMarker(pin, profile, size, m_DiagPinMaterial);
            var corners = BoardGeometry.CornersLocal(size);
            for (int i = 0; i < corners.Length; i++)
            {
                var go = Primitive("Corner " + BoardGeometry.CornerNames[i], m_DiagCornerMaterial);
                go.transform.localPosition = corners[i] + Vector3.up * 0.0005f;
                go.transform.localScale = new Vector3(0.0015f, 0.001f, 0.0015f);
            }
        }

        void AddMarker(PinRecord pin, BoardProfile profile, Vector2 size, Material material)
        {
            var go = Primitive("Pin " + pin.Id, material);
            go.transform.localPosition = BoardGeometry.PinLocal(pin, profile, size, k_MarkerHeightM * 0.5f);
            go.transform.localScale = new Vector3(k_MarkerSizeM, k_MarkerHeightM, k_MarkerSizeM);
        }

        GameObject Primitive(string name, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(m_Root, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Markers.Add(go.transform);
            return go;
        }

        /// <summary>
        /// Once per second: tracking state, image pose, viewing tilt (angle between the camera's view direction
        /// and the board's normal) and distance, plus screen pixels (origin bottom-left, as Unity reports them)
        /// of the PCB corners and of each pin at the board plane (b) and at the top of its marker (t).
        /// </summary>
        void UpdateDiagnostics(bool tracked)
        {
            m_Camera ??= FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARCameraManager>()?.GetComponent<Camera>();
            var image = tracked ? m_Tracker.TrackedImage : null;
            if (image == null || m_Camera == null)
            {
                DiagnosticSummary = "ALIGNMENT DIAGNOSTIC: no tracked board";
                if (Time.unscaledTime >= m_NextDiagLog)
                {
                    m_NextDiagLog = Time.unscaledTime + 1f;
                    Debug.Log($"[AlignDiag] state={(m_Tracker != null ? m_Tracker.State.ToString() : "none")} no tracked board");
                }
                return;
            }
            var t = image.transform;
            var toCamera = m_Camera.transform.position - t.position;
            float distance = toCamera.magnitude;
            float tilt = Vector3.Angle(t.up, toCamera);
            DiagnosticSummary = $"ALIGNMENT DIAGNOSTIC: tilt {tilt:0}°  distance {distance * 100f:0} cm  {image.trackingState}";
            if (Time.unscaledTime < m_NextDiagLog) return;
            m_NextDiagLog = Time.unscaledTime + 1f;

            string Px(Vector3 local)
            {
                var s = m_Camera.WorldToScreenPoint(t.TransformPoint(local));
                return s.z <= 0f ? "behind" : $"{s.x:0},{s.y:0}";
            }
            var profile = m_Tracker.Board.Profile;
            var sb = new System.Text.StringBuilder();
            sb.Append($"[AlignDiag] state={image.trackingState} tilt={tilt:0.0} dist={distance:0.000} pos={t.position:F3} rot={t.rotation.eulerAngles:F1} size={image.size:F3} screen={Screen.width}x{Screen.height} corners=");
            var corners = BoardGeometry.CornersLocal(image.size);
            for (int i = 0; i < corners.Length; i++) sb.Append(i == 0 ? "" : ";").Append(BoardGeometry.CornerNames[i]).Append(':').Append(Px(corners[i]));
            sb.Append(" pins=");
            bool first = true;
            foreach (var pin in profile.Pins)
            {
                sb.Append(first ? "" : ";").Append(pin.Id).Append(":b").Append(Px(BoardGeometry.PinLocal(pin, profile, image.size, 0f)))
                  .Append(":t").Append(Px(BoardGeometry.PinLocal(pin, profile, image.size, k_MarkerHeightM)));
                first = false;
            }
            Debug.Log(sb.ToString());
        }

        static PinRecord FindPin(BoardProfile profile, string id)
        {
            foreach (var p in profile.Pins) if (p.Id == id) return p;
            return null;
        }

        Material Tinted(Color color)
        {
            var mat = m_BaseMaterial != null ? new Material(m_BaseMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        /// <summary>Number of pin markers currently built (for tests/diagnostics).</summary>
        public int MarkerCount => m_Markers.Count;
        public bool IsVisible => m_Root != null && m_Root.gameObject.activeSelf;
    }
}
