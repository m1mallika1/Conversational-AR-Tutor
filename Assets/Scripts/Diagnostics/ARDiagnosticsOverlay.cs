using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// TEMPORARY on-device diagnostics for AR plane detection.
/// Draws AR session, camera tracking and plane state on screen and writes a
/// one-line summary to logcat (tag "[ARDiag]") once per second.
/// Remove this component (and its GameObject) once plane detection is confirmed.
/// </summary>
public class ARDiagnosticsOverlay : MonoBehaviour
{
    [SerializeField] ARPlaneManager m_PlaneManager;
    [SerializeField] ARCameraManager m_CameraManager;

    [Tooltip("Material used to draw a small test quad in front of the camera, to verify the plane material renders.")]
    [SerializeField] Material m_TestMaterial;

    [SerializeField] bool m_SpawnMaterialTestQuad = true;

    readonly StringBuilder m_Text = new();
    readonly HashSet<TrackableId> m_SeenPlaneIds = new();
    int m_FramesReceived;
    float m_NextLogTime;
    GameObject m_TestQuad;
    GUIStyle m_Style;

    void OnEnable()
    {
        if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
        if (m_CameraManager == null) m_CameraManager = FindAnyObjectByType<ARCameraManager>();
        if (m_CameraManager != null) m_CameraManager.frameReceived += OnFrameReceived;
    }

    void OnDisable()
    {
        if (m_CameraManager != null) m_CameraManager.frameReceived -= OnFrameReceived;
    }

    void OnFrameReceived(ARCameraFrameEventArgs args) => m_FramesReceived++;

    void Update()
    {
        BuildReport();

        if (m_SpawnMaterialTestQuad && m_TestQuad == null && m_TestMaterial != null &&
            ARSession.state == ARSessionState.SessionTracking && m_CameraManager != null)
            SpawnTestQuad();

        if (Time.unscaledTime >= m_NextLogTime)
        {
            m_NextLogTime = Time.unscaledTime + 1f;
            Debug.Log("[ARDiag] " + m_Text.ToString().Replace('\n', '|'));
        }
    }

    void SpawnTestQuad()
    {
        // A 20 cm quad 0.6 m in front of the camera, using the plane material.
        // If this is visible but planes are not, the material/URP setup is fine and the issue is detection.
        var cam = m_CameraManager.transform;
        m_TestQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        m_TestQuad.name = "ARDiag Material Test Quad";
        Destroy(m_TestQuad.GetComponent<Collider>());
        m_TestQuad.transform.SetPositionAndRotation(cam.position + cam.forward * 0.6f, Quaternion.LookRotation(cam.forward));
        m_TestQuad.transform.localScale = Vector3.one * 0.2f;
        m_TestQuad.GetComponent<MeshRenderer>().sharedMaterial = m_TestMaterial;
    }

    void BuildReport()
    {
        m_Text.Clear();
        m_Text.Append("Session: ").Append(ARSession.state);
        if (ARSession.notTrackingReason != NotTrackingReason.None)
            m_Text.Append("  reason: ").Append(ARSession.notTrackingReason);
        m_Text.Append('\n');

        if (m_CameraManager == null)
        {
            m_Text.Append("ERROR: no ARCameraManager in scene\n");
        }
        else
        {
            var cam = m_CameraManager.transform;
            m_Text.Append("Camera frames: ").Append(m_FramesReceived)
                .Append("  pos: ").Append(cam.position.ToString("F2"))
                .Append("  fwd: ").Append(cam.forward.ToString("F2")).Append('\n');
        }

        if (m_PlaneManager == null)
        {
            m_Text.Append("ERROR: no ARPlaneManager in scene\n");
            return;
        }

        var subsystem = m_PlaneManager.subsystem;
        m_Text.Append("PlaneManager enabled: ").Append(m_PlaneManager.enabled)
            .Append("  subsystem: ").Append(subsystem == null ? "NULL" : subsystem.running ? "running" : "stopped")
            .Append("  requested: ").Append(m_PlaneManager.requestedDetectionMode)
            .Append("  current: ").Append(m_PlaneManager.currentDetectionMode).Append('\n');
        m_Text.Append("Plane prefab: ").Append(m_PlaneManager.planePrefab == null ? "NULL" : m_PlaneManager.planePrefab.name).Append('\n');

        int count = 0, visible = 0;
        var details = new StringBuilder();
        foreach (var plane in m_PlaneManager.trackables)
        {
            count++;
            m_SeenPlaneIds.Add(plane.trackableId);
            var mr = plane.GetComponent<MeshRenderer>();
            var mf = plane.GetComponent<MeshFilter>();
            bool rendered = mr != null && mr.enabled && plane.gameObject.activeInHierarchy;
            if (rendered) visible++;
            if (count <= 4)
            {
                int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1;
                details.Append("  #").Append(count).Append(' ').Append(plane.alignment)
                    .Append(' ').Append(plane.trackingState)
                    .Append(" size ").Append(plane.size.ToString("F2"))
                    .Append(" at ").Append(plane.center.ToString("F2"))
                    .Append(" renderer ").Append(rendered ? "on" : "OFF")
                    .Append(" verts ").Append(verts)
                    .Append(" layer ").Append(LayerMask.LayerToName(plane.gameObject.layer)).Append('\n');
            }
        }
        m_Text.Append("Planes now: ").Append(count).Append("  rendered: ").Append(visible)
            .Append("  ever seen: ").Append(m_SeenPlaneIds.Count).Append('\n');
        m_Text.Append(details);

        if (m_TestQuad != null) m_Text.Append("Material test quad spawned (cyan square ahead of start pose)\n");

        if (ARSession.state == ARSessionState.Unsupported)
            m_Text.Append("ERROR: AR not supported/available on this device\n");
        else if (ARSession.state == ARSessionState.NeedsInstall || ARSession.state == ARSessionState.Installing)
            m_Text.Append("Waiting for Google Play Services for AR install\n");
        else if (ARSession.state == ARSessionState.SessionTracking && count == 0)
            m_Text.Append("Tracking OK, no planes yet: aim at a textured floor/table, move slowly\n");
    }

    void OnGUI()
    {
        if (m_Style == null)
        {
            m_Style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(16, Screen.height / 100), wordWrap = true };
            m_Style.normal.textColor = Color.yellow;
        }
        var rect = new Rect(20, 60, Screen.width - 40, Screen.height * 0.24f);
        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20), m_Text.ToString(), m_Style);
    }
}
