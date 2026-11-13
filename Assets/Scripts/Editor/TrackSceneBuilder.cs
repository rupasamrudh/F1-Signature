// TrackSceneBuilder.cs — Rebuilds Track_F1Signature.unity purely through the Unity API.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using F1Sim.Simulation;
using F1Sim.Track;

namespace F1Sim.Editor
{
    public static class TrackSceneBuilder
    {
        const string TargetScenePath = "Assets/Scenes/Track_F1Signature.unity";

        [MenuItem("Tools/F1 Signature/Rebuild Track Scene")]
        public static void RebuildTrackSceneMenu()
        {
            Debug.Log("[TrackSceneBuilder] Starting clean rebuild of Track_F1Signature.unity...");

            // 1. Ensure Track Prefab is available; bake if missing
            string trackPrefabPath = "Assets/Generated/Track/Track.prefab";
            var trackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(trackPrefabPath);
            if (trackPrefab == null)
            {
                Debug.Log("[TrackSceneBuilder] Track prefab missing. Running TrackMeshBaker first...");
                TrackMeshBaker.BuildTrackMenu();
                trackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(trackPrefabPath);
            }

            // 2. Create clean new empty scene
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 3. Directional Light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.98f, 0.95f);
            light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lightGo.transform.position = new Vector3(0f, 20f, 0f);

            // 4. RenderSettings & Fog
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.haloStrength = 0f;
            RenderSettings.flareStrength = 0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 500f;
            RenderSettings.fogEndDistance = 1500f;
            RenderSettings.fogColor = new Color(0.7f, 0.8f, 0.9f);

            // 5. Instantiate Track Prefab
            if (trackPrefab != null)
            {
                var trackInstance = (GameObject)PrefabUtility.InstantiatePrefab(trackPrefab);
                trackInstance.name = "Track_Built";
            }
            else
            {
                Debug.LogError("[TrackSceneBuilder] Track prefab could not be loaded or baked!");
            }

            // 6. Car spawn position & rotation from track data
            var trackConfig = TrackDataLoader.LoadTrack();
            Vector3 carSpawnPos = Vector3.zero;
            Quaternion carSpawnRot = Quaternion.identity;

            if (trackConfig != null && trackConfig.spawn_slot != null)
            {
                var sp = trackConfig.spawn_slot;
                carSpawnPos = new Vector3(sp.x, sp.y + 0.35f, sp.z);
                carSpawnRot = Quaternion.Euler(0f, sp.yaw_deg, 0f);
            }

            // 7. Create Car GameObject
            var carGo = new GameObject("Car");
            carGo.transform.position = carSpawnPos;
            carGo.transform.rotation = carSpawnRot;

            var cc = carGo.AddComponent<CarController>();
            cc.config = new CarParams
            {
                paramsVersion = CarParams.CURRENT_VERSION,
                brakeBiasFront = 0.68f
            };

            var ci = carGo.AddComponent<CarInput>();
            ci.inputParamsVersion = CarInput.CURRENT_INPUT_VERSION;
            ci.controlMode = CarInput.ControlMode.Tilt;
            ci.useTilt = true;
            ci.sensitivitySlider = 2.0f;
            ci.maxTiltDeg = 90f;
            ci.deadzoneDeg = 4.5f;
            ci.steerSmoothing = 14f;
            ci.tiltFilter = 20f;
            ci.minPlaneG = 0.08f;
            ci.steerExpo = 1.40f;
            ci.useSpeedSensitiveLock = false;
            ci.speedLockMinMultiplier = 1.0f;
            ci.speedLockMaxKmh = 320f;
            ci.invertTilt = false;
            ci.autoCenter = true;
            ci.autoCenterRate = 2f;
            ci.autoCenterWindowDeg = 8f;

            // Visual container for vehicle
            var visualsGo = new GameObject("CarVisuals");
            visualsGo.transform.SetParent(carGo.transform, false);

            // Attempt to load and instantiate Ferrari_SF26 prefab (preferred), fallback to Mercedes_W17
            string ferrariPrefabPath = "Assets/Prefabs/Vehicles/Ferrari_SF26.prefab";
            var vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ferrariPrefabPath);
            if (vehiclePrefab == null)
            {
                Debug.Log("[TrackSceneBuilder] Ferrari_SF26 prefab missing. Running FerrariModelProcessor.BuildPrefab...");
                F1Sim.EditorTools.FerrariModelProcessor.BuildPrefab();
                vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ferrariPrefabPath);
            }
            if (vehiclePrefab == null)
            {
                vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Vehicles/Mercedes_W17.prefab");
            }

            if (vehiclePrefab != null)
            {
                var vehicleInstance = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefab);
                vehicleInstance.name = vehiclePrefab.name;
                vehicleInstance.transform.SetParent(visualsGo.transform, false);
                vehicleInstance.transform.localPosition = Vector3.zero;
                vehicleInstance.transform.localRotation = Quaternion.identity;

                // Add WheelVisualBinder to animate steering and rolling
                var binder = visualsGo.AddComponent<F1Sim.Vehicle.WheelVisualBinder>();
                binder.controller = cc;
                binder.wheelFL = vehicleInstance.transform.Find("Wheel_FL");
                binder.wheelFR = vehicleInstance.transform.Find("Wheel_FR");
                binder.wheelRL = vehicleInstance.transform.Find("Wheel_RL");
                binder.wheelRR = vehicleInstance.transform.Find("Wheel_RR");
            }
            else
            {
                Debug.LogWarning("[TrackSceneBuilder] Falling back to placeholder primitives for car visuals.");
                // Fallback visual chassis
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var bodyCol = body.GetComponent<Collider>();
                if (bodyCol != null) Object.DestroyImmediate(bodyCol);
                body.name = "BodyVisual";
                body.transform.SetParent(visualsGo.transform, false);
                body.transform.localScale = new Vector3(1.8f, 0.5f, 4.5f);
                body.transform.localPosition = new Vector3(0f, 0.25f, 0f);

                for (int i = 0; i < 4; i++)
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    var wCol = w.GetComponent<Collider>();
                    if (wCol != null) Object.DestroyImmediate(wCol);
                    w.name = $"Wheel_{i}";
                    w.transform.SetParent(visualsGo.transform, false);
                    w.transform.localScale = new Vector3(0.7f, 0.35f, 0.7f);
                    w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    float wx = (i % 2 == 0) ? -0.85f : 0.85f;
                    float wz = (i < 2) ? 1.4f : -1.4f;
                    w.transform.localPosition = new Vector3(wx, 0f, wz);
                }
            }

            // 8. Main Camera with ChaseCamera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f; // Depth precision fix for mobile
            cam.farClipPlane = 1500f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();

            var chase = camGo.AddComponent<ChaseCamera>();
            chase.target = carGo.transform;
            chase.car = cc;
            chase.offset = new Vector3(0f, 2.5f, -9f);
            chase.posLag = 8f;
            chase.rotLag = 6f;
            chase.fovBase = 60f;
            chase.fovMax = 78f;
            chase.SnapToTarget();

            // 9. Save scene
            EditorSceneManager.SaveScene(scene, TargetScenePath);
            Debug.Log($"[TrackSceneBuilder] Successfully rebuilt and saved {TargetScenePath} using Unity API!");

            // 10. Run validation on the newly generated scene
            var valResult = SceneValidator.ValidateScene(TargetScenePath);
            if (valResult.IsValid)
            {
                Debug.Log($"[TrackSceneBuilder] Validation PASSED for {TargetScenePath} (0 missing scripts, 0 broken refs).");
            }
            else
            {
                Debug.LogError($"[TrackSceneBuilder] Validation FAILED for {TargetScenePath}! Issues: {valResult.TotalIssues}");
            }
        }
    }
}
