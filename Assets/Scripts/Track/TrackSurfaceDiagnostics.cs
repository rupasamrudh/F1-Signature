// TrackSurfaceDiagnostics.cs — Device HUD diagnostics and Surf Dbg unlit surface visualization.
using System.Collections.Generic;
using UnityEngine;

namespace F1Sim.Track
{
    public class TrackSurfaceDiagnostics : MonoBehaviour
    {
        public bool surfDbgEnabled = false;

        // Renderer counts per surface type
        public int asphaltCount = 0;
        public int kerbCount = 0;
        public int lineCount = 0;
        public int runoffCount = 0;
        public int gravelCount = 0;
        public int terrainCount = 0;
        public int barrierCount = 0;
        public int otherCount = 0;

        public bool hasMaterialIssues = false;
        public string diagnosticSummary = "";

        // Internal tracking for Surf Dbg swapping
        private struct RendererOriginalMat
        {
            public MeshRenderer renderer;
            public Material originalMat;
            public string surfaceType;
        }

        private List<RendererOriginalMat> trackedRenderers = new List<RendererOriginalMat>();

        // Solid unlit materials
        private Material dbgAsphalt;
        private Material dbgKerb;
        private Material dbgWhiteLine;
        private Material dbgRunoffTarmac;
        private Material dbgGravel;
        private Material dbgGrass;
        private Material dbgBarrier;
        private Material dbgTerrain;

        void Awake()
        {
            LoadOrCreateDiagnosticMaterials();
            ScanTrackRenderers();
        }

        void LoadOrCreateDiagnosticMaterials()
        {
            // Try loading from saved assets first (Assets/Materials/Track/Diagnostic/)
            dbgAsphalt = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_Asphalt");
            dbgKerb = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_Kerb");
            dbgWhiteLine = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_WhiteLine");
            dbgRunoffTarmac = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_RunoffTarmac");
            dbgGravel = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_Gravel");
            dbgGrass = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_Grass");
            dbgBarrier = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_Barrier");
            dbgTerrain = Resources.Load<Material>("Track/Diagnostic/Mat_SurfDbg_Terrain");

            // Fallback unlit shader in memory if not loaded via Resources
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Unlit/Color");
            if (unlit == null) unlit = Shader.Find("Standard");

            if (dbgAsphalt == null) dbgAsphalt = CreateFallbackMat(unlit, new Color(0.20f, 0.20f, 0.20f, 1f), "Dbg_Asphalt");
            if (dbgKerb == null) dbgKerb = CreateFallbackMat(unlit, new Color(0.90f, 0.10f, 0.10f, 1f), "Dbg_Kerb");
            if (dbgWhiteLine == null) dbgWhiteLine = CreateFallbackMat(unlit, new Color(1.0f, 1.0f, 1.0f, 1f), "Dbg_WhiteLine");
            if (dbgRunoffTarmac == null) dbgRunoffTarmac = CreateFallbackMat(unlit, new Color(0.55f, 0.55f, 0.55f, 1f), "Dbg_RunoffTarmac");
            if (dbgGravel == null) dbgGravel = CreateFallbackMat(unlit, new Color(0.85f, 0.75f, 0.20f, 1f), "Dbg_Gravel");
            if (dbgGrass == null) dbgGrass = CreateFallbackMat(unlit, new Color(0.20f, 0.60f, 0.20f, 1f), "Dbg_Grass");
            if (dbgBarrier == null) dbgBarrier = CreateFallbackMat(unlit, new Color(0.10f, 0.40f, 0.85f, 1f), "Dbg_Barrier");
            if (dbgTerrain == null) dbgTerrain = CreateFallbackMat(unlit, new Color(0.45f, 0.30f, 0.15f, 1f), "Dbg_Terrain");
        }

        Material CreateFallbackMat(Shader s, Color c, string name)
        {
            var m = new Material(s) { name = name };
            m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        public void ScanTrackRenderers()
        {
            trackedRenderers.Clear();
            asphaltCount = 0;
            kerbCount = 0;
            lineCount = 0;
            runoffCount = 0;
            gravelCount = 0;
            terrainCount = 0;
            barrierCount = 0;
            otherCount = 0;
            hasMaterialIssues = false;

            var allRenderers = FindObjectsByType<MeshRenderer>();
            HashSet<Material> inspectedMats = new HashSet<Material>();
            List<string> issues = new List<string>();

            foreach (var r in allRenderers)
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;

                string objName = r.gameObject.name;
                string parentName = r.transform.parent != null ? r.transform.parent.name : "";

                // Only track components belonging to the track or terrain
                bool isTrackPart = objName.Contains("Asphalt") || objName.Contains("Kerb") || objName.Contains("Line") ||
                                   objName.Contains("Runoff") || objName.Contains("Gravel") || objName.Contains("Terrain") ||
                                   objName.Contains("Barrier") || objName.Contains("Gantry") || parentName.Contains("Chunk_") ||
                                   parentName.Contains("Track");

                if (!isTrackPart) continue;

                string sType = ClassifySurfaceType(objName);
                switch (sType)
                {
                    case "Asphalt": asphaltCount++; break;
                    case "Kerb": kerbCount++; break;
                    case "Line": lineCount++; break;
                    case "Runoff": runoffCount++; break;
                    case "Gravel": gravelCount++; break;
                    case "Terrain": terrainCount++; break;
                    case "Barrier": barrierCount++; break;
                    default: otherCount++; break;
                }

                Material m = r.sharedMaterial;
                if (m == null)
                {
                    hasMaterialIssues = true;
                    issues.Add($"Renderer {objName} is MISSING a material!");
                }
                else
                {
                    inspectedMats.Add(m);
                }

                trackedRenderers.Add(new RendererOriginalMat
                {
                    renderer = r,
                    originalMat = m,
                    surfaceType = sType
                });
            }

            // Inspect unique materials
            foreach (var mat in inspectedMats)
            {
                if (mat.shader == null)
                {
                    hasMaterialIssues = true;
                    issues.Add($"Material {mat.name} has NULL shader!");
                }
                else
                {
                    // Check expected textures
                    bool expectsTex = mat.name.Contains("Kerb") || mat.name.Contains("Checker") || mat.name.Contains("Grass") || mat.name.Contains("Gravel");
                    Texture tex = mat.mainTexture ?? mat.GetTexture("_BaseMap");
                    if (expectsTex && tex == null)
                    {
                        hasMaterialIssues = true;
                        issues.Add($"Material {mat.name} expects a texture but mainTexture is null!");
                    }
                }
            }

            if (hasMaterialIssues)
            {
                diagnosticSummary = $"ISSUES: {string.Join("; ", issues)}";
                Debug.LogError($"[TrackSurfaceDiagnostics] {diagnosticSummary}");
            }
            else
            {
                diagnosticSummary = $"Asphalt: {asphaltCount} | Kerbs: {kerbCount} | Lines: {lineCount} | Runoff: {runoffCount} | Gravel: {gravelCount} | Terrain: {terrainCount} | Barrier: {barrierCount} | Mats OK";
                Debug.Log($"[TrackSurfaceDiagnostics] {diagnosticSummary}");
            }
        }

        string ClassifySurfaceType(string name)
        {
            if (name.Contains("Asphalt")) return "Asphalt";
            if (name.Contains("Kerb")) return "Kerb";
            if (name.Contains("Line")) return "Line";
            if (name.Contains("Runoff")) return "Runoff";
            if (name.Contains("Gravel")) return "Gravel";
            if (name.Contains("Terrain") || name.Contains("Grass")) return "Terrain";
            if (name.Contains("Barrier") || name.Contains("Pillar") || name.Contains("Beam")) return "Barrier";
            return "Other";
        }

        public void SetSurfaceDebug(bool enable)
        {
            surfDbgEnabled = enable;
            foreach (var item in trackedRenderers)
            {
                if (item.renderer == null) continue;
                if (enable)
                {
                    switch (item.surfaceType)
                    {
                        case "Asphalt": item.renderer.sharedMaterial = dbgAsphalt; break;
                        case "Kerb": item.renderer.sharedMaterial = dbgKerb; break;
                        case "Line": item.renderer.sharedMaterial = dbgWhiteLine; break;
                        case "Runoff": item.renderer.sharedMaterial = dbgRunoffTarmac; break;
                        case "Gravel": item.renderer.sharedMaterial = dbgGravel; break;
                        case "Terrain": item.renderer.sharedMaterial = dbgTerrain; break;
                        case "Barrier": item.renderer.sharedMaterial = dbgBarrier; break;
                        default: item.renderer.sharedMaterial = dbgAsphalt; break;
                    }
                }
                else
                {
                    item.renderer.sharedMaterial = item.originalMat;
                }
            }
        }

        void OnGUI()
        {
            float scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
            scale = Mathf.Clamp(scale, 1f, 2.5f);

            // Small status line at top-center
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(11 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            statusStyle.normal.textColor = hasMaterialIssues ? Color.red : new Color(0.85f, 0.95f, 0.85f, 0.9f);

            float barW = Screen.width;
            float barH = 22f * scale;
            GUI.Label(new Rect(0, 2f, barW, barH), diagnosticSummary, statusStyle);

            // Top-left HUD buttons
            float btnW = 85f * scale;
            float btnH = 28f * scale;
            float btnY = 24f * scale;

            // "Surf Dbg" Toggle Button
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = surfDbgEnabled ? new Color(0.9f, 0.3f, 0.3f, 0.9f) : new Color(0.2f, 0.2f, 0.2f, 0.7f);
            if (GUI.Button(new Rect(10f * scale, btnY, btnW, btnH), surfDbgEnabled ? "Surf: ON" : "Surf Dbg"))
            {
                SetSurfaceDebug(!surfDbgEnabled);
            }

            // "Show Zones" Toggle Button
            var input = FindAnyObjectByType<CarInput>();
            if (input != null)
            {
                GUI.backgroundColor = input.showZones ? new Color(0.3f, 0.8f, 0.4f, 0.9f) : new Color(0.2f, 0.2f, 0.2f, 0.7f);
                if (GUI.Button(new Rect(100f * scale, btnY, btnW, btnH), input.showZones ? "Zones: ON" : "Show Zones"))
                {
                    input.showZones = !input.showZones;
                    PlayerPrefs.SetInt("F1Sig_ShowZones", input.showZones ? 1 : 0);
                    PlayerPrefs.Save();
                }
            }
            GUI.backgroundColor = oldBg;
        }
    }
}
