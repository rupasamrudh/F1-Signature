// TrackPlaygroundSceneBuilder.cs — Editor script to build Assets/Scenes/Track_F1Signature.unity and update Build Settings.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using F1Sim.Track;

namespace F1Sim.Editor
{
    public static class TrackPlaygroundSceneBuilder
    {
        [MenuItem("Tools/F1 Signature/Setup Track Scene")]
        public static void SetupTrackSceneMenu()
        {
            // 1. First build the track meshes & scenery if not already done
            TrackMeshBaker.BuildTrackMenu();
            SceneryBuilder.BuildSceneryMenu();

            string scenePath = "Assets/Scenes/Track_F1Signature.unity";
            string sampleScenePath = "Assets/Scenes/Playground.unity";

            // Open or create the scene
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Add directional light with warm sunlight
            var lightGo = GameObject.Find("Directional Light");
            if (lightGo != null)
            {
                var light = lightGo.GetComponent<Light>();
                light.color = new Color(1.0f, 0.96f, 0.88f);
                light.intensity = 1.25f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            // Configure Camera with farClip 1500m
            var camGo = GameObject.Find("Main Camera");
            if (camGo != null)
            {
                var cam = camGo.GetComponent<Camera>();
                cam.farClipPlane = 1500f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 500f;
                RenderSettings.fogEndDistance = 1500f;
                RenderSettings.fogColor = new Color(0.7f, 0.8f, 0.9f);
            }

            // Instantiate Track Prefab
            var trackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/Track/Track.prefab");
            if (trackPrefab != null)
            {
                var trackInstance = (GameObject)PrefabUtility.InstantiatePrefab(trackPrefab);
                trackInstance.name = "Track_Built";
            }

            // Instantiate Car at Spawn Slot
            var trackConfig = TrackDataLoader.LoadTrack();
            Vector3 carSpawnPos = Vector3.zero;
            Quaternion carSpawnRot = Quaternion.identity;

            if (trackConfig != null && trackConfig.spawn_slot != null)
            {
                var sp = trackConfig.spawn_slot;
                carSpawnPos = new Vector3(sp.x, sp.y + 0.35f, sp.z);
                carSpawnRot = Quaternion.Euler(0f, sp.yaw_deg, 0f);
            }

            // Find car from SampleScene or build default car
            var carGo = GameObject.Find("Car");
            if (carGo == null)
            {
                // Create Car GameObject
                carGo = new GameObject("Car");
                var cc = carGo.AddComponent<CarController>();
                carGo.AddComponent<CarInput>();

                // Add simple visual chassis
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "BodyVisual";
                body.transform.SetParent(carGo.transform, false);
                body.transform.localScale = new Vector3(1.8f, 0.5f, 4.5f);
                body.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                var bodyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/CarBody_URP.mat");
                if (bodyMat != null) body.GetComponent<Renderer>().sharedMaterial = bodyMat;

                // Add 4 wheel visual cylinders
                for (int i = 0; i < 4; i++)
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    w.name = $"Wheel_{i}";
                    w.transform.SetParent(carGo.transform, false);
                    w.transform.localScale = new Vector3(0.7f, 0.35f, 0.7f);
                    w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    float wx = (i % 2 == 0) ? -0.85f : 0.85f;
                    float wz = (i < 2) ? 1.4f : -1.4f;
                    w.transform.localPosition = new Vector3(wx, 0f, wz);
                    var wMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wheel_URP.mat");
                    if (wMat != null) w.GetComponent<Renderer>().sharedMaterial = wMat;
                }
            }

            carGo.transform.position = carSpawnPos;
            carGo.transform.rotation = carSpawnRot;

            // Wire Chase Camera
            if (camGo != null)
            {
                var chase = camGo.AddComponent<ChaseCamera>();
                chase.target = carGo.transform;
            }

            // Save the scene
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[TrackPlaygroundSceneBuilder] Saved scene to {scenePath}");

            // Update Build Settings:
            // Make Assets/Scenes/Track_F1Signature.unity index 0, and SampleScene (Playground) index 1
            var buildScenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(scenePath, true),
                new EditorBuildSettingsScene(sampleScenePath, true)
            };
            EditorBuildSettings.scenes = buildScenes;
            Debug.Log($"[TrackPlaygroundSceneBuilder] Updated EditorBuildSettings with Track_F1Signature (index 0) and SampleScene (index 1).");
        }
    }
}

