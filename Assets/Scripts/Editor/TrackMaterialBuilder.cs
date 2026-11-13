// TrackMaterialBuilder.cs — Procedural generation of URP Lit and Unlit track materials and textures via Unity APIs.
// Saved under Assets/Materials/Track and Assets/Textures/Track.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace F1Sim.Editor
{
    public static class TrackMaterialBuilder
    {
        public static (Material asphalt, Material kerb, Material whiteLine, Material runoffTarmac, Material gravel, Material grass, Material barrier, Material checker) GetOrCreateTrackMaterials()
        {
            var urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLitShader == null) urpLitShader = Shader.Find("Standard");

            var urpUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpUnlitShader == null) urpUnlitShader = Shader.Find("Unlit/Color");

            // Ensure directory structure under Assets/Materials/Track and Assets/Textures/Track
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Materials/Track");
            EnsureFolder("Assets/Materials/Track/Diagnostic");
            EnsureFolder("Assets/Textures");
            EnsureFolder("Assets/Textures/Track");

            // 1. Procedural Kerb Stripe Texture (Red & White alternating stripes along V)
            string kerbTexPath = "Assets/Textures/Track/Tex_Kerb_Stripe.png";
            Texture2D kerbTex = AssetDatabase.LoadAssetAtPath<Texture2D>(kerbTexPath);
            if (kerbTex == null)
            {
                kerbTex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
                Color red = new Color(0.88f, 0.12f, 0.12f, 1f);
                Color white = new Color(0.95f, 0.95f, 0.95f, 1f);

                // Alternating stripes along Y (V coordinate)
                for (int y = 0; y < 256; y++)
                {
                    Color stripeColor = (y < 128) ? red : white;
                    for (int x = 0; x < 256; x++)
                    {
                        kerbTex.SetPixel(x, y, stripeColor);
                    }
                }
                kerbTex.Apply();
                File.WriteAllBytes(kerbTexPath, kerbTex.EncodeToPNG());
                AssetDatabase.ImportAsset(kerbTexPath);

                SetTextureSettings(kerbTexPath, TextureWrapMode.Repeat);
                kerbTex = AssetDatabase.LoadAssetAtPath<Texture2D>(kerbTexPath);
            }

            // 2. Checkerboard Texture for Start/Finish Gantry / Line
            string checkTexPath = "Assets/Textures/Track/Tex_Checker.png";
            Texture2D checkTex = AssetDatabase.LoadAssetAtPath<Texture2D>(checkTexPath);
            if (checkTex == null)
            {
                checkTex = new Texture2D(128, 128, TextureFormat.RGBA32, true);
                Color c1 = Color.white;
                Color c2 = new Color(0.08f, 0.08f, 0.08f, 1f);
                for (int y = 0; y < 128; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        bool isEven = ((x / 32) + (y / 32)) % 2 == 0;
                        checkTex.SetPixel(x, y, isEven ? c1 : c2);
                    }
                }
                checkTex.Apply();
                File.WriteAllBytes(checkTexPath, checkTex.EncodeToPNG());
                AssetDatabase.ImportAsset(checkTexPath);

                SetTextureSettings(checkTexPath, TextureWrapMode.Repeat);
                checkTex = AssetDatabase.LoadAssetAtPath<Texture2D>(checkTexPath);
            }

            // 3. Subtle Grass Texture
            string grassTexPath = "Assets/Textures/Track/Tex_Grass.png";
            Texture2D grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(grassTexPath);
            if (grassTex == null)
            {
                grassTex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
                Color gBase = new Color(0.24f, 0.48f, 0.18f, 1f);
                Color gDark = new Color(0.18f, 0.38f, 0.14f, 1f);
                Color gLight = new Color(0.28f, 0.54f, 0.22f, 1f);

                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        float n1 = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
                        float n2 = Mathf.PerlinNoise(x * 0.25f, y * 0.25f);
                        float blend = (n1 * 0.6f + n2 * 0.4f);
                        Color c = Color.Lerp(gDark, gLight, blend);
                        grassTex.SetPixel(x, y, c);
                    }
                }
                grassTex.Apply();
                File.WriteAllBytes(grassTexPath, grassTex.EncodeToPNG());
                AssetDatabase.ImportAsset(grassTexPath);

                SetTextureSettings(grassTexPath, TextureWrapMode.Repeat);
                grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(grassTexPath);
            }

            // 4. Subtle Gravel Texture
            string gravelTexPath = "Assets/Textures/Track/Tex_Gravel.png";
            Texture2D gravelTex = AssetDatabase.LoadAssetAtPath<Texture2D>(gravelTexPath);
            if (gravelTex == null)
            {
                gravelTex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
                Color sBase = new Color(0.68f, 0.58f, 0.42f, 1f);
                Color sDark = new Color(0.55f, 0.45f, 0.32f, 1f);
                Color sLight = new Color(0.76f, 0.66f, 0.50f, 1f);

                for (int y = 0; y < 256; y++)
                {
                    for (int x = 0; x < 256; x++)
                    {
                        float n = Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                        Color c = Color.Lerp(sDark, sLight, n);
                        gravelTex.SetPixel(x, y, c);
                    }
                }
                gravelTex.Apply();
                File.WriteAllBytes(gravelTexPath, gravelTex.EncodeToPNG());
                AssetDatabase.ImportAsset(gravelTexPath);

                SetTextureSettings(gravelTexPath, TextureWrapMode.Repeat);
                gravelTex = AssetDatabase.LoadAssetAtPath<Texture2D>(gravelTexPath);
            }

            // Standard Track Materials (URP Lit)
            Material asphalt = GetOrCreateMat("Assets/Materials/Track/Mat_Asphalt.mat", urpLitShader,
                new Color(0.18f, 0.18f, 0.20f, 1f), smoothness: 0.35f, metallic: 0.05f);

            Material kerb = GetOrCreateMat("Assets/Materials/Track/Mat_Kerb.mat", urpLitShader,
                Color.white, smoothness: 0.4f, metallic: 0.0f, mainTex: kerbTex);

            Material whiteLine = GetOrCreateMat("Assets/Materials/Track/Mat_WhiteLine.mat", urpLitShader,
                new Color(0.95f, 0.95f, 0.95f, 1f), smoothness: 0.5f, metallic: 0.0f);

            Material runoffTarmac = GetOrCreateMat("Assets/Materials/Track/Mat_RunoffTarmac.mat", urpLitShader,
                new Color(0.52f, 0.52f, 0.54f, 1f), smoothness: 0.30f, metallic: 0.05f);

            Material gravel = GetOrCreateMat("Assets/Materials/Track/Mat_Gravel.mat", urpLitShader,
                Color.white, smoothness: 0.15f, metallic: 0.0f, mainTex: gravelTex);

            Material grass = GetOrCreateMat("Assets/Materials/Track/Mat_Grass.mat", urpLitShader,
                Color.white, smoothness: 0.05f, metallic: 0.0f, mainTex: grassTex);

            Material barrier = GetOrCreateMat("Assets/Materials/Track/Mat_Barrier.mat", urpLitShader,
                new Color(0.72f, 0.72f, 0.75f, 1f), smoothness: 0.25f, metallic: 0.1f);

            Material checker = GetOrCreateMat("Assets/Materials/Track/Mat_Checker.mat", urpLitShader,
                Color.white, smoothness: 0.5f, metallic: 0.0f, mainTex: checkTex);

            // Diagnostic Materials (URP Unlit, needing no textures)
            CreateOrUpdateDiagnosticMaterials(urpUnlitShader);

            return (asphalt, kerb, whiteLine, runoffTarmac, gravel, grass, barrier, checker);
        }

        public static void CreateOrUpdateDiagnosticMaterials(Shader unlitShader)
        {
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_Asphalt.mat", unlitShader, new Color(0.20f, 0.20f, 0.20f, 1f));
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_Kerb.mat", unlitShader, new Color(0.90f, 0.10f, 0.10f, 1f));
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_WhiteLine.mat", unlitShader, new Color(1.0f, 1.0f, 1.0f, 1f));
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_RunoffTarmac.mat", unlitShader, new Color(0.55f, 0.55f, 0.55f, 1f));
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_Gravel.mat", unlitShader, new Color(0.85f, 0.75f, 0.20f, 1f));
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_Grass.mat", unlitShader, new Color(0.20f, 0.60f, 0.20f, 1f));
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_Barrier.mat", unlitShader, new Color(0.10f, 0.40f, 0.85f, 1f));
            GetOrCreateUnlitMat("Assets/Materials/Track/Diagnostic/Mat_SurfDbg_Terrain.mat", unlitShader, new Color(0.45f, 0.30f, 0.15f, 1f));
        }

        static Material GetOrCreateMat(string path, Shader shader, Color color, float smoothness, float metallic, Texture2D mainTex = null)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static Material GetOrCreateUnlitMat(string path, Shader shader, Color color)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                mat.SetColor("_BaseColor", color);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
                mat.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static void SetTextureSettings(string path, TextureWrapMode wrap)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = wrap;
                importer.SaveAndReimport();
            }
        }

        static void EnsureFolder(string fullPath)
        {
            if (AssetDatabase.IsValidFolder(fullPath)) return;
            string parent = Path.GetDirectoryName(fullPath).Replace('\\', '/');
            string child = Path.GetFileName(fullPath);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
