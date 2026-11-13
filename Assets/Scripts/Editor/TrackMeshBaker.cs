// TrackMeshBaker.cs — Idempotent procedural track & terrain mesh generator for F1 Signature.
// Generates chunks with Asphalt, Raised White Lines (+2cm), Raised Kerbs (+4cm),
// Runoff Tarmac, Gravel Beds, Barriers, Gantry, and a regular Grid-based Terrain (replacing the offset skirt).
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using F1Sim.Simulation.Track;
using F1Sim.Track;

namespace F1Sim.Editor
{
    [InitializeOnLoad]
    public static class TrackMeshBaker
    {
        const float CHUNK_LENGTH_M = 200f;
        const float TEXTURE_REPEAT_M = 10f;
        const float TERRAIN_CELL_SIZE = 8.5f;
        const float TERRAIN_MARGIN = 150f;
        const float TERRAIN_CHUNK_SIZE = 200f;

        static TrackMeshBaker()
        {
            // Replaced by InterlagosTrackProcessor for 3D track model
        }

        public struct KerbRange
        {
            public string name;
            public bool isLeft; // true = Left side, false = Right side
            public float startS;
            public float endS;
        }

        [MenuItem("Tools/F1 Signature/Build Track (Legacy Procedural)")]
        public static void BuildLegacyProceduralTrack()
        {
            BuildProceduralTrackInternal();
        }

        [MenuItem("Tools/F1 Signature/Build Track")]
        public static void BuildTrackMenu()
        {
            F1Sim.EditorTools.InterlagosTrackProcessor.BuildTrack();
        }

        public static void BuildProceduralTrackInternal()
        {
            var config = TrackDataLoader.LoadTrack();
            if (config == null)
            {
                EditorUtility.DisplayDialog("Build Track Error", "Failed to load f1sig_track.json", "OK");
                return;
            }

            var spline = new TrackSpline(config);
            var mats = TrackMaterialBuilder.GetOrCreateTrackMaterials();

            EnsureFolder("Assets/Generated");
            EnsureFolder("Assets/Generated/Track");
            EnsureFolder("Assets/Generated/Track/Meshes");

            // Clean up old track in scene or create new root
            var oldRoot = GameObject.Find("Track_Built");
            if (oldRoot != null) Undo.DestroyObjectImmediate(oldRoot);

            var trackRoot = new GameObject("Track_Built");
            Undo.RegisterCreatedObjectUndo(trackRoot, "Build Track");

            // Attach TrackSurfaceDiagnostics component for on-device diagnostics and Surf Dbg
            trackRoot.AddComponent<TrackSurfaceDiagnostics>();

            float totalLength = config.total_length_m;
            int numChunks = Mathf.CeilToInt(totalLength / CHUNK_LENGTH_M);

            Debug.Log($"[TrackMeshBaker] Starting bake: Track length {totalLength:F1} m ({numChunks} chunks).");

            // Calculate Kerb Ranges for all 15 corners (inside) + fast corners (outside)
            List<KerbRange> kerbRanges = ComputeKerbRanges(config);
            Debug.Log($"[TrackMeshBaker] Generated {kerbRanges.Count} kerb sections across 15 corners.");

            // Step sizing: 1m on tight corners (radius < 40m), 2m on straights
            float StepForS(float s)
            {
                var q = spline.QueryAtS(s);
                if (Mathf.Abs(q.curvature) > 0.025f) return 1.0f;
                return 2.0f;
            }

            int totalVertices = 0;
            int totalTriangles = 0;

            // 1. Bake Track Ribbons (Asphalt, White Lines, Kerbs, Runoff, Gravel, Barriers)
            for (int chunkIdx = 0; chunkIdx < numChunks; chunkIdx++)
            {
                float chunkStartS = chunkIdx * CHUNK_LENGTH_M;
                float chunkEndS = Mathf.Min((chunkIdx + 1) * CHUNK_LENGTH_M, totalLength);
                if (chunkIdx == numChunks - 1) chunkEndS = totalLength;

                List<float> sList = new List<float>();
                float currS = chunkStartS;
                while (currS < chunkEndS)
                {
                    sList.Add(currS);
                    currS += StepForS(currS);
                }
                sList.Add(chunkEndS);

                var chunkObj = new GameObject($"Chunk_{chunkIdx:D2}_{chunkStartS:F0}_{chunkEndS:F0}");
                chunkObj.transform.SetParent(trackRoot.transform, false);

                BuildAsphalt(chunkObj, sList, spline, mats.asphalt, chunkIdx, ref totalVertices, ref totalTriangles);
                BuildWhiteLines(chunkObj, sList, spline, mats.whiteLine, chunkIdx, ref totalVertices, ref totalTriangles);
                BuildKerbs(chunkObj, sList, spline, kerbRanges, mats.kerb, chunkIdx, ref totalVertices, ref totalTriangles);
                BuildRunoffAndGravel(chunkObj, sList, spline, mats.runoffTarmac, mats.gravel, chunkIdx, ref totalVertices, ref totalTriangles);
                BuildBarriers(chunkObj, sList, spline, mats.barrier, chunkIdx, ref totalVertices, ref totalTriangles);
            }

            // 2. Build Start/Finish Gantry & Checker Line Strip
            BuildStartFinishGantry(trackRoot, spline, mats.checker, mats.barrier);

            // 3. Bake Regular Grid Terrain (replaces the folded offset-ribbon skirt)
            Debug.Log("[TrackMeshBaker] Baking regular grid terrain...");
            BuildGridTerrain(trackRoot, config, spline, mats.grass, ref totalVertices, ref totalTriangles);

            // 4. Save Track Prefab
            string prefabPath = "Assets/Generated/Track/Track.prefab";
            PrefabUtility.SaveAsPrefabAsset(trackRoot, prefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TrackMeshBaker] Complete! Total Verts: {totalVertices}, Tris: {totalTriangles}. Saved to {prefabPath}");
        }

        public static List<KerbRange> ComputeKerbRanges(TrackConfig config)
        {
            var list = new List<KerbRange>();
            if (config.corners == null || config.corners.Count == 0) return list;

            var samples = config.samples;
            float totalLen = config.total_length_m;

            for (int i = 0; i < config.corners.Count; i++)
            {
                var c = config.corners[i];
                float apexS = c.apex_s;
                string cName = !string.IsNullOrEmpty(c.name) ? c.name : $"T{i + 1}";
                bool isLeftTurn = (c.direction == "left");

                // Find entry and exit by scanning curvature around apex
                int apexIdx = 0;
                float minDiff = float.MaxValue;
                for (int sIdx = 0; sIdx < samples.Count; sIdx++)
                {
                    float diff = Mathf.Abs(samples[sIdx].s - apexS);
                    if (diff < minDiff) { minDiff = diff; apexIdx = sIdx; }
                }

                int leftIdx = apexIdx;
                while (leftIdx > 0 && Mathf.Abs(samples[leftIdx].curvature) > 0.003f) leftIdx--;

                int rightIdx = apexIdx;
                while (rightIdx < samples.Count - 1 && Mathf.Abs(samples[rightIdx].curvature) > 0.003f) rightIdx++;

                float entryS = Mathf.Min(samples[leftIdx].s, apexS - 15f);
                float exitS = Mathf.Max(samples[rightIdx].s, apexS + 15f);

                // Inside Kerb (left for left turns, right for right turns)
                list.Add(new KerbRange
                {
                    name = $"{cName}_Inside",
                    isLeft = isLeftTurn,
                    startS = entryS,
                    endS = exitS
                });

                // Outside Kerb for fast corners (radius >= 45m)
                if (c.radius_m >= 45.0f)
                {
                    list.Add(new KerbRange
                    {
                        name = $"{cName}_Outside",
                        isLeft = !isLeftTurn,
                        startS = entryS,
                        endS = exitS
                    });
                }
            }
            return list;
        }

        static void BuildAsphalt(GameObject parent, List<float> sList, TrackSpline spline, Material mat, int chunkIdx, ref int totalV, ref int totalT)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            int numS = sList.Count;
            for (int i = 0; i < numS; i++)
            {
                float s = sList[i];
                var q = spline.QueryAtS(s);
                float halfW = (q.trackWidth > 0 ? q.trackWidth : 14.0f) * 0.5f;

                float rad = q.headingRad;
                Vector3 fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 left = new Vector3(-fwd.z, 0f, fwd.x);
                Vector3 center = new Vector3(q.x, q.y, q.z);

                Vector3 ptLeft = center + left * halfW;
                Vector3 ptRight = center - left * halfW;

                float v = s / TEXTURE_REPEAT_M;
                verts.Add(ptLeft);  uvs.Add(new Vector2(0f, v));
                verts.Add(ptRight); uvs.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < numS - 1; i++)
            {
                int i0 = i * 2, i1 = i * 2 + 1, i2 = (i + 1) * 2, i3 = (i + 1) * 2 + 1;
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }

            CreateMeshObject(parent, "Asphalt", verts, uvs, tris, mat, chunkIdx, true, ref totalV, ref totalT);
        }

        static void BuildWhiteLines(GameObject parent, List<float> sList, TrackSpline spline, Material mat, int chunkIdx, ref int totalV, ref int totalT)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float lineW = 0.15f; // 15 cm wide standard F1 line
            float lineElevation = 0.02f; // Raised 2 cm above road to prevent Z-fighting

            int numS = sList.Count;
            for (int i = 0; i < numS; i++)
            {
                float s = sList[i];
                var q = spline.QueryAtS(s);
                float halfW = (q.trackWidth > 0 ? q.trackWidth : 14.0f) * 0.5f;

                float rad = q.headingRad;
                Vector3 fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 left = new Vector3(-fwd.z, 0f, fwd.x);
                Vector3 center = new Vector3(q.x, q.y + lineElevation, q.z);

                // Left White Line
                Vector3 lOuter = center + left * halfW;
                Vector3 lInner = center + left * (halfW - lineW);

                // Right White Line
                Vector3 rOuter = center - left * halfW;
                Vector3 rInner = center - left * (halfW - lineW);

                float v = s / 5.0f;
                verts.Add(lOuter); uvs.Add(new Vector2(0f, v));
                verts.Add(lInner); uvs.Add(new Vector2(1f, v));
                verts.Add(rInner); uvs.Add(new Vector2(0f, v));
                verts.Add(rOuter); uvs.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < numS - 1; i++)
            {
                int b0 = i * 4, b1 = (i + 1) * 4;
                // Left line
                tris.Add(b0); tris.Add(b1); tris.Add(b0 + 1);
                tris.Add(b0 + 1); tris.Add(b1); tris.Add(b1 + 1);
                // Right line
                tris.Add(b0 + 2); tris.Add(b1 + 2); tris.Add(b0 + 3);
                tris.Add(b0 + 3); tris.Add(b1 + 2); tris.Add(b1 + 3);
            }

            CreateMeshObject(parent, "WhiteLines", verts, uvs, tris, mat, chunkIdx, true, ref totalV, ref totalT);
        }

        static void BuildKerbs(GameObject parent, List<float> sList, TrackSpline spline, List<KerbRange> kerbRanges, Material mat, int chunkIdx, ref int totalV, ref int totalT)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float kerbW = 1.5f;
            float kerbBaseH = 0.02f; // starts 2cm above road
            float kerbTopH = 0.04f;  // raised 4cm above road at outer edge

            for (int i = 0; i < sList.Count - 1; i++)
            {
                float s0 = sList[i];
                float s1 = sList[i + 1];

                // Check active kerbs on left and right
                bool hasLeft = IsKerbActive(s0, s1, kerbRanges, true);
                bool hasRight = IsKerbActive(s0, s1, kerbRanges, false);

                if (!hasLeft && !hasRight) continue;

                var q0 = spline.QueryAtS(s0);
                var q1 = spline.QueryAtS(s1);

                float hw0 = (q0.trackWidth > 0 ? q0.trackWidth : 14f) * 0.5f;
                float hw1 = (q1.trackWidth > 0 ? q1.trackWidth : 14f) * 0.5f;

                Vector3 left0 = new Vector3(-Mathf.Cos(q0.headingRad), 0f, Mathf.Sin(q0.headingRad));
                Vector3 left1 = new Vector3(-Mathf.Cos(q1.headingRad), 0f, Mathf.Sin(q1.headingRad));

                Vector3 c0 = new Vector3(q0.x, q0.y, q0.z);
                Vector3 c1 = new Vector3(q1.x, q1.y, q1.z);

                if (hasLeft)
                {
                    Vector3 inner0 = c0 + left0 * hw0 + Vector3.up * kerbBaseH;
                    Vector3 outer0 = c0 + left0 * (hw0 + kerbW) + Vector3.up * kerbTopH;
                    Vector3 inner1 = c1 + left1 * hw1 + Vector3.up * kerbBaseH;
                    Vector3 outer1 = c1 + left1 * (hw1 + kerbW) + Vector3.up * kerbTopH;

                    int idx = verts.Count;
                    verts.Add(inner0); uvs.Add(new Vector2(0f, s0 / 1.0f));
                    verts.Add(outer0); uvs.Add(new Vector2(1f, s0 / 1.0f));
                    verts.Add(inner1); uvs.Add(new Vector2(0f, s1 / 1.0f));
                    verts.Add(outer1); uvs.Add(new Vector2(1f, s1 / 1.0f));

                    tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 1);
                    tris.Add(idx + 1); tris.Add(idx + 2); tris.Add(idx + 3);
                }

                if (hasRight)
                {
                    Vector3 inner0 = c0 - left0 * hw0 + Vector3.up * kerbBaseH;
                    Vector3 outer0 = c0 - left0 * (hw0 + kerbW) + Vector3.up * kerbTopH;
                    Vector3 inner1 = c1 - left1 * hw1 + Vector3.up * kerbBaseH;
                    Vector3 outer1 = c1 - left1 * (hw1 + kerbW) + Vector3.up * kerbTopH;

                    int idx = verts.Count;
                    verts.Add(inner0); uvs.Add(new Vector2(0f, s0 / 1.0f));
                    verts.Add(outer0); uvs.Add(new Vector2(1f, s0 / 1.0f));
                    verts.Add(inner1); uvs.Add(new Vector2(0f, s1 / 1.0f));
                    verts.Add(outer1); uvs.Add(new Vector2(1f, s1 / 1.0f));

                    tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
                    tris.Add(idx + 1); tris.Add(idx + 3); tris.Add(idx + 2);
                }
            }

            if (verts.Count > 0)
            {
                CreateMeshObject(parent, "Kerbs", verts, uvs, tris, mat, chunkIdx, true, ref totalV, ref totalT);
            }
        }

        static bool IsKerbActive(float s0, float s1, List<KerbRange> ranges, bool isLeft)
        {
            float sMid = (s0 + s1) * 0.5f;
            foreach (var r in ranges)
            {
                if (r.isLeft == isLeft && sMid >= r.startS && sMid <= r.endS)
                    return true;
            }
            return false;
        }

        static void BuildRunoffAndGravel(GameObject parent, List<float> sList, TrackSpline spline, Material runoffMat, Material gravelMat, int chunkIdx, ref int totalV, ref int totalT)
        {
            var vertsRunoff = new List<Vector3>();
            var uvsRunoff = new List<Vector2>();
            var trisRunoff = new List<int>();

            var vertsGravel = new List<Vector3>();
            var uvsGravel = new List<Vector2>();
            var trisGravel = new List<int>();

            float kerbW = 1.5f;
            float runoffW = 5.0f; // 5m wide runoff tarmac
            float gravelW = 8.0f; // 8m wide gravel trap

            int numS = sList.Count;
            for (int i = 0; i < numS; i++)
            {
                float s = sList[i];
                var q = spline.QueryAtS(s);
                float halfW = (q.trackWidth > 0 ? q.trackWidth : 14.0f) * 0.5f;

                float rad = q.headingRad;
                Vector3 fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 left = new Vector3(-fwd.z, 0f, fwd.x);
                Vector3 center = new Vector3(q.x, q.y, q.z);

                // Outside of turn gets full runoff + gravel; inside gets compact runoff
                bool turningLeft = q.curvature > 0.005f;
                bool turningRight = q.curvature < -0.005f;

                float runoffLeft = turningRight ? runoffW : 3.0f;
                float runoffRight = turningLeft ? runoffW : 3.0f;
                float gravelLeft = turningRight ? gravelW : 4.0f;
                float gravelRight = turningLeft ? gravelW : 4.0f;

                // Left side Runoff & Gravel
                Vector3 lTrackEdge = center + left * (halfW + kerbW);
                Vector3 lRunoffEdge = lTrackEdge + left * runoffLeft;
                Vector3 lGravelEdge = lRunoffEdge + left * gravelLeft - Vector3.up * 0.02f;

                // Right side Runoff & Gravel
                Vector3 rTrackEdge = center - left * (halfW + kerbW);
                Vector3 rRunoffEdge = rTrackEdge - left * runoffRight;
                Vector3 rGravelEdge = rRunoffEdge - left * gravelRight - Vector3.up * 0.02f;

                float v = s / TEXTURE_REPEAT_M;

                // Left Runoff
                vertsRunoff.Add(lTrackEdge);  uvsRunoff.Add(new Vector2(0f, v));
                vertsRunoff.Add(lRunoffEdge); uvsRunoff.Add(new Vector2(1f, v));
                // Right Runoff
                vertsRunoff.Add(rTrackEdge);  uvsRunoff.Add(new Vector2(0f, v));
                vertsRunoff.Add(rRunoffEdge); uvsRunoff.Add(new Vector2(1f, v));

                // Left Gravel
                vertsGravel.Add(lRunoffEdge); uvsGravel.Add(new Vector2(0f, v));
                vertsGravel.Add(lGravelEdge); uvsGravel.Add(new Vector2(1f, v));
                // Right Gravel
                vertsGravel.Add(rRunoffEdge); uvsGravel.Add(new Vector2(0f, v));
                vertsGravel.Add(rGravelEdge); uvsGravel.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < numS - 1; i++)
            {
                int b0 = i * 4, b1 = (i + 1) * 4;

                // Left Runoff
                trisRunoff.Add(b0); trisRunoff.Add(b1); trisRunoff.Add(b0 + 1);
                trisRunoff.Add(b0 + 1); trisRunoff.Add(b1); trisRunoff.Add(b1 + 1);
                // Right Runoff
                trisRunoff.Add(b0 + 2); trisRunoff.Add(b0 + 3); trisRunoff.Add(b1 + 2);
                trisRunoff.Add(b0 + 3); trisRunoff.Add(b1 + 3); trisRunoff.Add(b1 + 2);

                // Left Gravel
                trisGravel.Add(b0); trisGravel.Add(b1); trisGravel.Add(b0 + 1);
                trisGravel.Add(b0 + 1); trisGravel.Add(b1); trisGravel.Add(b1 + 1);
                // Right Gravel
                trisGravel.Add(b0 + 2); trisGravel.Add(b0 + 3); trisGravel.Add(b1 + 2);
                trisGravel.Add(b0 + 3); trisGravel.Add(b1 + 3); trisGravel.Add(b1 + 2);
            }

            CreateMeshObject(parent, "Runoff_Tarmac", vertsRunoff, uvsRunoff, trisRunoff, runoffMat, chunkIdx, true, ref totalV, ref totalT);
            CreateMeshObject(parent, "Runoff_Gravel", vertsGravel, uvsGravel, trisGravel, gravelMat, chunkIdx, true, ref totalV, ref totalT);
        }

        static void BuildBarriers(GameObject parent, List<float> sList, TrackSpline spline, Material mat, int chunkIdx, ref int totalV, ref int totalT)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float barrierOffset = 18.5f; // Placed outside runoff & gravel
            float barrierHeight = 1.2f;
            int numS = sList.Count;

            for (int i = 0; i < numS; i++)
            {
                float s = sList[i];
                var q = spline.QueryAtS(s);
                float rad = q.headingRad;
                Vector3 fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 left = new Vector3(-fwd.z, 0f, fwd.x);
                Vector3 center = new Vector3(q.x, q.y, q.z);

                Vector3 bBaseL = center + left * barrierOffset;
                Vector3 bTopL = bBaseL + Vector3.up * barrierHeight;

                Vector3 bBaseR = center - left * barrierOffset;
                Vector3 bTopR = bBaseR + Vector3.up * barrierHeight;

                float v = s / 5.0f;
                verts.Add(bBaseL); uvs.Add(new Vector2(0f, v));
                verts.Add(bTopL);  uvs.Add(new Vector2(1f, v));
                verts.Add(bBaseR); uvs.Add(new Vector2(0f, v));
                verts.Add(bTopR);  uvs.Add(new Vector2(1f, v));
            }

            for (int i = 0; i < numS - 1; i++)
            {
                int b0 = i * 4, b1 = (i + 1) * 4;
                // Left barrier wall
                tris.Add(b0); tris.Add(b0 + 1); tris.Add(b1);
                tris.Add(b1); tris.Add(b0 + 1); tris.Add(b1 + 1);
                // Right barrier wall
                tris.Add(b0 + 2); tris.Add(b1 + 2); tris.Add(b0 + 3);
                tris.Add(b0 + 3); tris.Add(b1 + 2); tris.Add(b1 + 3);
            }

            CreateMeshObject(parent, "Barriers", verts, uvs, tris, mat, chunkIdx, true, ref totalV, ref totalT);
        }

        static void BuildStartFinishGantry(GameObject parent, TrackSpline spline, Material checkMat, Material frameMat)
        {
            var gantryRoot = new GameObject("Gantry_StartFinish");
            gantryRoot.transform.SetParent(parent.transform, false);

            var q = spline.QueryAtS(0f);
            float rad = q.headingRad;
            Vector3 fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Vector3 left = new Vector3(-fwd.z, 0f, fwd.x);
            Vector3 center = new Vector3(q.x, q.y, q.z);

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "GantryBeam";
            beam.transform.SetParent(gantryRoot.transform, false);
            beam.transform.position = center + Vector3.up * 6.5f;
            beam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            beam.transform.localScale = new Vector3(18f, 1.5f, 1.2f);
            beam.GetComponent<Renderer>().sharedMaterial = checkMat;

            var pL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pL.name = "PillarLeft";
            pL.transform.SetParent(gantryRoot.transform, false);
            pL.transform.position = center + left * 9.5f + Vector3.up * 3.5f;
            pL.transform.localScale = new Vector3(0.8f, 3.5f, 0.8f);
            pL.GetComponent<Renderer>().sharedMaterial = frameMat;

            var pR = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pR.name = "PillarRight";
            pR.transform.SetParent(gantryRoot.transform, false);
            pR.transform.position = center - left * 9.5f + Vector3.up * 3.5f;
            pR.transform.localScale = new Vector3(0.8f, 3.5f, 0.8f);
            pR.GetComponent<Renderer>().sharedMaterial = frameMat;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // NEW TERRAIN: Regular Height Grid with Lower-Envelope & Smoothed Relief
        // ─────────────────────────────────────────────────────────────────────────────
        static void BuildGridTerrain(GameObject parent, TrackConfig config, TrackSpline spline, Material grassMat, ref int totalV, ref int totalT)
        {
            var samples = config.samples;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float minY = float.MaxValue;

            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (s.x < minX) minX = s.x; if (s.x > maxX) maxX = s.x;
                if (s.z < minZ) minZ = s.z; if (s.z > maxZ) maxZ = s.z;
                if (s.y < minY) minY = s.y;
            }

            minX -= TERRAIN_MARGIN; maxX += TERRAIN_MARGIN;
            minZ -= TERRAIN_MARGIN; maxZ += TERRAIN_MARGIN;
            float baseGroundY = minY - 3.0f;

            // Build 2D spatial hash of track samples
            float bucketSize = 25.0f;
            var buckets = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                int bx = Mathf.FloorToInt(s.x / bucketSize);
                int bz = Mathf.FloorToInt(s.z / bucketSize);
                var key = (bx, bz);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    buckets[key] = list;
                }
                list.Add(i);
            }

            int nx = Mathf.CeilToInt((maxX - minX) / TERRAIN_CELL_SIZE) + 1;
            int nz = Mathf.CeilToInt((maxZ - minZ) / TERRAIN_CELL_SIZE) + 1;

            float[,] gridH = new float[nz, nx];
            float[,] ceilingH = new float[nz, nx];

            float searchRadius = 100.0f;
            int searchCells = Mathf.CeilToInt(searchRadius / bucketSize);

            // Compute lower envelope for every grid vertex
            for (int iz = 0; iz < nz; iz++)
            {
                float z = minZ + iz * TERRAIN_CELL_SIZE;
                int bz = Mathf.FloorToInt(z / bucketSize);

                for (int ix = 0; ix < nx; ix++)
                {
                    float x = minX + ix * TERRAIN_CELL_SIZE;
                    int bx = Mathf.FloorToInt(x / bucketSize);

                    float minCand = float.MaxValue;
                    bool hasNearSample = false;

                    for (int dx = -searchCells; dx <= searchCells; dx++)
                    {
                        for (int dz = -searchCells; dz <= searchCells; dz++)
                        {
                            if (!buckets.TryGetValue((bx + dx, bz + dz), out var cellList)) continue;

                            for (int ci = 0; ci < cellList.Count; ci++)
                            {
                                int sIdx = cellList[ci];
                                var smp = samples[sIdx];
                                float dist = Mathf.Sqrt((x - smp.x) * (x - smp.x) + (z - smp.z) * (z - smp.z));
                                if (dist > searchRadius) continue;

                                hasNearSample = true;
                                float halfW = (smp.width > 0 ? smp.width : 14.0f) * 0.5f;
                                float bandRadius = halfW + 13.5f; // halfW + kerb(1.5) + runoff(8) + 4
                                float distEdge = Mathf.Max(0f, dist - halfW);

                                float cand;
                                if (dist <= bandRadius)
                                {
                                    // Strictly at least 3 cm (and comfortably 6-10cm) below road
                                    cand = smp.y - 0.06f - 0.05f * (dist / bandRadius);
                                }
                                else if (distEdge <= 20.0f)
                                {
                                    // Within 20m of road edge: slopes away <= 8 deg, never rises above road level
                                    cand = smp.y - 0.06f - 0.05f * (distEdge / 20.0f);
                                }
                                else
                                {
                                    // Farther out: gentle rolling hills (slope <= 25 deg, max height delta 12m)
                                    float past20 = distEdge - 20.0f;
                                    float hillRise = Mathf.Min(past20 * 0.18f, 12.0f);
                                    cand = smp.y - 0.11f + hillRise;
                                }

                                if (cand < minCand) minCand = cand;
                            }
                        }
                    }

                    float finalH = hasNearSample ? minCand : baseGroundY;
                    gridH[iz, ix] = finalH;
                    ceilingH[iz, ix] = finalH;
                }
            }

            // 2 passes of smoothing, strictly clamped by ceiling limit (cannot lift vertex above road band)
            for (int pass = 0; pass < 2; pass++)
            {
                float[,] smoothed = (float[,])gridH.Clone();
                for (int iz = 1; iz < nz - 1; iz++)
                {
                    for (int ix = 1; ix < nx - 1; ix++)
                    {
                        float avg = 0.5f * gridH[iz, ix] + 0.125f * (gridH[iz - 1, ix] + gridH[iz + 1, ix] + gridH[iz, ix - 1] + gridH[iz, ix + 1]);
                        smoothed[iz, ix] = Mathf.Min(avg, ceilingH[iz, ix]);
                    }
                }
                gridH = smoothed;
            }

            // Invisible check: verify no vertex inside road band violates -0.03m constraint
            float maxDiffInsideBand = float.MinValue;
            for (int i = 0; i < samples.Count; i++)
            {
                var smp = samples[i];
                float halfW = (smp.width > 0 ? smp.width : 14.0f) * 0.5f;
                float bandR = halfW + 13.5f;

                int ixMin = Mathf.Clamp(Mathf.FloorToInt((smp.x - bandR - minX) / TERRAIN_CELL_SIZE), 0, nx - 1);
                int ixMax = Mathf.Clamp(Mathf.CeilToInt((smp.x + bandR - minX) / TERRAIN_CELL_SIZE), 0, nx - 1);
                int izMin = Mathf.Clamp(Mathf.FloorToInt((smp.z - bandR - minZ) / TERRAIN_CELL_SIZE), 0, nz - 1);
                int izMax = Mathf.Clamp(Mathf.CeilToInt((smp.z + bandR - minZ) / TERRAIN_CELL_SIZE), 0, nz - 1);

                for (int iz = izMin; iz <= izMax; iz++)
                {
                    float z = minZ + iz * TERRAIN_CELL_SIZE;
                    for (int ix = ixMin; ix <= ixMax; ix++)
                    {
                        float x = minX + ix * TERRAIN_CELL_SIZE;
                        float d = Mathf.Sqrt((x - smp.x) * (x - smp.x) + (z - smp.z) * (z - smp.z));
                        if (d <= bandR)
                        {
                            float diff = gridH[iz, ix] - smp.y;
                            if (diff > maxDiffInsideBand) maxDiffInsideBand = diff;
                        }
                    }
                }
            }
            Debug.Log($"[TrackMeshBaker] Terrain Road-Band Max Height Difference: {maxDiffInsideBand:F4} m (Must be <= -0.03 m).");

            // Partition grid into ~200m spatial chunks and create meshes
            var terrainRoot = new GameObject("Terrain_Grid");
            terrainRoot.transform.SetParent(parent.transform, false);

            int cellsPerChunk = Mathf.RoundToInt(TERRAIN_CHUNK_SIZE / TERRAIN_CELL_SIZE);
            int chunksX = Mathf.CeilToInt((float)(nx - 1) / cellsPerChunk);
            int chunksZ = Mathf.CeilToInt((float)(nz - 1) / cellsPerChunk);

            int chunkCount = 0;
            for (int cz = 0; cz < chunksZ; cz++)
            {
                int startIz = cz * cellsPerChunk;
                int endIz = Mathf.Min(startIz + cellsPerChunk, nz - 1);

                for (int cx = 0; cx < chunksX; cx++)
                {
                    int startIx = cx * cellsPerChunk;
                    int endIx = Mathf.Min(startIx + cellsPerChunk, nx - 1);

                    var verts = new List<Vector3>();
                    var uvs = new List<Vector2>();
                    var tris = new List<int>();

                    int chunkNx = (endIx - startIx) + 1;
                    int chunkNz = (endIz - startIz) + 1;

                    for (int iz = startIz; iz <= endIz; iz++)
                    {
                        float z = minZ + iz * TERRAIN_CELL_SIZE;
                        for (int ix = startIx; ix <= endIx; ix++)
                        {
                            float x = minX + ix * TERRAIN_CELL_SIZE;
                            float y = gridH[iz, ix];

                            verts.Add(new Vector3(x, y, z));
                            // World-space UV mapping avoids stretching and diagonal seam artifacts
                            uvs.Add(new Vector2(x / 25.0f, z / 25.0f));
                        }
                    }

                    for (int lz = 0; lz < chunkNz - 1; lz++)
                    {
                        for (int lx = 0; lx < chunkNx - 1; lx++)
                        {
                            int i0 = lz * chunkNx + lx;
                            int i1 = i0 + 1;
                            int i2 = (lz + 1) * chunkNx + lx;
                            int i3 = i2 + 1;

                            tris.Add(i0); tris.Add(i2); tris.Add(i1);
                            tris.Add(i1); tris.Add(i2); tris.Add(i3);
                        }
                    }

                    if (verts.Count > 0)
                    {
                        string chunkName = $"Terrain_Chunk_{chunkCount:D2}";
                        CreateTerrainMeshObject(terrainRoot, chunkName, verts, uvs, tris, grassMat, chunkCount, ref totalV, ref totalT);
                        chunkCount++;
                    }
                }
            }
        }

        static void CreateTerrainMeshObject(GameObject parent, string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris, Material mat, int chunkIdx, ref int totalV, ref int totalT)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.indexFormat = (verts.Count > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string assetPath = $"Assets/Generated/Track/Meshes/{mesh.name}.asset";
            AssetDatabase.CreateAsset(mesh, assetPath);

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.isStatic = true;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            // Disable shadow casting on terrain to completely eliminate hard diagonal shadow streaks on mobile
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            totalV += verts.Count;
            totalT += tris.Count / 3;
        }

        static void CreateMeshObject(GameObject parent, string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris, Material mat, int chunkIdx, bool addCollider, ref int totalV, ref int totalT)
        {
            if (verts.Count == 0 || tris.Count == 0) return;

            Mesh mesh = new Mesh();
            mesh.name = $"{name}_Chunk{chunkIdx:D2}";
            mesh.indexFormat = (verts.Count > 65000) ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string assetPath = $"Assets/Generated/Track/Meshes/{mesh.name}.asset";
            AssetDatabase.CreateAsset(mesh, assetPath);

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.isStatic = true;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            if (addCollider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }

            totalV += verts.Count;
            totalT += tris.Count / 3;
        }

        static void EnsureFolder(string fullPath)
        {
            if (AssetDatabase.IsValidFolder(fullPath)) return;
            string parent = Path.GetDirectoryName(fullPath).Replace('\\', '/');
            string child = Path.GetFileName(fullPath);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
