// TrackConfig.cs — Pure C#, no UnityEngine. Data structures for track configuration.
using System;
using System.Collections.Generic;

namespace F1Sim.Simulation.Track
{
    public enum SurfaceType
    {
        Asphalt = 0,
        Kerb    = 1,
        Gravel  = 2,
        Grass   = 3
    }

    [Serializable]
    public class TrackSample
    {
        public int index;
        public float s;
        public float x;
        public float y;
        public float z;
        public float width;
        public float banking_deg;
        public float pitch_deg;
        public float curvature;
        public float radius_m;
        public float px_x;
        public float px_y;
        public float barrier_left;
        public float barrier_right;
    }

    [Serializable]
    public class TrackSector
    {
        public int sector;
        public float start_s;
        public float end_s;
        public float length_m;
    }

    [Serializable]
    public class StraightModeZone
    {
        public string name;
        public float start_s;
        public float end_s;
        public float length_m;
    }

    [Serializable]
    public class TrackCorner
    {
        public int number;
        public string name;
        public float apex_s;
        public float entry_s;
        public float exit_s;
        public string direction; // "left" or "right"
        public float radius_m;
        public float curvature;
        public float label_x_px;
        public float label_y_px;
    }

    [Serializable]
    public class SpawnSlot
    {
        public float s;
        public float x;
        public float y;
        public float z;
        public float yaw_deg;
    }

    [Serializable]
    public class TrackConfig
    {
        public int version = 1;
        public string name = "F1 Signature Circuit";
        public float total_length_m = 4309.0f;
        public float sample_spacing_m = 2.0f;
        public float scale_m_per_px = 0.703762f;
        public float airDensity = 1.10f;
        public float default_width_m = 14.0f;

        public List<TrackSector> sectors = new List<TrackSector>();
        public List<StraightModeZone> straight_mode_zones = new List<StraightModeZone>();
        public List<TrackCorner> corners = new List<TrackCorner>();
        public SpawnSlot spawn_slot = new SpawnSlot();
        public List<TrackSample> samples = new List<TrackSample>();
    }

    [Serializable]
    public class ElevationControlPoint
    {
        public string corner;
        public float s;
        public float height;
    }

    [Serializable]
    public class ElevationConfig
    {
        public int version = 1;
        public string name = "Interlagos Elevation Profile";
        public float total_range_m = 40.0f;
        public float max_gradient_pct = 9.09f;
        public List<ElevationControlPoint> control_points = new List<ElevationControlPoint>();
    }
}

