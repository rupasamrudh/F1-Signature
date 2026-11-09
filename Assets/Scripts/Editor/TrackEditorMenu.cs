// TrackEditorMenu.cs — Unity Editor menu Tools/F1 Signature/Load Track.
using UnityEditor;
using UnityEngine;
using F1Sim.Track;

namespace F1Sim.Editor
{
    public static class TrackEditorMenu
    {
        [MenuItem("Tools/F1 Signature/Load Track")]
        public static void LoadTrackMenu()
        {
            var config = TrackDataLoader.LoadTrack();
            if (config == null)
            {
                EditorUtility.DisplayDialog("Load Track Error", "Failed to load Assets/Data/Tracks/f1sig_track.json", "OK");
                return;
            }

            // Find or create TrackVisualizer object in current scene
            var visualizer = Object.FindAnyObjectByType<TrackGizmoVisualizer>();
            if (visualizer == null)
            {
                var go = new GameObject("TrackVisualizer");
                visualizer = go.AddComponent<TrackGizmoVisualizer>();
                Undo.RegisterCreatedObjectUndo(go, "Create TrackVisualizer");
            }

            visualizer.trackConfig = config;
            EditorUtility.SetDirty(visualizer);
            SceneView.RepaintAll();

            Debug.Log($"[TrackEditorMenu] Successfully loaded track '{config.name}' ({config.samples.Count} samples, {config.total_length_m} m, {config.corners.Count} corners).");
        }
    }
}

