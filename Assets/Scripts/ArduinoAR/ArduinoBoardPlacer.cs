using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArduinoAR
{
    /// <summary>
    /// Places the board on a detected horizontal plane where the user taps (AR raycast).
    /// After placement, plane visuals are hidden and taps are ignored until the user enters
    /// reposition mode ("Move board"). A fallback places it in front of the camera without a plane.
    /// </summary>
    public class ArduinoBoardPlacer : MonoBehaviour
    {
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] Camera m_ArCamera;
        [SerializeField] ArduinoBoardView m_Board;

        [Tooltip("Layer the plane prefab renders on. Hiding planes removes it from the camera culling mask; detection and raycasts keep running.")]
        [SerializeField] string m_PlaneLayer = "ARPlanes";
        [Tooltip("Taps closer than this to the camera are ignored, so the board can't land right under the phone and fill the screen.")]
        [SerializeField] float m_MinPlacementDistance = 0.3f;

        public bool IsPlaced { get; private set; }
        public bool PlacedOnPlane { get; private set; }
        /// <summary>True before the first placement, and after the user presses "Move board".</summary>
        public bool IsRepositioning { get; private set; } = true;
        public string LastRejection { get; private set; }
        public event Action Placed;

        static readonly List<ARRaycastHit> s_Hits = new();
        static readonly List<RaycastResult> s_UiHits = new();
        int m_PlaneLayerMask;

        void Awake()
        {
            if (m_RaycastManager == null) m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            if (m_ArCamera == null) m_ArCamera = FindAnyObjectByType<ARCameraManager>()?.GetComponent<Camera>();
            int layer = LayerMask.NameToLayer(m_PlaneLayer);
            m_PlaneLayerMask = layer >= 0 ? 1 << layer : 0;

            // Keep the board out of any scaled hierarchy; its size comes only from ArduinoBoardView.
            m_Board.transform.SetParent(null, false);
            m_Board.transform.localScale = Vector3.one;
            m_Board.Build();
            m_Board.gameObject.SetActive(false);
            SetPlanesVisible(true);
        }

        /// <summary>Enter or leave reposition mode. Planes are shown only while repositioning.</summary>
        public void SetRepositioning(bool on)
        {
            IsRepositioning = on || !IsPlaced;
            LastRejection = null;
            SetPlanesVisible(IsRepositioning);
        }

        public void SetPlanesVisible(bool visible)
        {
            if (m_ArCamera == null || m_PlaneLayerMask == 0) return;
            m_ArCamera.cullingMask = visible ? m_ArCamera.cullingMask | m_PlaneLayerMask : m_ArCamera.cullingMask & ~m_PlaneLayerMask;
        }

        public bool PlanesVisible => m_ArCamera != null && (m_ArCamera.cullingMask & m_PlaneLayerMask) != 0;

        public int TrackedHorizontalPlanes
        {
            get
            {
                int n = 0;
                if (m_PlaneManager == null) return 0;
                foreach (var p in m_PlaneManager.trackables)
                    if (p.trackingState == TrackingState.Tracking && p.alignment == PlaneAlignment.HorizontalUp) n++;
                return n;
            }
        }

        void Update()
        {
            // Taps only place the board while placing/repositioning; otherwise stray taps would move it.
            if (!IsRepositioning) return;
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return;
            Vector2 screenPos = pointer.position.ReadValue();
            if (IsOverUi(screenPos)) return;
            TryPlaceAtScreenPoint(screenPos);
        }

        public bool TryPlaceAtScreenPoint(Vector2 screenPos)
        {
            if (m_RaycastManager == null || !m_RaycastManager.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon))
                return false;
            foreach (var hit in s_Hits)
            {
                if (hit.trackable is ARPlane plane && plane.alignment != PlaneAlignment.HorizontalUp) continue;
                if (Vector3.Distance(hit.pose.position, m_ArCamera.transform.position) < m_MinPlacementDistance)
                {
                    LastRejection = "Too close to the phone: tap a spot further away.";
                    return false;
                }
                PlaceAt(hit.pose.position, onPlane: true);
                return true;
            }
            return false;
        }

        /// <summary>Fallback: put the board 45 cm ahead of the camera, 20 cm lower, lying flat.</summary>
        public void PlaceInFrontOfCamera()
        {
            if (m_ArCamera == null) return;
            var cam = m_ArCamera.transform;
            var flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            PlaceAt(cam.position + flatForward.normalized * 0.45f + Vector3.down * 0.2f, onPlane: false);
        }

        void PlaceAt(Vector3 position, bool onPlane)
        {
            // Lie flat; local +Z (digital header) points away from the viewer so labels read upright.
            var away = Vector3.ProjectOnPlane(position - m_ArCamera.transform.position, Vector3.up);
            var rotation = away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away.normalized, Vector3.up) : Quaternion.identity;
            m_Board.transform.SetPositionAndRotation(position, rotation);
            m_Board.transform.localScale = Vector3.one; // fixed size; placement never scales
            m_Board.gameObject.SetActive(true);
            IsPlaced = true;
            PlacedOnPlane = onPlane;
            SetRepositioning(false);
            Placed?.Invoke();
        }

        static bool IsOverUi(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            s_UiHits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screenPos }, s_UiHits);
            return s_UiHits.Count > 0;
        }
    }
}
