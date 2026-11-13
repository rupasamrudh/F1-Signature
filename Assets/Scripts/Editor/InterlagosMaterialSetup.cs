using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    [InitializeOnLoad]
    public static class InterlagosMaterialSetup
    {
        static InterlagosMaterialSetup()
        {
            EditorApplication.delayCall += () =>
            {
                // Check if road material exists
                string sampleMatPath = "Assets/Materials/Track/Interlagos/roada.008.mat";
                if (!File.Exists(sampleMatPath))
                {
                    Debug.Log("[InterlagosMaterialSetup] Track materials missing. Auto-creating URP Lit materials...");
                    CreateOrUpdateMaterials();
                }
            };
        }

        [Serializable]
        public class MatInfo
        {
            public int index;
            public string texture;
            public float[] baseColor;
            public float metallic;
            public float roughness;
        }

        [MenuItem("Tools/F1 Signature/Setup Interlagos Materials")]
        public static void CreateOrUpdateMaterials()
        {
            string jsonPath = "Assets/Materials/Track/Interlagos_mat_mapping.json";
            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[InterlagosMaterialSetup] Mapping file {jsonPath} not found!");
                return;
            }

            string matDir = "Assets/Materials/Track/Interlagos";
            if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);

            string texDir = "Assets/Textures/Track/Interlagos";
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Standard");

            string jsonText = File.ReadAllText(jsonPath);
            // Parse json using simple dictionary parsing or MiniJSON/custom
            var rawMapping = JsonUtilityHelper.ParseMatMapping(jsonText);

            int createdCount = 0;
            int updatedCount = 0;

            foreach (var kvp in rawMapping)
            {
                string matName = kvp.Key;
                string cleanMatName = matName.Replace('/', '_').Replace('\\', '_');
                string matPath = $"{matDir}/{cleanMatName}.mat";
                var info = kvp.Value;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                bool isNew = (mat == null);
                if (isNew)
                {
                    mat = new Material(urpShader);
                }
                else
                {
                    mat.shader = urpShader;
                }

                // Base Map
                if (!string.IsNullOrEmpty(info.texture))
                {
                    string texPath = $"{texDir}/{info.texture}";
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if (tex != null)
                    {
                        mat.SetTexture("_BaseMap", tex);
                    }
                }

                // Base Color
                if (info.baseColor != null && info.baseColor.Length >= 4)
                {
                    mat.SetColor("_BaseColor", new Color(info.baseColor[0], info.baseColor[1], info.baseColor[2], info.baseColor[3]));
                }
                else
                {
                    mat.SetColor("_BaseColor", Color.white);
                }

                // Smoothness & Metallic
                string mlow = cleanMatName.ToLower();
                if (mlow.Contains("glass") || mlow.Contains("vidro") || mlow.Contains("window"))
                {
                    mat.SetFloat("_Smoothness", 0.85f);
                }
                else if (mlow.Contains("road") || mlow.Contains("asphalt") || mlow.Contains("rdil"))
                {
                    mat.SetFloat("_Smoothness", 0.15f);
                }
                else if (mlow.Contains("rmbl") || mlow.Contains("kerb"))
                {
                    mat.SetFloat("_Smoothness", 0.20f);
                }
                else if (mlow.Contains("grass") || mlow.Contains("inerfield") || mlow.Contains("lake") || mlow.Contains("terrain"))
                {
                    mat.SetFloat("_Smoothness", 0.05f);
                }
                else
                {
                    mat.SetFloat("_Smoothness", 0.20f);
                }

                mat.SetFloat("_Metallic", 0.0f);

                // Disable normal mapping and specular fireflies
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
                mat.SetFloat("_SpecularHighlights", 0.0f);
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

                // Cutout / Alpha Test for fences, vegetation, wires
                bool isCutout = mlow.Contains("fence") || mlow.Contains("tree") || mlow.Contains("bush") ||
                                mlow.Contains("chroma") || mlow.Contains("wire") || mlow.Contains("arvore") ||
                                mlow.Contains("screen") || mlow.Contains("grade");

                if (isCutout)
                {
                    mat.SetFloat("_AlphaClip", 1.0f);
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = 2450;
                }
                else
                {
                    mat.SetFloat("_AlphaClip", 0.0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "Opaque");
                    mat.renderQueue = 2000;
                }

                if (isNew)
                {
                    AssetDatabase.CreateAsset(mat, matPath);
                    createdCount++;
                }
                else
                {
                    EditorUtility.SetDirty(mat);
                    updatedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[InterlagosMaterialSetup] Created {createdCount}, updated {updatedCount} materials in {matDir}.");
        }
    }

    public static class JsonUtilityHelper
    {
        public static Dictionary<string, InterlagosMaterialSetup.MatInfo> ParseMatMapping(string json)
        {
            var dict = new Dictionary<string, InterlagosMaterialSetup.MatInfo>();
            // Parse using string tokens from mapping json
            string[] blocks = json.Split(new[] { "\n  \"" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var b in blocks)
            {
                int colonIdx = b.IndexOf("\": {");
                if (colonIdx < 0) continue;
                string key = b.Substring(0, colonIdx).Trim().Trim('"');
                var info = new InterlagosMaterialSetup.MatInfo();

                // Extract texture
                int texIdx = b.IndexOf("\"texture\": \"");
                if (texIdx >= 0)
                {
                    int start = texIdx + 12;
                    int end = b.IndexOf("\"", start);
                    if (end > start) info.texture = b.Substring(start, end - start);
                }

                dict[key] = info;
            }
            return dict;
        }
    }
}

