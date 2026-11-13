// TrackDataLoader.cs — Loads track and elevation JSON via Resources.Load<TextAsset> for editor and device.
using System;
using UnityEngine;
using F1Sim.Simulation.Track;

namespace F1Sim.Track
{
    public static class TrackDataLoader
    {
        public const string DefaultTrackResource = "Tracks/f1sig_track";
        public const string DefaultElevationResource = "Tracks/f1sig_elevation";
        public const string DefaultPropsResource = "Tracks/f1sig_props";

        public static TrackConfig LoadTrack(string resourcePath = DefaultTrackResource)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                Debug.LogError($"[TrackDataLoader] Track TextAsset not found at Resources/{resourcePath}");
                return null;
            }

            return JsonUtility.FromJson<TrackConfig>(textAsset.text);
        }

        public static ElevationConfig LoadElevation(string resourcePath = DefaultElevationResource)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                Debug.LogError($"[TrackDataLoader] Elevation TextAsset not found at Resources/{resourcePath}");
                return null;
            }

            return JsonUtility.FromJson<ElevationConfig>(textAsset.text);
        }

        public static TrackSpline CreateSpline(string resourcePath = DefaultTrackResource)
        {
            var config = LoadTrack(resourcePath);
            return config != null ? new TrackSpline(config) : null;
        }
    }
}

