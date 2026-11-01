using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArduinoAR
{
    public enum BoardRecognitionState { Searching, Recognized, TrackingLost }

    /// <summary>Snapshot of the recognized physical board.</summary>
    public struct RecognizedBoard
    {
        public string ReferenceName;
        public BoardProfile Profile;          // null if the image isn't mapped to a profile
        public TrackableId TrackableId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector2 SizeMeters;
        public TrackingState TrackingState;
        public float LastUpdateTime;
    }

    /// <summary>
    /// Physical development-board recognition via ARTrackedImageManager.
    /// Independent of plane detection and of the virtual-board placement mode.
    /// </summary>
    public class PhysicalBoardTracker : MonoBehaviour
    {
        [SerializeField] ARTrackedImageManager m_ImageManager;

        public BoardRecognitionState State { get; private set; } = BoardRecognitionState.Searching;
        public RecognizedBoard Board { get; private set; }
        public ARTrackedImage TrackedImage { get; private set; }

        /// <summary>True only when a known board is recognized and currently tracked (pose is live).</summary>
        public bool HasValidBoard => State == BoardRecognitionState.Recognized && Board.Profile != null && TrackedImage != null;

        public event Action<BoardRecognitionState> StateChanged;

        void Awake()
        {
            if (m_ImageManager == null) m_ImageManager = FindAnyObjectByType<ARTrackedImageManager>();
        }

        void OnEnable()
        {
            if (m_ImageManager != null) m_ImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }

        void OnDisable()
        {
            if (m_ImageManager != null) m_ImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        // Whether the board has been actively tracked since start/resume. Before that, a None/Limited
        // report is still "Searching" (ARCore can add an image as None one frame before Tracking).
        bool m_EverRecognized;
        readonly Dictionary<TrackableId, TrackingState> m_LastImageState = new();

        void OnApplicationPause(bool paused)
        {
            // After a pause the old pose may be stale and the image may never report again; start a
            // fresh search. If ARCore still tracks the image, the next Tracking update re-recognizes it.
            if (!paused) ResetToSearching("app resumed");
        }

        public void ResetToSearching(string reason)
        {
            TrackedImage = null;
            m_EverRecognized = false;
            m_LastImageState.Clear();
            Debug.Log($"[BoardTrack] reset to Searching ({reason})");
            SetState(BoardRecognitionState.Searching);
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (var image in args.added) { LogImageState(image, "added"); Consider(image); }
            foreach (var image in args.updated) { LogImageState(image, "updated"); Consider(image); }
            foreach (var removed in args.removed)
            {
                m_LastImageState.Remove(removed.Key);
                Debug.Log($"[BoardTrack] image removed id={removed.Key}");
                if (TrackedImage != null && removed.Key == TrackedImage.trackableId)
                {
                    TrackedImage = null;
                    SetState(m_EverRecognized ? BoardRecognitionState.TrackingLost : BoardRecognitionState.Searching);
                }
            }
        }

        void LogImageState(ARTrackedImage image, string change)
        {
            bool known = m_LastImageState.TryGetValue(image.trackableId, out var previous);
            if (known && previous == image.trackingState) return; // only log transitions
            m_LastImageState[image.trackableId] = image.trackingState;
            Debug.Log($"[BoardTrack] image {change} name={image.referenceImage.name} id={image.trackableId} {(known ? previous.ToString() : "-")} -> {image.trackingState}");
        }

        void Consider(ARTrackedImage image)
        {
            // Only follow known boards; keep following the current one while it is actively tracked.
            var profile = BoardProfileRegistry.ForReferenceImage(image.referenceImage.name);
            if (profile == null) return;
            if (TrackedImage != null && TrackedImage.trackableId != image.trackableId && State == BoardRecognitionState.Recognized) return;

            TrackedImage = image;
            Board = new RecognizedBoard
            {
                ReferenceName = image.referenceImage.name,
                Profile = profile,
                TrackableId = image.trackableId,
                Position = image.transform.position,
                Rotation = image.transform.rotation,
                SizeMeters = image.size,
                TrackingState = image.trackingState,
                LastUpdateTime = Time.time,
            };
            // ARCore reports Limited when the image is out of view (pose is a stale estimate).
            if (image.trackingState == TrackingState.Tracking)
            {
                m_EverRecognized = true;
                SetState(BoardRecognitionState.Recognized);
            }
            else
            {
                SetState(m_EverRecognized ? BoardRecognitionState.TrackingLost : BoardRecognitionState.Searching);
            }
        }

        void SetState(BoardRecognitionState state)
        {
            if (state == State) return;
            State = state;
            var b = Board;
            Debug.Log($"[BoardTrack] {state} name={b.ReferenceName} profile={b.Profile?.Id} tracking={b.TrackingState} pos={b.Position:F3} rot={b.Rotation.eulerAngles:F0} size={b.SizeMeters:F3}");
            StateChanged?.Invoke(state);
        }

        public string StatusText()
        {
            var b = Board;
            return State switch
            {
                BoardRecognitionState.Searching => "Searching for a known board: point the camera straight at the SPDUINO board.",
                BoardRecognitionState.Recognized => $"Recognized: {b.ReferenceName} (tracking). Ask a question to highlight pins.",
                _ => $"Tracking lost ({b.TrackingState}): point the camera back at the board.",
            };
        }
    }
}
