// TrackDataLoader.cs — Loads track and elevation JSON into TrackConfig and builds TrackSpline.
using System;
using System.IO;
using UnityEngine;
using F1Sim.Simulation.Track;

namespace F1Sim.Track
{
    public static class TrackDataLoader
    {
        public static TrackConfig LoadTrack(string trackPath = "Assets/Data/Tracks/f1sig_track.json")
        {
            if (!File.Exists(trackPath))
            {
                Debug.LogError($"[TrackDataLoader] File not found: {trackPath}");
                return null;
            }

            string json = File.ReadAllText(trackPath);
            return JsonUtility.FromJson<TrackConfig>(json);
        }

        public static ElevationConfig LoadElevation(string elevPath = "Assets/Data/Tracks/f1sig_elevation.json")
        {
            if (!File.Exists(elevPath))
            {
                Debug.LogError($"[TrackDataLoader] File not found: {elevPath}");
                return null;
            }

            string json = File.ReadAllText(elevPath);
            return JsonUtility.FromJson<ElevationConfig>(json);
        }

        public static TrackSpline CreateSpline(string trackPath = "Assets/Data/Tracks/f1sig_track.json")
        {
            var config = LoadTrack(trackPath);
            return config != null ? new TrackSpline(config) : null;
        }
    }
}

