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
                { "sf25_carbon1", ("ext_carbon_fiber_d.dds_1", null) },
                { "sf25_details", ("ferrari_details_d_2", null) },
                { "sf25_decals2", ("ferrari_decal_da_3", null) },
                { "discs.008", ("disc_d.dds_4", null) },
                { "tyre_sides", ("S.dds_6", null) },
                { "sf25_wheel_hub", ("ferrari_wheel_d_7", null) },
                { "tyre_tread", ("AC_black_9", null) },
                { "chrome.005", ("AC_silver.dds_8", null) },
                { "black_s.002", ("AC_black_9", null) },
                { "kers_green.004", ("kers_green_10", null) },
                { "kers_red.004", ("kers_red_11", null) },
                { "kers_blue", ("kers_blue_12", null) },
                { "chrome_ao.002", ("chrome_ao_13", null) },
                { "CLEARLED.004", ("clearled.dds_14", null) },
                { "plastic_interior.002", ("AC_black_9", null) },
                { "sf25_lcd", ("LCD_F175_15", null) },
                { "CLARED", ("clearled.dds_14", null) },
                { "sf25_sw_carbon", ("ext_carbon_fiber_16", null) },
                { "sf25_sw", ("ferrari_steering_wheel_d_17", null) },
                { "sf25_sw_decals", ("ferrari_steering_wheel_decals_da_18", null) },
                { "numbers", ("ferrari_driver_19", null) },
                { "gp21_cinture.005", ("gp21_cinture.dds_20", null) },
                { "sf25_paints", ("ferrari_paint_d26_21", null) },
                { "sf25_decals", ("decals26_22", null) },
                { "sf25_carbon1_white", ("ext_carbon_fiber_d - white_23", null) },
                { "rear_light.001", ("rear_light_d_24", null) },
                { "sf25_generics", ("generic_main_d.dds_25", null) },
                { "sf25_carbon2", ("MAT_Carbon_FERRARI3_27", null) },
                { "sf25_carbon3", ("INT_carbon_fibre2_28", null) },
                { "sf25_mirrorl", ("AC_silver.dds_8", null) },
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

                // Remove all reflections, specular highlights, and emissions
                // to display the car textures cleanly without mirror artifacts or bloom flares
                mat.SetFloat("_Metallic", 0.0f);
                mat.SetFloat("_Smoothness", 0.0f);
                mat.SetFloat("_EnvironmentReflections", 0.0f);
                mat.SetFloat("_SpecularHighlights", 0.0f);
                mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
                mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

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

                // Pure black rubber for contact tread
                if (matName == "tyre_tread")
                {
                    mat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f, 1f));
                }
                else
                {
                    mat.SetColor("_BaseColor", Color.white);
                }

                // Handle decals and cutouts
                if (matName.Contains("decal") || matName.Contains("numbers") || matName.Contains("cinture") || matName == "glass")
                {
                    mat.SetFloat("_AlphaClip", 1.0f);
                    mat.SetFloat("_Cutoff", 0.33f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FerrariMaterialSetup] Ferrari materials updated: 0 reflections, 0 bloom NaNs, red sidewall restored.");
        }
    }
}
