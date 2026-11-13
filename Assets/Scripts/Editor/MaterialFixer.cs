using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace F1Sim.Editor
{
    public static class MaterialFixer
    {
        [MenuItem("Tools/F1 Signature/Fix Materials")]
        public static void FixMaterials()
        {
            var urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null)
            {
                Debug.LogError("[MaterialFixer] Shader 'Universal Render Pipeline/Lit' not found!");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            // Create or load Body material
            string bodyMatPath = "Assets/Materials/CarBody.mat";
            Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(bodyMatPath);
            if (bodyMat == null)
            {
                bodyMat = new Material(urpShader) { name = "CarBody" };
                bodyMat.SetColor("_BaseColor", new Color(0.85f, 0.08f, 0.12f, 1f));
                bodyMat.SetFloat("_Metallic", 0.7f);
                bodyMat.SetFloat("_Smoothness", 0.8f);
                AssetDatabase.CreateAsset(bodyMat, bodyMatPath);
                Debug.Log($"[MaterialFixer] Created {bodyMatPath}");
            }
            else if (bodyMat.shader != urpShader)
            {
                bodyMat.shader = urpShader;
                EditorUtility.SetDirty(bodyMat);
            }

            // Create or load Wheel material
            string wheelMatPath = "Assets/Materials/Wheel.mat";
            Material wheelMat = AssetDatabase.LoadAssetAtPath<Material>(wheelMatPath);
            if (wheelMat == null)
            {
                wheelMat = new Material(urpShader) { name = "Wheel" };
                wheelMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f, 1f));
                wheelMat.SetFloat("_Metallic", 0.2f);
                wheelMat.SetFloat("_Smoothness", 0.3f);
                AssetDatabase.CreateAsset(wheelMat, wheelMatPath);
                Debug.Log($"[MaterialFixer] Created {wheelMatPath}");
            }
            else if (wheelMat.shader != urpShader)
            {
                wheelMat.shader = urpShader;
                EditorUtility.SetDirty(wheelMat);
            }

            // Create or load Ground material
            string groundMatPath = "Assets/Materials/Ground.mat";
            Material groundMat = AssetDatabase.LoadAssetAtPath<Material>(groundMatPath);
            if (groundMat == null)
            {
                groundMat = new Material(urpShader) { name = "Ground" };
                groundMat.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.35f, 1f));
                groundMat.SetFloat("_Metallic", 0.0f);
                groundMat.SetFloat("_Smoothness", 0.2f);
                AssetDatabase.CreateAsset(groundMat, groundMatPath);
                Debug.Log($"[MaterialFixer] Created {groundMatPath}");
            }
            else if (groundMat.shader != urpShader)
            {
                groundMat.shader = urpShader;
                EditorUtility.SetDirty(groundMat);
            }

            AssetDatabase.SaveAssets();

            // Find all Car objects in the open scene
            var carControllers = Object.FindObjectsByType<CarController>(FindObjectsInactive.Include);
            foreach (var cc in carControllers)
            {
                var renderers = cc.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    string rName = r.gameObject.name.ToLower();
                    Material targetMat = bodyMat;
                    if (rName.Contains("wheel") || rName.Contains("tire") || rName.Contains("cube"))
                    {
                        targetMat = wheelMat;
                    }
                    r.sharedMaterial = targetMat;
                    EditorUtility.SetDirty(r);
                    Debug.Log($"[MaterialFixer] Assigned {targetMat.name} to Car Renderer: {r.gameObject.name}");
                }
            }

            // Also replace any material in the open scene whose shader is missing or error-pink (Hidden/InternalErrorShader)
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            foreach (var r in allRenderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null || m.shader.name.Contains("Error") || m.shader.name.Contains("InternalErrorShader"))
                    {
                        mats[i] = (r.gameObject.name.ToLower().Contains("plane") || r.gameObject.name.ToLower().Contains("ground"))
                            ? groundMat : bodyMat;
                        changed = true;
                        Debug.Log($"[MaterialFixer] Replaced missing/error shader on {r.gameObject.name} with {mats[i].name}");
                    }
                }
                if (changed)
                {
                    r.sharedMaterials = mats;
                    EditorUtility.SetDirty(r);
                }
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[MaterialFixer] Completed fixing materials and saved scene.");
        }
    }
}
