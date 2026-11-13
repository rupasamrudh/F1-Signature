// TrackSceneVisibilityPlayModeTest.cs — PlayMode and Editor test validating track visibility, chunk renderers, car placement, road height, and spawn screenshot.
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using F1Sim.Simulation.Track;
using F1Sim.Track;

namespace F1Sim.Editor.Tests
{
    public class TrackSceneVisibilityPlayModeTest
    {
        const string ScenePath = "Assets/Scenes/Track_F1Signature.unity";
        const string ScreenshotDir = "Docs/screenshots";
        const string ScreenshotPath = "Docs/screenshots/spawn_view.png";

        [UnityTest]
        public IEnumerator TestTrackVisibilityInPlayMode()
        {
            // 1. Load scene
            if (SceneManager.GetActiveScene().name != "Track_F1Signature")
            {
                SceneManager.LoadScene("Track_F1Signature");
                yield return null;
            }

            // Wait 2 seconds for physics, spline, and visuals to settle
            float elapsed = 0f;
            while (elapsed < 2.0f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            RunAssertions();
            CaptureSpawnScreenshot();
        }

        [MenuItem("Tools/F1 Signature/Run Track Visibility Test")]
        public static void RunTestFromMenu()
        {
            Debug.Log("[TrackVisibilityTest] Running Track Visibility Test...");

            // Ensure scene is open
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath);
            }

            var test = new TrackSceneVisibilityPlayModeTest();
            test.RunAssertions();
            test.CaptureSpawnScreenshot();

            EditorUtility.DisplayDialog(
                "Track Visibility Test",
                "All assertions PASSED!\n- Active chunk renderers >= 20\n- Car lateral offset < 3.0 m\n- Surface is Asphalt\n- Road surface height matches track elevation\n- No flat plane above road\n- Screenshot saved to Docs/screenshots/spawn_view.png",
                "OK"
            );
        }

        public void RunAssertions()
        {
            // 1. Verify Track object and chunk renderers
            var trackObj = GameObject.Find("Track_Built");
            if (trackObj == null) trackObj = GameObject.Find("Track");
            Assert.IsNotNull(trackObj, "Track GameObject ('Track_Built' or 'Track') must exist in the scene.");

            int activeChunkRenderers = 0;
            for (int i = 0; i < trackObj.transform.childCount; i++)
            {
                var child = trackObj.transform.GetChild(i);
                if (child.name.StartsWith("Chunk_") && child.gameObject.activeInHierarchy)
                {
                    var renderers = child.GetComponentsInChildren<MeshRenderer>();
                    foreach (var r in renderers)
                    {
                        if (r.enabled && r.gameObject.activeInHierarchy)
                            activeChunkRenderers++;
                    }
                }
            }

            Debug.Log($"[TrackVisibilityTest] Active chunk mesh renderers: {activeChunkRenderers}");
            Assert.GreaterOrEqual(activeChunkRenderers, 20, "Track prefab must have >= 20 active chunk renderers.");

            // 2. Verify Car placement and spline lateral offset < 3m
            var carObj = GameObject.Find("Car");
            Assert.IsNotNull(carObj, "Car GameObject must exist in the scene.");

            var carCtrl = carObj.GetComponent<CarController>();
            Assert.IsNotNull(carCtrl, "Car must have CarController component attached.");

            var trackConfig = TrackDataLoader.LoadTrack();
            Assert.IsNotNull(trackConfig, "TrackConfig must load from Resources/Tracks/f1sig_track.");
            var spline = new TrackSpline(trackConfig);

            var query = spline.Query(carObj.transform.position.x, carObj.transform.position.z);
            float latOffset = MathF.Abs(query.lateralOffset);
            Debug.Log($"[TrackVisibilityTest] Car at ({carObj.transform.position.x:F2}, {carObj.transform.position.y:F2}, {carObj.transform.position.z:F2}), s={query.s:F1}m, offset={query.lateralOffset:F2}m, surface={query.surface}");
            Assert.Less(latOffset, 3.0f, $"Car lateral offset ({latOffset:F2} m) must be < 3.0 m at spawn.");

            // 3. Verify surface under car is Asphalt
            Assert.AreEqual(SurfaceType.Asphalt, query.surface, "Surface at spawn position must be Asphalt.");

            // 4. Verify road surface height under car matches track height at that s within 0.1 m
            float expectedHeight = query.heightY;
            float tireRadius = carCtrl.config != null ? carCtrl.config.tireRadius : 0.33f;
            float roadY = carObj.transform.position.y - tireRadius;

            // Check raycast if colliders are active, or compare directly with track height
            if (Physics.Raycast(new Vector3(carObj.transform.position.x, carObj.transform.position.y + 1f, carObj.transform.position.z), Vector3.down, out RaycastHit hit, 5f))
            {
                roadY = hit.point.y;
            }

            float heightDiff = MathF.Abs(roadY - expectedHeight);
            Debug.Log($"[TrackVisibilityTest] Road surface height={roadY:F2}m, expected track height={expectedHeight:F2}m (diff={heightDiff:F3}m)");
            Assert.Less(heightDiff, 0.1f, $"Road surface height under car ({roadY:F2}m) must match track height ({expectedHeight:F2}m) within 0.1 m.");

            // 5. Verify no flat plane above road
            var oldPlane = GameObject.Find("Plane");
            Assert.IsNull(oldPlane, "The legacy flat brown 'Plane' must be removed from the scene.");

            Debug.Log("[TrackVisibilityTest] All assertions PASSED!");
        }

        public void CaptureSpawnScreenshot()
        {
            if (!Directory.Exists(ScreenshotDir))
            {
                Directory.CreateDirectory(ScreenshotDir);
            }

            var cam = Camera.main;
            if (cam == null)
            {
                var camObj = GameObject.Find("Main Camera");
                if (camObj != null) cam = camObj.GetComponent<Camera>();
            }

            if (cam != null)
            {
                var rt = new RenderTexture(1920, 1080, 24);
                var prevTarget = cam.targetTexture;
                var prevActive = RenderTexture.active;

                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;

                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();

                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;

                byte[] pngData = tex.EncodeToPNG();
                File.WriteAllBytes(ScreenshotPath, pngData);
                Debug.Log($"[TrackVisibilityTest] Spawn screenshot saved to {ScreenshotPath} ({pngData.Length} bytes).");

                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }
            else
            {
                ScreenCapture.CaptureScreenshot(ScreenshotPath);
                Debug.Log($"[TrackVisibilityTest] ScreenCapture requested for {ScreenshotPath}.");
            }
        }
    }
}

