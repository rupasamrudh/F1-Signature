using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    public static class InspectImportedGLB
    {
        public static void Run()
        {
            string glbPath = "Assets/Models/F1 Car/2026-mercedes-w17/source/f1_2026_w17.glb";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (go == null)
            {
                Debug.LogError("[InspectGLB] Could not load GameObject at " + glbPath);
                return;
            }

            Debug.Log($"[InspectGLB] Loaded {go.name} successfully!");
            var mfs = go.GetComponentsInChildren<MeshFilter>(true);
            Debug.Log($"[InspectGLB] MeshFilters found: {mfs.Length}");
            foreach (var mf in mfs)
            {
                var m = mf.sharedMesh;
                Debug.Log($"  MF: {mf.name}, Mesh: {m.name}, subMeshes: {m.subMeshCount}, verts: {m.vertexCount}");
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Debug.Log($"  MR materials: {mr.sharedMaterials.Length}");
                    for (int i = 0; i < mr.sharedMaterials.Length; i++)
                    {
                        var mat = mr.sharedMaterials[i];
                        string matName = mat != null ? mat.name : "null";
                        string shaderName = mat != null && mat.shader != null ? mat.shader.name : "null";
                        Debug.Log($"    [{i}] Mat: '{matName}' (Shader: '{shaderName}')");
                    }
                }
            }
        }
    }
}

