// MinimapDrawer.cs — Draws the track centreline into a Texture2D with sector colours and start/finish mark.
using UnityEngine;
using F1Sim.Simulation.Track;

namespace F1Sim.Track
{
    public static class MinimapDrawer
    {
        public static Texture2D GenerateMinimapTexture(TrackConfig config, int width = 160, int height = 110)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color bg = new Color(0.08f, 0.08f, 0.10f, 0.75f);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
            tex.SetPixels(pixels);

            if (config == null || config.samples == null || config.samples.Count == 0)
            {
                tex.Apply();
                return tex;
            }

            // Find world X, Z bounds
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var smp in config.samples)
            {
                if (smp.x < minX) minX = smp.x;
                if (smp.x > maxX) maxX = smp.x;
                if (smp.z < minZ) minZ = smp.z;
                if (smp.z > maxZ) maxZ = smp.z;
            }

            float rangeX = Mathf.Max(maxX - minX, 1f);
            float rangeZ = Mathf.Max(maxZ - minZ, 1f);

            // Fit north-up with padding
            float pad = 10f;
            float scaleX = (width - 2f * pad) / rangeX;
            float scaleZ = (height - 2f * pad) / rangeZ;
            float mapScale = Mathf.Min(scaleX, scaleZ);

            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;

            Vector2 WorldToMap(float wx, float wz)
            {
                float px = (width * 0.5f) + (wx - cx) * mapScale;
                float py = (height * 0.5f) + (wz - cz) * mapScale; // +Z is north/up
                return new Vector2(px, py);
            }

            // Sector colors: S1 = Red, S2 = Blue, S3 = Yellow
            Color colS1 = new Color(0.95f, 0.2f, 0.2f, 1f);
            Color colS2 = new Color(0.2f, 0.55f, 0.95f, 1f);
            Color colS3 = new Color(0.95f, 0.85f, 0.15f, 1f);

            float s1End = (config.sectors != null && config.sectors.Count > 0) ? config.sectors[0].end_s : 1224f;
            float s2End = (config.sectors != null && config.sectors.Count > 1) ? config.sectors[1].end_s : 3130f;

            int n = config.samples.Count;
            for (int i = 0; i < n; i++)
            {
                var s0 = config.samples[i];
                var s1 = config.samples[(i + 1) % n];

                Vector2 p0 = WorldToMap(s0.x, s0.z);
                Vector2 p1 = WorldToMap(s1.x, s1.z);

                Color lineCol = (s0.s < s1End) ? colS1 : (s0.s < s2End ? colS2 : colS3);
                DrawThickLine(tex, (int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, lineCol, 2);
            }

            // Start/Finish marker in white
            var sf = config.samples[0];
            Vector2 sfMap = WorldToMap(sf.x, sf.z);
            DrawDisk(tex, (int)sfMap.x, (int)sfMap.y, 3, Color.white);

            tex.Apply();
            return tex;
        }

        public static Vector2 GetNormalizedCarPosition(float carX, float carZ, TrackConfig config, int width, int height)
        {
            if (config == null || config.samples == null || config.samples.Count == 0)
                return Vector2.zero;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var smp in config.samples)
            {
                if (smp.x < minX) minX = smp.x;
                if (smp.x > maxX) maxX = smp.x;
                if (smp.z < minZ) minZ = smp.z;
                if (smp.z > maxZ) maxZ = smp.z;
            }

            float rangeX = Mathf.Max(maxX - minX, 1f);
            float rangeZ = Mathf.Max(maxZ - minZ, 1f);
            float pad = 10f;
            float mapScale = Mathf.Min((width - 2f * pad) / rangeX, (height - 2f * pad) / rangeZ);

            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;

            float px = (width * 0.5f) + (carX - cx) * mapScale;
            float py = (height * 0.5f) + (carZ - cz) * mapScale;
            return new Vector2(px, py);
        }

        static void DrawThickLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col, int thickness)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                DrawDisk(tex, x0, y0, thickness, col);
                if (x0 == x1 && y0 == y1) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static void DrawDisk(Texture2D tex, int cx, int cy, int radius, Color col)
        {
            int w = tex.width, h = tex.height;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        int px = cx + dx;
                        int py = cy + dy;
                        if (px >= 0 && px < w && py >= 0 && py < h)
                        {
                            tex.SetPixel(px, py, col);
                        }
                    }
                }
            }
        }
    }
}

