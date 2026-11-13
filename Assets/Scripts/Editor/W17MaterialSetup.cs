using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1Sim.EditorTools
{
    public static class W17MaterialSetup
    {
        public static readonly Dictionary<string, (string baseTex, string normTex)> MaterialConfigs =
            new Dictionary<string, (string, string)>
            {
                { "generics.007", ("generic_main_d_1", "flat_nm_0") },
                { "mercedes_decals.005", ("mercedes_decal_da.tif_2", null) },
                { "mercedes_carbon3.007", ("carbon_fiber_1_4", null) },
                { "mercedes_detail.007", ("mercedes_details_d.tif_5", "flat_nm_0") },
                { "mercedes_carbon1.007", ("carbon_fiber_1_4", null) },
                { "rear_light_2.007", ("rear_light_2_d_7", "rear_light_2_n.tif_6") },
                { "mercedes_number_63.005", ("mercedes_driver_63.tif_8", null) },
                { "mercedes_mirror_left.005", ("AC_silver_17", null) },
                { "mercedes_paint.007", ("mercedes_paint_d.tif_10", "flat_nm_0") },
                { "mercedes_paint2.007", ("mercedes_paint_halo_11", "flat_nm_0") },
                { "mercedes_mirror_right.005", ("AC_silver_17", null) },
                { "mercedes_wheel_hub.007", ("mercedes_wheel_d.tif_13", null) },
                { "discs.007", ("disc_d_14", null) },
                { "sidewall.007", ("S_16", "Normal_15") },
                { "tread_medium.007", ("S_16", "Normal_15") },
                { "chrome.004", ("AC_silver_17", "flat_nm_0") },
                { "gp21_cinture.004", ("gp21_cinture_19", "gp21_cinture_NM_18") },
                { "mercedes_colordriver.005", ("driver_color_20", null) },
                { "mercedes_carbon2.005", ("carbon_fiber_2_22", null) },
                { "mercedes_sw.005", ("mercedes_steering_wheel_d25_23", "flat_nm_0") },
                { "sw_decals.003", ("mercedes_steering_wheel_decal_da.tif_24", null) },
                { "sw_carbon.003", ("carbon_fiber_3_26", null) },
                { "indicator_lights.004", ("int_indicator_d_27", null) },
                { "rev_lights.003", ("ext_carbon_fiber_d_28", "flat_nm_0") },
                { "generics_lcd.003", ("W13LCD2_29", "flat_nm_0") },
                { "kers_red.003", ("LIGHTGREEN_30", null) },
                { "kers_green.003", ("LIGHTGREEN_30", null) },
                { "whitelight.003", ("LIGHTwhite_31", null) },
                { "CLEARLED.003", ("clearled_32", null) }
            };

        [MenuItem("Tools/F1 Signature/Setup W17 Materials")]
        public static void CreateOrUpdateMaterials()
        {
            string matDir = "Assets/Materials/W17";
            if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);

            string texDir = "Assets/Models/F1 Car/2026-mercedes-w17/textures";
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

                if (!string.IsNullOrEmpty(normTexName))
                {
                    string normPath = $"{texDir}/{normTexName}.png";
                    Texture2D nTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normPath);
                    if (nTex != null)
                    {
                        mat.SetTexture("_BumpMap", nTex);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                }

                // Remove all reflections and artificial specular glare
                // Shows the exact car textures, decals, and colors cleanly without mirror artifacts
                mat.SetFloat("_Metallic", 0.0f);
                mat.SetFloat("_Smoothness", 0.0f);
                mat.SetFloat("_EnvironmentReflections", 0.0f);
                mat.SetFloat("_SpecularHighlights", 0.0f);
                mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

                // Handle decals / transparent items if needed
                if (matName.Contains("decal") || matName.Contains("number") || matName.Contains("cinture"))
                {
                    mat.SetFloat("_AlphaClip", 1.0f);
                    mat.SetFloat("_Cutoff", 0.33f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[W17MaterialSetup] Mercedes materials updated with 0 reflections in " + matDir);
        }
    }
}
