using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    public static class ModelInspectionHelper
    {
        [MenuItem("Tools/F1 Signature/Inspect Mercedes Model")]
        public static void InspectModel()
        {
            string glbPath = "Assets/Models/F1 Car/2026-mercedes-w17/source/f1_2026_w17.glb";
            AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (prefab == null)
            {
                Debug.LogError($"[ModelInspection] Failed to load prefab at {glbPath}. Check if glTFast is installed!");
                return;
            }

            Debug.Log($"[ModelInspection] Successfully loaded {prefab.name}!");
            MeshFilter[] mfs = prefab.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] mrs = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Debug.Log($"[ModelInspection] Child MeshFilters: {mfs.Length}, MeshRenderers: {mrs.Length}");

            foreach (var mf in mfs)
            {
                Mesh m = mf.sharedMesh;
                string mName = m != null ? m.name : "null";
                int submeshCount = m != null ? m.subMeshCount : 0;
                Debug.Log($"  GameObject: '{mf.gameObject.name}', Mesh: '{mName}', Submeshes: {submeshCount}, Bounds: {m.bounds}");
            }
        }
    }
}

