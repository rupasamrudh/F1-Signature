// TrackGeometryTests.cs — EditMode & PlayMode test suite validating track geometry, surface visibility, kerbs, terrain road-band height, and screenshot capture.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using F1Sim.Simulation.Track;
using F1Sim.Track;

namespace F1Sim.Editor.Tests
{
    public class TrackGeometryTests
    {
        const string ScenePath = "Assets/Scenes/Track_F1Signature.unity";
        const string ScreenshotDir = "Docs/screenshots";

        [OneTimeSetUp]
        public void Setup()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath);
            }
        }

        [MenuItem("Tools/F1 Signature/Run All Track Tests & Capture Screenshots")]
        public static void RunAllTestsAndCaptureMenu()
        {
            Debug.Log("[TrackGeometryTests] Running full test suite & screenshot generation...");
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath);
            }

            var suite = new TrackGeometryTests();
            suite.T1_RaycastTrackCoverageTest();
            suite.T2_CornerApexRaycastTest();
            suite.T3_TerrainNeverRisesAboveRoadBandTest();
            suite.T4_KerbMeshCoverageTest();
            suite.T5_MaterialsAndShadersTest();

            CaptureAllScreenshots();

            EditorUtility.DisplayDialog(
                "Track Tests & Screenshots Complete",
                "All tests T1 - T5 PASSED!\nScreenshots saved in Docs/screenshots/\n- spawn_view.png & spawn_surf_dbg.png\n- t3_view.png & t3_surf_dbg.png\n- t12_view.png & t12_surf_dbg.png\n- pit_straight_view.png & pit_straight_surf_dbg.png",
                "OK"
            );
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // T1. Raycast Test: 5000+ points vertically down from 10m above road
        // ─────────────────────────────────────────────────────────────────────────────
        [Test]
        public void T1_RaycastTrackCoverageTest()
        {
            var config = TrackDataLoader.LoadTrack();
            Assert.IsNotNull(config, "TrackConfig must load");
            var spline = new TrackSpline(config);

            float totalLength = config.total_length_m;
            float stepS = 5.0f; // Every 5 meters along track
            float[] lateralOffsets = new float[] { -9.0f, -6.0f, -3.0f, 0.0f, 3.0f, 6.0f, 9.0f }; // Across asphalt, lines, kerbs, runoff

            int pointsTested = 0;
            int terrainFailures = 0;
            int missedRays = 0;

            for (float s = 0f; s < totalLength; s += stepS)
            {
                var q = spline.QueryAtS(s);
                Vector3 left = new Vector3(-Mathf.Cos(q.headingRad), 0f, Mathf.Sin(q.headingRad));
                Vector3 center = new Vector3(q.x, q.y, q.z);

                for (int lo = 0; lo < lateralOffsets.Length; lo++)
                {
                    float offset = lateralOffsets[lo];
                    Vector3 rayOrigin = center + left * offset + Vector3.up * 10.0f;
                    pointsTested++;

                    if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 25.0f))
                    {
                        string hitName = hit.collider.gameObject.name;
                        if (hitName.Contains("Terrain") || hitName.Contains("Grass"))
                        {
                            // Terrain hit above track surface!
                            terrainFailures++;
                        }
                    }
                    else
                    {
                        missedRays++;
                    }
                }
            }

            Debug.Log($"[T1] Points tested: {pointsTested}, Failures (Terrain hit first): {terrainFailures}, Missed rays: {missedRays}");
            Assert.GreaterOrEqual(pointsTested, 5000, "T1 requires at least 5000 points tested.");
            Assert.AreEqual(0, terrainFailures, $"T1 FAILED: Terrain hit first at {terrainFailures} points inside road/runoff!");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // T2. Corner Apex Raycast Test: 15 corners, 10 lateral offsets (-25 to +25m)
        // ─────────────────────────────────────────────────────────────────────────────
        [Test]
        public void T2_CornerApexRaycastTest()
        {
            var config = TrackDataLoader.LoadTrack();
            Assert.IsNotNull(config, "TrackConfig must load");
            var spline = new TrackSpline(config);

            float[] lateralOffsets = new float[] { -25f, -19f, -13f, -7f, -2f, 2f, 7f, 13f, 19f, 25f };

            int totalCornerTests = 0;
            int cornerFailures = 0;

            for (int i = 0; i < config.corners.Count; i++)
            {
                var c = config.corners[i];
                float apexS = c.apex_s;
                string cName = !string.IsNullOrEmpty(c.name) ? c.name : $"T{i + 1}";

                var q = spline.QueryAtS(apexS);
                Vector3 left = new Vector3(-Mathf.Cos(q.headingRad), 0f, Mathf.Sin(q.headingRad));
                Vector3 center = new Vector3(q.x, q.y, q.z);
                float halfW = (q.trackWidth > 0 ? q.trackWidth : 14.0f) * 0.5f;

                int cPass = 0;
                int cFail = 0;

                for (int j = 0; j < lateralOffsets.Length; j++)
                {
                    float lat = lateralOffsets[j];
                    totalCornerTests++;
                    Vector3 rayOrigin = center + left * lat + Vector3.up * 10.0f;

                    if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 25.0f))
                    {
                        string hitName = hit.collider.gameObject.name;
                        // Within road and kerb (|lat| <= halfW + 1.5m), terrain must NEVER be the first hit
                        if (Mathf.Abs(lat) <= halfW + 1.5f && (hitName.Contains("Terrain") || hitName.Contains("Grass")))
                        {
                            cFail++;
                            cornerFailures++;
                        }
                        else
                        {
                            cPass++;
                        }
                    }
                    else
                    {
                        cPass++;
                    }
                }

                Debug.Log($"[T2] {cName} (s={apexS:F1}m, dir={c.direction}, r={c.radius_m:F1}m): {cPass} passed, {cFail} failed.");
            }

            Assert.AreEqual(0, cornerFailures, $"T2 FAILED: {cornerFailures} corner apex points blocked by terrain!");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // T3. Terrain Road-Band Height Test: Max (Terrain - Road) <= -0.03 m
        // ─────────────────────────────────────────────────────────────────────────────
        [Test]
        public void T3_TerrainNeverRisesAboveRoadBandTest()
        {
            var config = TrackDataLoader.LoadTrack();
            Assert.IsNotNull(config, "TrackConfig must load");

            var terrainObj = GameObject.Find("Terrain_Grid");
            if (terrainObj == null) terrainObj = GameObject.Find("Track_Terrain");
            if (terrainObj == null)
            {
                var trackBuilt = GameObject.Find("Track_Built");
                if (trackBuilt != null)
                {
                    var tTransform = trackBuilt.transform.Find("Track_Terrain") ?? trackBuilt.transform.Find("Terrain_Grid");
                    if (tTransform != null) terrainObj = tTransform.gameObject;
                }
            }
            Assert.IsNotNull(terrainObj, "Terrain object must exist in scene.");

            var terrainFilters = terrainObj.GetComponentsInChildren<MeshFilter>();
            Assert.IsNotEmpty(terrainFilters, "Terrain_Grid must have MeshFilter components.");

            float maxDiff = float.MinValue;
            var samples = config.samples;

            for (int tf = 0; tf < terrainFilters.Length; tf++)
            {
                var mf = terrainFilters[tf];
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;

                var verts = mesh.vertices;
                var trans = mf.transform;

                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 worldPt = trans.TransformPoint(verts[v]);

                    // Check if inside road band of any nearby sample
                    for (int sIdx = 0; sIdx < samples.Count; sIdx += 2)
                    {
                        var s = samples[sIdx];
                        float halfW = (s.width > 0 ? s.width : 14.0f) * 0.5f;
                        float bandR = halfW + 13.5f; // road half-width + kerb(1.5) + runoff(8) + 4m

                        float dx = worldPt.x - s.x;
                        float dz = worldPt.z - s.z;
                        if (dx * dx + dz * dz <= bandR * bandR)
                        {
                            float diff = worldPt.y - s.y;
                            if (diff > maxDiff) maxDiff = diff;
                        }
                    }
                }
            }

            Debug.Log($"[T3] Maximum (Terrain Height - Road Height) within road band: {maxDiff:F4} m (Must be <= -0.03 m).");
            Assert.LessOrEqual(maxDiff, -0.03f, $"T3 FAILED: Terrain height exceeds road surface within road band! Max diff: {maxDiff:F4} m");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // T4. Kerb Coverage Test: Exists for every corner entry to exit + fast corners outside
        // ─────────────────────────────────────────────────────────────────────────────
        [Test]
        public void T4_KerbMeshCoverageTest()
        {
            var config = TrackDataLoader.LoadTrack();
            Assert.IsNotNull(config, "TrackConfig must load");

            var kerbRanges = TrackMeshBaker.ComputeKerbRanges(config);
            Assert.GreaterOrEqual(kerbRanges.Count, 15, "Kerb ranges must cover all 15 corners.");

            float totalKerbLen = 0f;
            foreach (var kr in kerbRanges)
            {
                float len = kr.endS - kr.startS;
                totalKerbLen += len;
            }

            // Count kerb renderers in track prefab
            var trackObj = GameObject.Find("Track_Built");
            Assert.IsNotNull(trackObj, "Track_Built must exist");

            int kerbMeshCount = 0;
            var renderers = trackObj.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in renderers)
            {
                if (r.name.Contains("Kerb")) kerbMeshCount++;
            }

            Debug.Log($"[T4] Kerb sections: {kerbRanges.Count}, Total kerb length: {totalKerbLen:F1} m, Kerb mesh objects: {kerbMeshCount}");
            Assert.GreaterOrEqual(kerbMeshCount, 1, "Track must have kerb mesh objects covering corners.");
            Assert.Greater(totalKerbLen, 1200f, "Total kerb length must be > 1200m across circuit.");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // T5. Materials & Shaders Test: Non-null, URP-compatible, non-null textures
        // ─────────────────────────────────────────────────────────────────────────────
        [Test]
        public void T5_MaterialsAndShadersTest()
        {
            var trackObj = GameObject.Find("Track_Built");
            Assert.IsNotNull(trackObj, "Track_Built must exist");

            var renderers = trackObj.GetComponentsInChildren<MeshRenderer>();
            Assert.IsNotEmpty(renderers, "Track must have MeshRenderers.");

            int checkedCount = 0;
            foreach (var r in renderers)
            {
                checkedCount++;
                Assert.IsNotNull(r.sharedMaterial, $"Renderer {r.gameObject.name} has NULL sharedMaterial!");
                Assert.IsNotNull(r.sharedMaterial.shader, $"Material {r.sharedMaterial.name} on {r.gameObject.name} has NULL shader!");

                string shaderName = r.sharedMaterial.shader.name;
                bool isURP = shaderName.Contains("Universal Render Pipeline") || shaderName.Contains("URP") || shaderName.Contains("Standard");
                Assert.IsTrue(isURP, $"Shader {shaderName} on {r.gameObject.name} is not URP-compatible!");

                if (r.sharedMaterial.name.Contains("Kerb") || r.sharedMaterial.name.Contains("Checker") || r.sharedMaterial.name.Contains("Grass") || r.sharedMaterial.name.Contains("Gravel"))
                {
                    Texture tex = r.sharedMaterial.mainTexture ?? r.sharedMaterial.GetTexture("_BaseMap");
                    Assert.IsNotNull(tex, $"Material {r.sharedMaterial.name} on {r.gameObject.name} expects a texture but mainTexture is null!");
                }
            }

            Debug.Log($"[T5] Checked {checkedCount} track renderers. All materials, shaders, and textures are valid!");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // T6. PlayMode Test: Camera-to-car ray not blocked by terrain at 5 track locations
        // ─────────────────────────────────────────────────────────────────────────────
        [UnityTest]
        public IEnumerator T6_CameraNotBlockedByTerrainPlayModeTest()
        {
            if (SceneManager.GetActiveScene().name != "Track_F1Signature")
            {
                SceneManager.LoadScene("Track_F1Signature");
                yield return null;
            }

            var car = GameObject.Find("Car");
            Assert.IsNotNull(car, "Car must exist in scene");

            var camGo = GameObject.Find("Main Camera");
            Assert.IsNotNull(camGo, "Main Camera must exist");
            var chaseCam = camGo.GetComponent<ChaseCamera>();
            Assert.IsNotNull(chaseCam, "ChaseCamera must be attached to Main Camera");

            var config = TrackDataLoader.LoadTrack();
            var spline = new TrackSpline(config);

            // Test at 5 locations: Spawn (0m), T3 (584m), T8 (2294m), T10 (2718m), T12 (3260m)
            float[] testS = new float[] { 0f, 584.1f, 2294.5f, 2718.6f, 3260.8f };
            string[] testNames = new string[] { "Spawn", "T3 Apex", "T8 Apex", "T10 Hairpin", "T12 Corner" };

            for (int i = 0; i < testS.Length; i++)
            {
                float s = testS[i];
                var q = spline.QueryAtS(s);
                Vector3 carPos = new Vector3(q.x, q.y + 0.35f, q.z);
                Quaternion carRot = Quaternion.Euler(0f, q.headingRad * Mathf.Rad2Deg, 0f);

                car.transform.position = carPos;
                car.transform.rotation = carRot;
                chaseCam.SnapToTarget();

                yield return null;

                Vector3 camPos = camGo.transform.position;
                Vector3 targetPos = car.transform.position + Vector3.up * 0.8f;
                Vector3 dir = (targetPos - camPos).normalized;
                float dist = Vector3.Distance(camPos, targetPos);

                int blockedCount = 0;
                if (Physics.Raycast(camPos, dir, out RaycastHit hit, dist))
                {
                    if (hit.collider.gameObject.name.Contains("Terrain") || hit.collider.gameObject.name.Contains("Grass"))
                    {
                        blockedCount++;
                        Debug.LogError($"[T6] Line of sight BLOCKED by terrain at {testNames[i]}! Hit: {hit.collider.name}");
                    }
                }

                Debug.Log($"[T6] Location {testNames[i]} (s={s:F1}m): Camera-to-car ray unobstructed.");
                Assert.AreEqual(0, blockedCount, $"Camera ray to car was blocked by terrain at {testNames[i]}!");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Automated Screenshot Generation for Normal View & Surf Dbg View
        // ─────────────────────────────────────────────────────────────────────────────
        public static void CaptureAllScreenshots()
        {
            if (!Directory.Exists(ScreenshotDir)) Directory.CreateDirectory(ScreenshotDir);

            var camGo = GameObject.Find("Main Camera");
            if (camGo == null)
            {
                Debug.LogError("[TrackGeometryTests] Main Camera not found for screenshots!");
                return;
            }

            var cam = camGo.GetComponent<Camera>();
            var chase = camGo.GetComponent<ChaseCamera>();
            var car = GameObject.Find("Car");
            var diag = GameObject.FindAnyObjectByType<TrackSurfaceDiagnostics>();

            var config = TrackDataLoader.LoadTrack();
            var spline = new TrackSpline(config);

            var locations = new (string id, float s, string name)[]
            {
                ("spawn", 0f, "Spawn"),
                ("t3", 584.1f, "T3"),
                ("t12", 3260.8f, "T12"),
                ("pit_straight", 4200f, "Pit Straight")
            };

            foreach (var loc in locations)
            {
                var q = spline.QueryAtS(loc.s);
                if (car != null)
                {
                    car.transform.position = new Vector3(q.x, q.y + 0.35f, q.z);
                    car.transform.rotation = Quaternion.Euler(0f, q.headingRad * Mathf.Rad2Deg, 0f);
                }
                if (chase != null) chase.SnapToTarget();

                // 1. Normal View
                if (diag != null) diag.SetSurfaceDebug(false);
                CaptureCameraToFile(cam, $"{ScreenshotDir}/{loc.id}_view.png");

                // 2. Surf Dbg View
                if (diag != null) diag.SetSurfaceDebug(true);
                CaptureCameraToFile(cam, $"{ScreenshotDir}/{loc.id}_surf_dbg.png");

                // Restore
                if (diag != null) diag.SetSurfaceDebug(false);
            }

            Debug.Log($"[TrackGeometryTests] Saved 8 screenshots (Normal & Surf Dbg) to {ScreenshotDir}/");
        }

        static void CaptureCameraToFile(Camera cam, string filePath)
        {
            RenderTexture rt = new RenderTexture(1280, 720, 24);
            RenderTexture prevRt = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();

            cam.targetTexture = prevRt;
            RenderTexture.active = prevActive;
            UnityEngine.Object.DestroyImmediate(rt);

            File.WriteAllBytes(filePath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
