using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    public static class InspectObjImport
    {
        [MenuItem("Tools/F1 Signature/Inspect OBJ Import")]
        public static void Inspect()
        {
            string path = "Assets/Models/F1 Car/2026-mercedes-w17/Processed/Mercedes_W17_Body.obj";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
            {
                Debug.LogError("Could not load OBJ at " + path);
                return;
            }

            Debug.Log($"[InspectObjImport] Loaded {go.name}");
            Debug.Log($"  Root Transform: pos={go.transform.localPosition}, rot={go.transform.localRotation.eulerAngles}, scale={go.transform.localScale}");
            
            foreach (Transform child in go.transform)
            {
                Debug.Log($"  Child '{child.name}': pos={child.localPosition}, rot={child.localRotation.eulerAngles}, scale={child.localScale}");
            }

            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;
                Debug.Log($"  Mesh bounds: center={b.center}, size={b.size}, min={b.min}, max={b.max}");
            }
        }
    }
}

