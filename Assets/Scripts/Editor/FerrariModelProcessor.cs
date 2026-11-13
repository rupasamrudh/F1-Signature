using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    public class FerrariModelProcessor : EditorWindow
    {
        [MenuItem("Tools/F1 Signature/Process Ferrari SF-26 Model")]
        public static void ShowWindow()
        {
            GetWindow<FerrariModelProcessor>("Ferrari Processor");
        }

        private void OnGUI()
        {
            GUILayout.Label("Ferrari SF-26 Model Processor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Processes Ferrari SF-26 GLB into native Unity assets:\n" +
                "- 4 independent wheels (Wheel_FL, Wheel_FR, Wheel_RL, Wheel_RR) centered at hub pivots\n" +
                "- Chassis body oriented horizontally forward (+Z)\n" +
                "- Direct assignment of all 33 URP Lit Ferrari materials and textures\n" +
                "- Outputs Assets/Prefabs/Vehicles/Ferrari_SF26.prefab",
                MessageType.Info);

            if (GUILayout.Button("1. Process & Separate Wheels (Auto-Builds Prefab)", GUILayout.Height(36)))
            {
                ProcessModel();
            }

            if (GUILayout.Button("2. Rebuild Ferrari_SF26 Prefab", GUILayout.Height(36)))
            {
                BuildPrefab();
            }

            if (GUILayout.Button("3. Rebuild Track Scene & Validate", GUILayout.Height(36)))
            {
                IntegrateWithScene();
            }
        }

        // Coordinate conversion:
        // Raw GLB:
        //   X = lateral
        //   Y = longitudinal (+Y is nose forward, -Y is rear wing)
        //   Z = vertical (-Z is roll hoop/airbox, +Z is ground)
        // In Unity:
        //   X_unity = X_raw
        //   Y_unity = -Z_raw - 0.334f (axle center at Y = 0, ground at Y = -0.334m)
        //   Z_unity = Y_raw
        public const float AXLE_Z_OFFSET = -0.334f;

        public static Vector3 ToUnityPoint(Vector3 vRaw, Vector3 pivotOffset)
        {
            Vector3 vUnity = new Vector3(vRaw.x, -vRaw.z - 0.334f, vRaw.y);
            return vUnity - pivotOffset;
        }

        public static Vector3 ToUnityVector(Vector3 vRaw)
        {
            return new Vector3(vRaw.x, -vRaw.z, vRaw.y);
        }

        public static Vector4 ToUnityTangent(Vector4 tRaw)
        {
            return new Vector4(tRaw.x, -tRaw.z, tRaw.y, tRaw.w);
        }

        public class ProcessedGroup
        {
            public Mesh mesh;
            public string[] materialNames;
        }

        public static void ProcessModel()
        {
            FerrariMaterialSetup.CreateOrUpdateMaterials();

            string glbPath = "Assets/Models/F1 Car/2026-ferrari-sf-26/source/f1_2026_sf26.glb";
            var glbGo = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (glbGo == null)
            {
                EditorUtility.DisplayDialog("Error", $"Could not load {glbPath}. Ensure glTFast is loaded in Unity.", "OK");
                return;
            }

            var mf = glbGo.GetComponentInChildren<MeshFilter>();
            var mr = glbGo.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find MeshFilter with sharedMesh on imported GLB.", "OK");
                return;
            }

            Mesh srcMesh = mf.sharedMesh;
            Material[] srcMaterials = mr != null ? mr.sharedMaterials : new Material[srcMesh.subMeshCount];

            Debug.Log($"[FerrariProcessor] Source Mesh has {srcMesh.vertexCount} verts, {srcMesh.subMeshCount} submeshes.");

            string outDir = "Assets/Models/F1 Car/2026-ferrari-sf-26/Processed";
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            // Wheel axle centers in Unity coordinates:
            Vector3 centerFL = new Vector3(0.799f, 0.002f, 1.522f);
            Vector3 centerFR = new Vector3(-0.800f, 0.005f, 1.522f);
            Vector3 centerRL = new Vector3(0.763f, -0.003f, -2.011f);
            Vector3 centerRR = new Vector3(-0.763f, -0.002f, -2.011f);

            // Body mesh (excluding wheel primitives 3, 4, 5, 6)
            var bodyGroup = ExtractSubmeshGroup(srcMesh, srcMaterials, (submeshIdx, triCenterUnity) =>
            {
                if (submeshIdx >= 3 && submeshIdx <= 6) return false;
                return true;
            }, Vector3.zero, "Ferrari_SF26_Body");

            // Extract the 4 independent wheels
            var wheelFLGroup = ExtractWheelMesh(srcMesh, srcMaterials, centerFL, 1, 1, "Ferrari_SF26_Wheel_FL");
            var wheelFRGroup = ExtractWheelMesh(srcMesh, srcMaterials, centerFR, -1, 1, "Ferrari_SF26_Wheel_FR");
            var wheelRLGroup = ExtractWheelMesh(srcMesh, srcMaterials, centerRL, 1, -1, "Ferrari_SF26_Wheel_RL");
            var wheelRRGroup = ExtractWheelMesh(srcMesh, srcMaterials, centerRR, -1, -1, "Ferrari_SF26_Wheel_RR");

            AssetDatabase.CreateAsset(bodyGroup.mesh, $"{outDir}/Ferrari_SF26_Body.asset");
            AssetDatabase.CreateAsset(wheelFLGroup.mesh, $"{outDir}/Ferrari_SF26_Wheel_FL.asset");
            AssetDatabase.CreateAsset(wheelFRGroup.mesh, $"{outDir}/Ferrari_SF26_Wheel_FR.asset");
            AssetDatabase.CreateAsset(wheelRLGroup.mesh, $"{outDir}/Ferrari_SF26_Wheel_RL.asset");
            AssetDatabase.CreateAsset(wheelRRGroup.mesh, $"{outDir}/Ferrari_SF26_Wheel_RR.asset");

            SaveMaterialMapping($"{outDir}/body_mats.txt", bodyGroup.materialNames);
            SaveMaterialMapping($"{outDir}/wheel_fl_mats.txt", wheelFLGroup.materialNames);
            SaveMaterialMapping($"{outDir}/wheel_fr_mats.txt", wheelFRGroup.materialNames);
            SaveMaterialMapping($"{outDir}/wheel_rl_mats.txt", wheelRLGroup.materialNames);
            SaveMaterialMapping($"{outDir}/wheel_rr_mats.txt", wheelRRGroup.materialNames);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[FerrariProcessor] Meshes extracted. Building prefab...");
            BuildPrefab();

            EditorUtility.DisplayDialog("Success", "Ferrari SF-26 mesh processing & prefab generation complete!\nAll materials and textures assigned.", "OK");
        }

        static ProcessedGroup ExtractWheelMesh(Mesh src, Material[] srcMats, Vector3 centerUnity, int signX, int signZ, string name)
        {
            return ExtractSubmeshGroup(src, srcMats, (submeshIdx, triCenterUnity) =>
            {
                // Wheel primitives: 3 (discs), 4 (sides), 5 (hub), 6 (tread)
                bool isWheelSubmesh = (submeshIdx >= 3 && submeshIdx <= 6);
                if (!isWheelSubmesh) return false;

                bool matchesQuadrant = (Mathf.Sign(triCenterUnity.x) == signX) && (Mathf.Sign(triCenterUnity.z) == signZ);
                return matchesQuadrant;
            }, centerUnity, name);
        }

        static ProcessedGroup ExtractSubmeshGroup(Mesh src, Material[] srcMats, Func<int, Vector3, bool> triangleFilter, Vector3 pivotOffset, string name)
        {
            Vector3[] srcVerts = src.vertices;
            Vector3[] srcNormals = src.normals;
            Vector4[] srcTangents = src.tangents;
            Vector2[] srcUv = src.uv;

            List<Vector3> newVerts = new List<Vector3>();
            List<Vector3> newNormals = new List<Vector3>();
            List<Vector4> newTangents = new List<Vector4>();
            List<Vector2> newUv = new List<Vector2>();

            Dictionary<int, int> vertMap = new Dictionary<int, int>();
            List<List<int>> newSubmeshes = new List<List<int>>();
            List<string> usedMaterialNames = new List<string>();

            for (int subIdx = 0; subIdx < src.subMeshCount; subIdx++)
            {
                int[] tris = src.GetTriangles(subIdx);
                List<int> validTris = new List<int>();

                for (int t = 0; t < tris.Length; t += 3)
                {
                    int i0 = tris[t];
                    int i1 = tris[t + 1];
                    int i2 = tris[t + 2];

                    Vector3 v0 = ToUnityPoint(srcVerts[i0], Vector3.zero);
                    Vector3 v1 = ToUnityPoint(srcVerts[i1], Vector3.zero);
                    Vector3 v2 = ToUnityPoint(srcVerts[i2], Vector3.zero);
                    Vector3 triCenterUnity = (v0 + v1 + v2) / 3f;

                    if (triangleFilter(subIdx, triCenterUnity))
                    {
                        validTris.Add(GetOrAddVert(i0));
                        validTris.Add(GetOrAddVert(i1));
                        validTris.Add(GetOrAddVert(i2));
                    }
                }

                if (validTris.Count > 0)
                {
                    newSubmeshes.Add(validTris);
                    string matName = (srcMats != null && subIdx < srcMats.Length && srcMats[subIdx] != null)
                        ? srcMats[subIdx].name
                        : $"Material_{subIdx}";
                    usedMaterialNames.Add(matName);
                }
            }

            int GetOrAddVert(int srcIdx)
            {
                if (vertMap.TryGetValue(srcIdx, out int existing)) return existing;
                int newIdx = newVerts.Count;
                newVerts.Add(ToUnityPoint(srcVerts[srcIdx], pivotOffset));
                if (srcNormals != null && srcNormals.Length > srcIdx)
                    newNormals.Add(ToUnityVector(srcNormals[srcIdx]));
                if (srcTangents != null && srcTangents.Length > srcIdx)
                    newTangents.Add(ToUnityTangent(srcTangents[srcIdx]));
                if (srcUv != null && srcUv.Length > srcIdx)
                    newUv.Add(srcUv[srcIdx]);
                vertMap[srcIdx] = newIdx;
                return newIdx;
            }

            Mesh res = new Mesh();
            res.name = name;
            if (newVerts.Count > 65535) res.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            res.SetVertices(newVerts);
            if (newNormals.Count > 0) res.SetNormals(newNormals);
            if (newTangents.Count > 0) res.SetTangents(newTangents);
            if (newUv.Count > 0) res.SetUVs(0, newUv);

            res.subMeshCount = newSubmeshes.Count;
            for (int i = 0; i < newSubmeshes.Count; i++)
            {
                res.SetTriangles(newSubmeshes[i], i);
            }

            res.RecalculateBounds();

            return new ProcessedGroup
            {
                mesh = res,
                materialNames = usedMaterialNames.ToArray()
            };
        }

        public static void BuildPrefab()
        {
            string outDir = "Assets/Models/F1 Car/2026-ferrari-sf-26/Processed";
            string prefabDir = "Assets/Prefabs/Vehicles";
            if (!Directory.Exists(prefabDir)) Directory.CreateDirectory(prefabDir);

            Mesh bodyMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Body.asset");
            Mesh wheelFL = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_FL.asset");
            Mesh wheelFR = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_FR.asset");
            Mesh wheelRL = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_RL.asset");
            Mesh wheelRR = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_RR.asset");

            if (bodyMesh == null || wheelFL == null)
            {
                Debug.Log("[FerrariProcessor] Processed meshes missing. Running ProcessModel first...");
                ProcessModel();
                bodyMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Body.asset");
                wheelFL = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_FL.asset");
                wheelFR = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_FR.asset");
                wheelRL = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_RL.asset");
                wheelRR = AssetDatabase.LoadAssetAtPath<Mesh>($"{outDir}/Ferrari_SF26_Wheel_RR.asset");
            }

            Material[] bodyMats = LoadMaterialsForGroup($"{outDir}/body_mats.txt", bodyMesh.subMeshCount);
            Material[] wheelFlMats = LoadMaterialsForGroup($"{outDir}/wheel_fl_mats.txt", wheelFL.subMeshCount);
            Material[] wheelFrMats = LoadMaterialsForGroup($"{outDir}/wheel_fr_mats.txt", wheelFR.subMeshCount);
            Material[] wheelRlMats = LoadMaterialsForGroup($"{outDir}/wheel_rl_mats.txt", wheelRL.subMeshCount);
            Material[] wheelRrMats = LoadMaterialsForGroup($"{outDir}/wheel_rr_mats.txt", wheelRR.subMeshCount);

            GameObject root = new GameObject("Ferrari_SF26");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // Body Child
            GameObject bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root.transform, false);
            bodyGo.transform.localPosition = Vector3.zero;
            bodyGo.transform.localRotation = Quaternion.identity;
            bodyGo.transform.localScale = Vector3.one;
            var bodyMf = bodyGo.AddComponent<MeshFilter>();
            bodyMf.sharedMesh = bodyMesh;
            var bodyMr = bodyGo.AddComponent<MeshRenderer>();
            bodyMr.sharedMaterials = bodyMats;

            // Wheel Children
            Vector3 centerFL = new Vector3(0.799f, 0.002f, 1.522f);
            Vector3 centerFR = new Vector3(-0.800f, 0.005f, 1.522f);
            Vector3 centerRL = new Vector3(0.763f, -0.003f, -2.011f);
            Vector3 centerRR = new Vector3(-0.763f, -0.002f, -2.011f);

            CreateWheelChild(root.transform, "Wheel_FL", wheelFL, centerFL, wheelFlMats);
            CreateWheelChild(root.transform, "Wheel_FR", wheelFR, centerFR, wheelFrMats);
            CreateWheelChild(root.transform, "Wheel_RL", wheelRL, centerRL, wheelRlMats);
            CreateWheelChild(root.transform, "Wheel_RR", wheelRR, centerRR, wheelRrMats);

            string prefabPath = $"{prefabDir}/Ferrari_SF26.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FerrariProcessor] Successfully created Ferrari_SF26 prefab at {prefabPath}!");
        }

        static void CreateWheelChild(Transform parent, string name, Mesh m, Vector3 localPos, Material[] mats)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
        }

        static void SaveMaterialMapping(string path, string[] names)
        {
            File.WriteAllLines(path, names);
        }

        static Material[] LoadMaterialsForGroup(string path, int expectedCount)
        {
            string matDir = "Assets/Materials/Ferrari";
            List<Material> mats = new List<Material>();

            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    string matPath = $"{matDir}/{trimmed}.mat";
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (m != null) mats.Add(m);
                }
            }

            while (mats.Count < expectedCount)
            {
                mats.Add(AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/sf25_paints.mat"));
            }

            return mats.ToArray();
        }

        public static void IntegrateWithScene()
        {
            FerrariMaterialSetup.CreateOrUpdateMaterials();
            BuildPrefab();
            F1Sim.Editor.TrackSceneBuilder.RebuildTrackSceneMenu();
            EditorUtility.DisplayDialog("Complete", "Ferrari SF-26 prefab built and integrated into Track scene!", "OK");
        }

        public static void BatchUpdateAll()
        {
            FerrariMaterialSetup.CreateOrUpdateMaterials();
            BuildPrefab();
            F1Sim.Editor.TrackSceneBuilder.RebuildTrackSceneMenu();
            Debug.Log("[FerrariProcessor] Batch update complete!");
        }
    }
}
