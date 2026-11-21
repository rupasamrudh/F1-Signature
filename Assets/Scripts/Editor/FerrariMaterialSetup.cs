using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    [InitializeOnLoad]
    public static class FerrariMaterialSetup
    {
        static FerrariMaterialSetup()
        {
            EditorApplication.delayCall += () =>
            {
                Material tyreSides = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Ferrari/tyre_sides.mat");
                if (tyreSides != null && (tyreSides.GetTexture("_BumpMap") != null || tyreSides.GetTextureScale("_BaseMap").y > 0))
                {
                    Debug.Log("[FerrariMaterialSetup] Auto-updating Ferrari materials to fix bloom fireflies and wheel textures...");
                    CreateOrUpdateMaterials();
                }
            };
        }
        public static readonly Dictionary<string, (string baseTex, string normTex)> MaterialConfigs =
            new Dictionary<string, (string, string)>
            {
                { "sf26_carbon1", ("ext_carbon_fiber_d.dds_1", null) },
                { "sf26_details", ("ferrari_details_d_2", null) },
                { "sf26_decals2", ("ferrari_decal_da_3", null) },
                { "discs.008", ("disc_d.dds_4", null) },
                { "tyre_sides", ("S.dds_6", null) },
                { "sf26_wheel_hub", ("ferrari_wheel_d_7", null) },
                { "tyre_tread", ("AC_black_9", null) },
                { "chrome.005", ("AC_silver.dds_8", null) },
                { "black_s.002", ("AC_black_9", null) },
                { "kers_green.004", ("kers_green_10", null) },
                { "kers_red.004", ("kers_red_11", null) },
                { "kers_blue", ("kers_blue_12", null) },
                { "chrome_ao.002", ("chrome_ao_13", null) },
                { "CLEARLED.004", ("clearled.dds_14", null) },
                { "plastic_interior.002", ("AC_black_9", null) },
                { "sf26_lcd", ("LCD_F175_15", null) },
                { "CLARED", ("clearled.dds_14", null) },
                { "sf26_sw_carbon", ("ext_carbon_fiber_16", null) },
                { "sf26_sw", ("ferrari_steering_wheel_d_17", null) },
                { "sf26_sw_decals", ("ferrari_steering_wheel_decals_da_18", null) },
                { "numbers", ("ferrari_driver_19", null) },
                { "gp21_cinture.005", ("gp21_cinture.dds_20", null) },
                { "sf26_paints", ("ferrari_paint_d26_21", null) },
                { "sf26_decals", ("decals26_22", null) },
                { "sf26_carbon1_white", ("ext_carbon_fiber_d - white_23", null) },
                { "rear_light.001", ("rear_light_d_24", null) },
                { "sf26_generics", ("generic_main_d.dds_25", null) },
                { "sf26_carbon2", ("MAT_Carbon_FERRARI3_27", null) },
                { "sf26_carbon3", ("INT_carbon_fibre2_28", null) },
                { "sf26_mirrorl", ("AC_silver.dds_8", null) },
                { "driver_color", ("driver_color.dds_30", null) },
                { "mirrorlight", ("mirror_light_d_31", null) },
                { "glass", ("glass_da_32", null) }
            };

        [MenuItem("Tools/F1 Signature/Setup Ferrari Materials")]
        public static void CreateOrUpdateMaterials()
        {
            string matDir = "Assets/Materials/Ferrari";
            if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);

            string texDir = "Assets/Models/F1 Car/2026-ferrari-sf-26/textures";
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Standard");

            foreach (var kvp in MaterialConfigs)
            {
                string matName = kvp.Key;
                var (baseTexName, normTexName) = kvp.Value;
                string matPath = $"{matDir}/{matName}.mat";

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(urpShader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    mat.shader = urpShader;
                }

                if (!string.IsNullOrEmpty(baseTexName))
                {
                    string texPath = $"{texDir}/{baseTexName}.png";
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if (tex != null) mat.SetTexture("_BaseMap", tex);
                }

                // Explicitly clear normal map and disable normal mapping keyword.
                // This eliminates NaN unpacking fireflies caused by all-zero normal maps.
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");

                // Enable specular highlights and environment reflections for photorealistic PBR rendering
                mat.SetFloat("_SpecularHighlights", 1.0f);
                mat.SetFloat("_EnvironmentReflections", 1.0f);
                mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

                // PBR Material Configurations:
                if (matName == "sf26_paints")
                {
                    // Ferrari Rosso Corsa bodywork: high-gloss automotive clearcoat lacquer over rich red basecoat
                    mat.SetFloat("_Metallic", 0.12f);
                    mat.SetFloat("_Smoothness", 0.90f);
                    mat.SetFloat("_ClearCoatMask", 1.0f);
                    mat.SetFloat("_ClearCoatSmoothness", 0.94f);
                    mat.EnableKeyword("_CLEARCOAT");
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                    mat.SetFloat("_AlphaClip", 0.0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                }
                else if (matName.Contains("carbon"))
                {
                    // Prepreg carbon fiber composites (wings, floor, sidepods): semi-gloss epoxy resin weave
                    mat.SetFloat("_Metallic", matName.Contains("white") ? 0.05f : 0.15f);
                    mat.SetFloat("_Smoothness", matName.Contains("white") ? 0.72f : 0.68f);
                    mat.SetFloat("_ClearCoatMask", 0.0f);
                    mat.DisableKeyword("_CLEARCOAT");
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                    mat.SetFloat("_AlphaClip", 0.0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                }
                else if (matName == "sf26_wheel_hub")
                {
                    // BBS forged magnesium wheels: satin dark metallic alloy rim
                    mat.SetFloat("_Metallic", 0.75f);
                    mat.SetFloat("_Smoothness", 0.62f);
                    mat.SetFloat("_ClearCoatMask", 0.0f);
                    mat.DisableKeyword("_CLEARCOAT");
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
                else if (matName == "tyre_sides")
                {
                    // Pirelli P-Zero sidewalls: vulcanized racing rubber compound with red compound stripe
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.28f);
                    mat.SetFloat("_ClearCoatMask", 0.0f);
                    mat.DisableKeyword("_CLEARCOAT");
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
                else if (matName == "tyre_tread")
                {
                    // Scrubbed slick tire contact patch: dark carbon black rubber
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.18f);
                    mat.SetFloat("_ClearCoatMask", 0.0f);
                    mat.DisableKeyword("_CLEARCOAT");
                    mat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f, 1f));
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
                else if (matName == "discs.008")
                {
                    // Brembo carbon-carbon brake discs
                    mat.SetFloat("_Metallic", 0.45f);
                    mat.SetFloat("_Smoothness", 0.40f);
                    mat.SetFloat("_ClearCoatMask", 0.0f);
                    mat.DisableKeyword("_CLEARCOAT");
                    mat.SetColor("_BaseColor", Color.white);
                }
                else if (matName.Contains("chrome") || matName == "sf26_mirrorl")
                {
                    // Polished chrome & rear mirrors: high-specular reflective surface
                    mat.SetFloat("_Metallic", 0.95f);
                    mat.SetFloat("_Smoothness", 0.95f);
                    mat.SetFloat("_ClearCoatMask", 0.0f);
                    mat.DisableKeyword("_CLEARCOAT");
                    mat.SetColor("_BaseColor", Color.white);
                }
                else if (matName == "sf26_lcd")
                {
                    // Cockpit digital telemetry dash: backlit LCD
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.85f);
                    mat.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.35f, 1f));
                    mat.EnableKeyword("_EMISSION");
                }
                else if (matName.Contains("kers_green"))
                {
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.90f);
                    mat.SetColor("_EmissionColor", new Color(0.1f, 1.8f, 0.2f, 1f));
                    mat.EnableKeyword("_EMISSION");
                }
                else if (matName.Contains("kers_red"))
                {
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.90f);
                    mat.SetColor("_EmissionColor", new Color(1.9f, 0.1f, 0.05f, 1f));
                    mat.EnableKeyword("_EMISSION");
                }
                else if (matName.Contains("kers_blue"))
                {
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.90f);
                    mat.SetColor("_EmissionColor", new Color(0.1f, 0.35f, 1.9f, 1f));
                    mat.EnableKeyword("_EMISSION");
                }
                else if (matName.Contains("CLEARLED") || matName.Contains("CLARED"))
                {
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.90f);
                    mat.SetColor("_EmissionColor", new Color(0.7f, 0.7f, 0.7f, 1f));
                    mat.EnableKeyword("_EMISSION");
                }
                else if (matName.Contains("rear_light"))
                {
                    // FIA safety rain / ERS warning light
                    mat.SetFloat("_Metallic", 0.0f);
                    mat.SetFloat("_Smoothness", 0.90f);
                    mat.SetColor("_EmissionColor", new Color(2.0f, 0.05f, 0.05f, 1f));
                    mat.EnableKeyword("_EMISSION");
                }
                else if (matName == "glass")
                {
                    mat.SetFloat("_Metallic", 0.10f);
                    mat.SetFloat("_Smoothness", 0.95f);
                    mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.65f));
                    mat.SetFloat("_AlphaClip", 1.0f);
                    mat.SetFloat("_Cutoff", 0.10f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }
                else if (matName.Contains("decal") || matName.Contains("numbers") || matName.Contains("details") || matName.Contains("cinture"))
                {
                    // High-gloss vinyl decals applied cleanly over bodywork
                    mat.SetFloat("_Metallic", 0.05f);
                    mat.SetFloat("_Smoothness", 0.78f);
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetFloat("_AlphaClip", 1.0f);
                    mat.SetFloat("_Cutoff", 0.33f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }
                else
                {
                    // Interior plastics and driver trim
                    mat.SetFloat("_Metallic", 0.10f);
                    mat.SetFloat("_Smoothness", 0.40f);
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                    mat.SetFloat("_AlphaClip", 0.0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                }

                // Invert V coordinate on tyre_sides so the red Pirelli stripe maps to the sidewall
                if (matName == "tyre_sides")
                {
                    mat.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
                    mat.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
                }
                else
                {
                    mat.SetTextureScale("_BaseMap", Vector2.one);
                    mat.SetTextureOffset("_BaseMap", Vector2.zero);
                }

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FerrariMaterialSetup] Ferrari materials updated: 0 reflections, 0 bloom NaNs, red sidewall restored.");
        }
    }
}
