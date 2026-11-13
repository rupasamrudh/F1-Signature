using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    public static class TestLoadGLB
    {
        [MenuItem("Tools/F1 Signature/Test Load Interlagos GLB")]
        public static void Run()
        {
            string glbPath = "Assets/Models/Track/Interlagos/source/interlagos.glb";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (go == null)
            {
                Debug.LogError($"[TestLoadGLB] Could not load GameObject at {glbPath}");
                return;
            }

            Debug.Log($"[TestLoadGLB] Loaded {go.name} successfully! Children: {go.transform.childCount}");
            PrintHierarchy(go.transform, 0);
        }

        static void PrintHierarchy(Transform t, int indent)
        {
            string pad = new string(' ', indent * 2);
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            string meshInfo = mf != null && mf.sharedMesh != null 
                ? $"Mesh: {mf.sharedMesh.name}, verts: {mf.sharedMesh.vertexCount}, submeshes: {mf.sharedMesh.subMeshCount}"
                : "No MeshFilter";
            string matInfo = mr != null ? $"Mats: {mr.sharedMaterials.Length}" : "No MeshRenderer";
            Debug.Log($"{pad}- {t.name} (pos={t.localPosition}, rot={t.localEulerAngles}, scale={t.localScale}) [{meshInfo}] [{matInfo}]");

            for (int i = 0; i < t.childCount; i++)
            {
                PrintHierarchy(t.GetChild(i), indent + 1);
            }
        }
    }
}

