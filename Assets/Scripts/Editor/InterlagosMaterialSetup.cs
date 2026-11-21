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

                // ── Steel / Armco barriers — PBR galvanized steel ──────────────
                bool isArmco = mlow.Contains("armco") || mlow.Contains("silverbars") ||
                               mlow.Contains("guardrail") || mlow.Contains("armcopost");
                if (isArmco)
                {
                    mat.SetFloat("_Metallic", 0.72f);
                    mat.SetFloat("_Smoothness", 0.52f);
                    mat.SetFloat("_GlossMapScale", 1.0f);
                    mat.SetFloat("_SpecularHighlights", 1.0f);
                    mat.SetFloat("_EnvironmentReflections", 1.0f);
                    mat.SetFloat("_GlossyReflections", 1.0f);
                    mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    mat.SetTexture("_BumpMap", null);
                    mat.DisableKeyword("_NORMALMAP");
                }
                else
                {
                    mat.SetFloat("_Metallic", 0.0f);

                    // Disable normal mapping and specular fireflies on non-steel surfaces
                    mat.SetTexture("_BumpMap", null);
                    mat.DisableKeyword("_NORMALMAP");
                    mat.SetFloat("_SpecularHighlights", 0.0f);
                    mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }

                // Identify material categories
                bool isShadowOrEmpty = mlow.Contains("5165") || mlow.Contains("5386") || mlow.Contains("5387") || 
                                       mlow.Contains("multimat_134_slot") || mlow.Contains("genshadow");

                bool isTireDecal = mlow.Contains("groove") || mlow.Contains("marbles");

                bool isCutout = mlow.Contains("fence") || mlow.Contains("tree") || mlow.Contains("bush") ||
                                mlow.Contains("chroma") || mlow.Contains("wire") || mlow.Contains("arvore") ||
                                mlow.Contains("screen") || mlow.Contains("grade") || mlow.Contains("bars") ||
                                mlow.Contains("outerbuild") || mlow.Contains("bgbuild") || mlow.Contains("prhousefront") ||
                                mlow.Contains("bigradio") || mlow.Contains("alpha") || mlow.Contains("toldo");

                bool isTwoSided = isCutout || mlow.Contains("wall") || mlow.Contains("armco") || mlow.Contains("tyre") ||
                                  mlow.Contains("guard") || mlow.Contains("gstand") || mlow.Contains("roof") ||
                                  mlow.Contains("teto") || mlow.Contains("toldo") || mlow.Contains("tent") ||
                                  mlow.Contains("gantry") || mlow.Contains("support") || mlow.Contains("pole") ||
                                  mlow.Contains("pillar") || mlow.Contains("tower") || mlow.Contains("sign") ||
                                  mlow.Contains("banner") || mlow.Contains("advert") || mlow.Contains("stall") ||
                                  mlow.Contains("door") || mlow.Contains("portao") || mlow.Contains("predio");

                if (isTwoSided)
                {
                    mat.SetFloat("_Cull", 0.0f); // Double-Sided (Off)
                }
                else
                {
                    mat.SetFloat("_Cull", 2.0f); // Backface culling
                }

                if (isShadowOrEmpty)
                {
                    // Transparent invisible decal (never render as black pitch slabs)
                    mat.SetFloat("_Surface", 1.0f);
                    mat.SetFloat("_Blend", 0.0f);
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0.0f);
                    mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                }
                else if (isTireDecal)
                {
                    // Groove / Marbles translucent decal
                    mat.SetFloat("_Surface", 1.0f);
                    mat.SetFloat("_Blend", 0.0f);
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0.0f);
                    mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.25f));
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                }
                else if (isCutout)
                {
                    mat.SetFloat("_Surface", 0.0f);
                    mat.SetFloat("_AlphaClip", 1.0f);
                    mat.SetFloat("_Cutoff", mlow.Contains("tree") || mlow.Contains("bush") ? 0.5f : 0.33f);
                    mat.SetFloat("_ZWrite", 1.0f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = 2450;
                }
                else
                {
                    // All road surfaces (roadanl, roadsf, roadd, roada, roadpits, creteg, rdil*), terrain, curbs, barriers, etc. are OPAQUE
                    mat.SetFloat("_Surface", 0.0f);
                    mat.SetFloat("_AlphaClip", 0.0f);
                    mat.SetFloat("_ZWrite", 1.0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
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

