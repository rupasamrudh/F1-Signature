using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1Sim.EditorTools
{
    [InitializeOnLoad]
    public static class InterlagosTrackProcessor
    {
        public static bool includeScenery = false; // City is permanently excluded in Python GLB processor
        public static bool includeVegetation = true; // Infield trees and bushes
        // Trigger build v3 (authentic track with original walls, barriers, runoffs, shortcut tracks, grass/gravel, trackside)

        static InterlagosTrackProcessor()
        {
            EditorApplication.delayCall += () =>
            {
                string prefabPath = "Assets/Generated/Track/Track.prefab";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                // If prefab missing or missing Track_Trackside / Track_Terrain, rebuild
                if (go == null || go.transform.Find("Track_Road") == null || go.transform.Find("Track_Trackside") == null)
                {
                    Debug.Log("[InterlagosTrackProcessor] 3D Track prefab missing or outdated. Building centered 3D Interlagos track with authentic runoffs, walls, and trackside...");
                    BuildTrackInternal();
                }
            };
        }

        [MenuItem("Tools/F1 Signature/Build 3D Interlagos Track (Only Track)")]
        public static void BuildTrackOnly()
        {
            includeScenery = false;
            includeVegetation = false;
            BuildTrackInternal();
        }

        [MenuItem("Tools/F1 Signature/Build 3D Interlagos Track (With Scenery)")]
        public static void BuildTrackWithScenery()
        {
            includeScenery = true;
            includeVegetation = true;
            BuildTrackInternal();
        }

        [MenuItem("Tools/F1 Signature/Build 3D Interlagos Track")]
        public static void BuildTrack()
        {
            BuildTrackOnly();
        }

        public static void BuildTrackInternal()
        {
            Debug.Log($"[InterlagosTrackProcessor] Building 3D Track (includeScenery={includeScenery}, includeVegetation={includeVegetation})...");

            // 1. Setup materials
            InterlagosMaterialSetup.CreateOrUpdateMaterials();

            string dataDir = "Assets/Generated/Track/InterlagosData";
            string meshDir = "Assets/Generated/Track/InterlagosMeshes";
            string matDir = "Assets/Materials/Track/Interlagos";
            string prefabDir = "Assets/Generated/Track";

            if (!Directory.Exists(meshDir)) Directory.CreateDirectory(meshDir);
            if (!Directory.Exists(prefabDir)) Directory.CreateDirectory(prefabDir);

            // 2. Categories to process
            var categories = new List<(string name, bool hasCollider, bool castShadows)>();
            categories.Add(("Track_Road", true, true));
            categories.Add(("Track_Kerbs", true, true));
            categories.Add(("Track_Barriers", true, true));
            categories.Add(("Track_Terrain", true, false)); // Infield grass, verge, gravel & terrain (solid collider for off-track runoffs)
            categories.Add(("Track_Trackside", true, true)); // Distance markers, gantries, marshal huts, pit structures, grandstands

            if (includeVegetation) categories.Add(("Track_Vegetation", false, false));
            if (includeScenery) categories.Add(("Track_Scenery", false, true));

            GameObject root = new GameObject("Track");

            foreach (var (catName, hasCollider, castShadows) in categories)
            {
                string binPath = $"{dataDir}/{catName}.bin";
                if (!File.Exists(binPath))
                {
                    Debug.LogWarning($"[InterlagosTrackProcessor] Binary file {binPath} missing! Skipping {catName}.");
                    continue;
                }

                Debug.Log($"[InterlagosTrackProcessor] Loading {catName} from {binPath}...");
                Mesh mesh = new Mesh();
                mesh.name = catName;
                mesh.indexFormat = IndexFormat.UInt32;

                List<Material> submeshMaterials = new List<Material>();

                using (var br = new BinaryReader(File.OpenRead(binPath)))
                {
                    uint magic = br.ReadUInt32();
                    if (magic != 0x46315452)
                    {
                        Debug.LogError($"[InterlagosTrackProcessor] Invalid magic in {binPath}!");
                        continue;
                    }

                    int vCount = br.ReadInt32();
                    int smCount = br.ReadInt32();

                    Vector3[] verts = new Vector3[vCount];
                    Vector3[] norms = new Vector3[vCount];
                    Vector2[] uvs = new Vector2[vCount];

                    for (int i = 0; i < vCount; i++)
                    {
                        verts[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        norms[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        uvs[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                    }

                    mesh.SetVertices(verts);
                    mesh.SetNormals(norms);
                    mesh.SetUVs(0, uvs);
                    mesh.subMeshCount = smCount;

                    for (int s = 0; s < smCount; s++)
                    {
                        int nameLen = br.ReadInt32();
                        byte[] nameBytes = br.ReadBytes(nameLen);
                        string rawMatName = System.Text.Encoding.UTF8.GetString(nameBytes);
                        string cleanMatName = rawMatName.Replace('/', '_').Replace('\\', '_');

                        int idxCount = br.ReadInt32();
                        int[] tris = new int[idxCount];
                        for (int j = 0; j < idxCount; j++)
                        {
                            tris[j] = br.ReadInt32();
                        }

                        mesh.SetTriangles(tris, s);

                        // Load material
                        string matPath = $"{matDir}/{cleanMatName}.mat";
                        Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if (m == null)
                        {
                            m = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/roada.008.mat");
                        }
                        submeshMaterials.Add(m);
                    }
                }

                mesh.RecalculateBounds();

                // Save mesh asset
                string meshAssetPath = $"{meshDir}/{catName}.asset";
                AssetDatabase.CreateAsset(mesh, meshAssetPath);

                // Create GameObject
                GameObject child = new GameObject(catName);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = Vector3.one;

                var mf = child.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterials = submeshMaterials.ToArray();
                mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

                if (hasCollider)
                {
                    var col = child.AddComponent<MeshCollider>();
                    col.sharedMesh = mesh;
                    col.convex = false;
                }
            }

            // Save prefab
            string prefabPath = $"{prefabDir}/Track.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            GameObject.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[InterlagosTrackProcessor] Successfully created 3D Track prefab at {prefabPath}!");

            // Rebuild scene with new track
            F1Sim.Editor.TrackSceneBuilder.RebuildTrackSceneMenu();
        }
    }
}
