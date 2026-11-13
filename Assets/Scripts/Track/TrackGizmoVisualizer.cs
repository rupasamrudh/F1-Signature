// TrackGizmoVisualizer.cs — Visualizes track centreline, corners, sectors, and DRS zones in Scene view.
using UnityEngine;
using F1Sim.Simulation.Track;

namespace F1Sim.Track
{
    [ExecuteInEditMode]
    public class TrackGizmoVisualizer : MonoBehaviour
    {
        public string trackResourcePath = TrackDataLoader.DefaultTrackResource;
        public bool showCenterline = true;
        public bool showTrackWidth = true;
        public bool showCorners = true;
        public bool showSectors = true;
        public bool showDrsZones = true;
        public bool showSpawn = true;

        [HideInInspector]
        public TrackConfig trackConfig;

        public void Reload()
        {
            trackConfig = TrackDataLoader.LoadTrack(trackResourcePath);
        }

        void OnDrawGizmos()
        {
            if (trackConfig == null || trackConfig.samples == null || trackConfig.samples.Count == 0)
            {
                Reload();
                if (trackConfig == null) return;
            }

            var samples = trackConfig.samples;
            int count = samples.Count;

            // 1. Centerline & Track Width
            if (showCenterline)
            {
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    Vector3 p0 = new Vector3(samples[i].x, samples[i].y, samples[i].z);
                    Vector3 p1 = new Vector3(samples[next].x, samples[next].y, samples[next].z);

                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(p0, p1);

                    if (showTrackWidth && i % 5 == 0)
                    {
                        Vector3 tangent = (p1 - p0).normalized;
                        Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
                        float halfW = samples[i].width * 0.5f;

                        Gizmos.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                        Gizmos.DrawLine(p0 - normal * halfW, p0 + normal * halfW);
                    }
                }
            }

            // 2. Straight-mode (DRS) zones
            if (showDrsZones && trackConfig.straight_mode_zones != null)
            {
                Gizmos.color = Color.green;
                foreach (var z in trackConfig.straight_mode_zones)
                {
                    foreach (var s in samples)
                    {
                        bool inZone = false;
                        if (z.start_s <= z.end_s)
                            inZone = s.s >= z.start_s && s.s <= z.end_s;
                        else // crosses S/F
                            inZone = s.s >= z.start_s || s.s <= z.end_s;

                        if (inZone)
                        {
                            Vector3 pos = new Vector3(s.x, s.y + 0.3f, s.z);
                            Gizmos.DrawWireSphere(pos, 0.8f);
                        }
                    }
                }
            }

            // 3. Sector splits
            if (showSectors && trackConfig.sectors != null)
            {
                Gizmos.color = Color.magenta;
                foreach (var sec in trackConfig.sectors)
                {
                    int idx = Mathf.Clamp(Mathf.RoundToInt(sec.start_s / 2f), 0, count - 1);
                    Vector3 pos = new Vector3(samples[idx].x, samples[idx].y + 1f, samples[idx].z);
                    Gizmos.DrawCube(pos, new Vector3(2f, 4f, 2f));
                }
            }

            // 4. Corners
            if (showCorners && trackConfig.corners != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f); // Orange
                float spacing = trackConfig.sample_spacing_m > 0f ? trackConfig.sample_spacing_m : 2.0f;
                foreach (var c in trackConfig.corners)
                {
                    int idx = Mathf.Clamp(Mathf.RoundToInt(c.apex_s / spacing), 0, count - 1);
                    Vector3 pos = new Vector3(samples[idx].x, samples[idx].y + 0.5f, samples[idx].z);
                    Gizmos.DrawSphere(pos, 2.5f);
                }
            }

            // 5. Spawn Slot
            if (showSpawn && trackConfig.spawn_slot != null)
            {
                var sp = trackConfig.spawn_slot;
                Vector3 spawnPos = new Vector3(sp.x, sp.y + 0.5f, sp.z);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(spawnPos, new Vector3(3f, 1f, 5f));
            }
        }
    }
}

