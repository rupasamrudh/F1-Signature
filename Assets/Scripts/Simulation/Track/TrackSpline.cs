// TrackSpline.cs — Pure C#, no UnityEngine (uses System.MathF).
// Fast cached local query, surface classification, speed profile helper.
using System;
using System.Collections.Generic;

namespace F1Sim.Simulation.Track
{
    public struct TrackQueryResult
    {
        public float s;
        public float lateralOffset; // positive = left of driving direction
        public float headingRad;    // radians, clockwise from world +Z
        public float curvature;     // 1/m, positive = turning left
        public float heightY;       // world Y height
        public float slope;         // pitch along track (dy/ds)
        public float trackWidth;    // width in meters
        public SurfaceType surface; // surface classification
        public int nearestIndex;
    }

    public class TrackSpline
    {
        public readonly TrackConfig Config;
        public readonly int Count;
        public readonly float TotalLength;

        // Surface physical properties (tunable public fields)
        public float asphaltGrip = 1.0f;
        public float kerbGrip    = 0.92f;
        public float gravelGrip  = 0.45f;
        public float grassGrip   = 0.55f;

        public float asphaltDrag = 1.0f;
        public float kerbDrag    = 1.05f;
        public float gravelDrag  = 3.50f;
        public float grassDrag   = 1.80f;

        public float kerbWidth   = 1.0f; // m wide inside corners
        public float gravelWidth = 8.0f; // m wide outside track/kerb

        // Cached precomputed arrays for O(1) query performance
        readonly float[] _x;
        readonly float[] _y;
        readonly float[] _z;
        readonly float[] _s;
        readonly float[] _width;
        readonly float[] _curvature;
        readonly float[] _heading;
        readonly float[] _slope;

        public TrackSpline(TrackConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Count = config.samples.Count;
            TotalLength = config.total_length_m;

            _x = new float[Count];
            _y = new float[Count];
            _z = new float[Count];
            _s = new float[Count];
            _width = new float[Count];
            _curvature = new float[Count];
            _heading = new float[Count];
            _slope = new float[Count];

            for (int i = 0; i < Count; i++)
            {
                var smp = config.samples[i];
                _x[i] = smp.x;
                _y[i] = smp.y;
                _z[i] = smp.z;
                _s[i] = smp.s;
                _width[i] = smp.width;
                _curvature[i] = smp.curvature;
            }

            // Precompute tangents, heading, and slopes
            for (int i = 0; i < Count; i++)
            {
                int next = (i + 1) % Count;
                int prev = (i - 1 + Count) % Count;

                float dx = _x[next] - _x[prev];
                float dy = _y[next] - _y[prev];
                float dz = _z[next] - _z[prev];
                float ds = 4.0f; // 2 steps of 2m

                // Heading: angle in X-Z plane clockwise from +Z: atan2(dx, dz)
                _heading[i] = MathF.Atan2(dx, dz);
                _slope[i]   = dy / ds;
            }
        }

        /// Cached local search around hintIndex for fast query (< 0.05 ms).
        public TrackQueryResult Query(float px, float pz, int hintIndex = 0)
        {
            if (Count == 0) return default;

            int bestIdx = hintIndex;
            float bestDistSq = float.MaxValue;

            // Local search window around hintIndex (+- 35 samples = +- 70 m)
            int searchRadius = 35;
            for (int k = -searchRadius; k <= searchRadius; k++)
            {
                int idx = (hintIndex + k) % Count;
                if (idx < 0) idx += Count;

                float dx = px - _x[idx];
                float dz = pz - _z[idx];
                float dsq = dx * dx + dz * dz;
                if (dsq < bestDistSq)
                {
                    bestDistSq = dsq;
                    bestIdx = idx;
                }
            }

            // If hint was far off (> 50m distance), fallback to global scan
            if (bestDistSq > 2500f)
            {
                for (int i = 0; i < Count; i++)
                {
                    float dx = px - _x[i];
                    float dz = pz - _z[i];
                    float dsq = dx * dx + dz * dz;
                    if (dsq < bestDistSq)
                    {
                        bestDistSq = dsq;
                        bestIdx = i;
                    }
                }
            }

            // Project onto segment (prev->bestIdx or bestIdx->next)
            int i0 = (bestIdx - 1 + Count) % Count;
            int i1 = bestIdx;
            int i2 = (bestIdx + 1) % Count;

            // Choose closer segment
            float seg0X = _x[i1] - _x[i0], seg0Z = _z[i1] - _z[i0];
            float seg1X = _x[i2] - _x[i1], seg1Z = _z[i2] - _z[i1];

            float len0Sq = seg0X * seg0X + seg0Z * seg0Z;
            float len1Sq = seg1X * seg1X + seg1Z * seg1Z;

            float t0 = len0Sq > 1e-4f ? ((px - _x[i0]) * seg0X + (pz - _z[i0]) * seg0Z) / len0Sq : 0f;
            float t1 = len1Sq > 1e-4f ? ((px - _x[i1]) * seg1X + (pz - _z[i1]) * seg1Z) / len1Sq : 0f;

            int segStart, segEnd;
            float tParam;
            if (t1 >= 0f)
            {
                segStart = i1;
                segEnd = i2;
                tParam = Math.Clamp(t1, 0f, 1f);
            }
            else
            {
                segStart = i0;
                segEnd = i1;
                tParam = Math.Clamp(t0, 0f, 1f);
            }

            float cx = _x[segStart] + tParam * (_x[segEnd] - _x[segStart]);
            float cz = _z[segStart] + tParam * (_z[segEnd] - _z[segStart]);
            float cy = _y[segStart] + tParam * (_y[segEnd] - _y[segStart]);

            // Arc length s
            float s0 = _s[segStart];
            float s1 = _s[segEnd];
            if (s1 < s0) s1 += TotalLength; // wrap-around S/F
            float s = (s0 + tParam * (s1 - s0)) % TotalLength;

            // Tangent unit vector
            float tx = _x[segEnd] - _x[segStart];
            float tz = _z[segEnd] - _z[segStart];
            float tLen = MathF.Sqrt(tx * tx + tz * tz);
            if (tLen > 1e-5f) { tx /= tLen; tz /= tLen; }

            // Normal unit vector (left of driving direction in X-Z: 90 deg counter-clockwise)
            // Left of (tx, tz): (-tz, tx)
            float nx = -tz;
            float nz = tx;

            // Lateral offset: dot(P - C, N)
            float lateralOffset = (px - cx) * nx + (pz - cz) * nz;

            float w = _width[bestIdx];
            float curvature = _curvature[bestIdx];
            float heading = _heading[bestIdx];
            float slope = _slope[bestIdx];

            SurfaceType surface = ClassifySurface(s, lateralOffset, w, curvature);

            return new TrackQueryResult
            {
                s = s,
                lateralOffset = lateralOffset,
                headingRad = heading,
                curvature = curvature,
                heightY = cy,
                slope = slope,
                trackWidth = w,
                surface = surface,
                nearestIndex = bestIdx
            };
        }

        public SurfaceType ClassifySurface(float s, float lateralOffset, float trackWidth, float curvature)
        {
            float halfW = trackWidth * 0.5f;
            float absOffset = MathF.Abs(lateralOffset);

            // Inside track edge -> Asphalt
            if (absOffset <= halfW)
                return SurfaceType.Asphalt;

            // Kerb: 1.0 m wide at the inside of corners
            // Positive curvature = turning left -> inside is left (lateralOffset > 0)
            // Negative curvature = turning right -> inside is right (lateralOffset < 0)
            bool isInside = (curvature > 0.005f && lateralOffset > 0f) ||
                            (curvature < -0.005f && lateralOffset < 0f);

            if (isInside && absOffset <= halfW + kerbWidth)
                return SurfaceType.Kerb;

            // Runoff: gravel outside kerb / track
            if (absOffset <= halfW + kerbWidth + gravelWidth)
                return SurfaceType.Gravel;

            // Otherwise grass
            return SurfaceType.Grass;
        }

        public float GetGripMultiplier(SurfaceType surface)
        {
            return surface switch
            {
                SurfaceType.Asphalt => asphaltGrip,
                SurfaceType.Kerb    => kerbGrip,
                SurfaceType.Gravel  => gravelGrip,
                SurfaceType.Grass   => grassGrip,
                _ => 1.0f
            };
        }

        public float GetDragFactor(SurfaceType surface)
        {
            return surface switch
            {
                SurfaceType.Asphalt => asphaltDrag,
                SurfaceType.Kerb    => kerbDrag,
                SurfaceType.Gravel  => gravelDrag,
                SurfaceType.Grass   => grassDrag,
                _ => 1.0f
            };
        }

        /// Speed profile helper: v_max(s) from curvature with aero, forward and backward braking/accel pass.
        public float[] ComputeSpeedProfile(float maxLatG = 2.8f, float maxAccG = 1.2f, float maxDecelG = 3.5f)
        {
            float[] vProfile = new float[Count];
            float g = 9.81f;

            // 1. Curvature grip limit pass: v_corner = sqrt(ay_max * R) = sqrt(ay_max / |k|)
            for (int i = 0; i < Count; i++)
            {
                float absK = MathF.Abs(_curvature[i]);
                if (absK > 1e-4f)
                {
                    float vMaxGrip = MathF.Sqrt(maxLatG * g / absK);
                    vProfile[i] = MathF.Min(vMaxGrip, 350f / 3.6f); // cap at 350 km/h
                }
                else
                {
                    vProfile[i] = 350f / 3.6f;
                }
            }

            // 2. Backward braking pass (ensures car can brake down in time for corners)
            float ds = TotalLength / Count;
            float maxA_brake = maxDecelG * g;
            for (int iter = 0; iter < 2; iter++) // 2 passes to handle cyclic loop
            {
                for (int i = Count - 1; i >= 0; i--)
                {
                    int next = (i + 1) % Count;
                    float vNext = vProfile[next];
                    float vAllowed = MathF.Sqrt(vNext * vNext + 2.0f * maxA_brake * ds);
                    if (vAllowed < vProfile[i])
                        vProfile[i] = vAllowed;
                }
            }

            // 3. Forward acceleration pass
            float maxA_acc = maxAccG * g;
            for (int iter = 0; iter < 2; iter++)
            {
                for (int i = 0; i < Count; i++)
                {
                    int prev = (i - 1 + Count) % Count;
                    float vPrev = vProfile[prev];
                    float vAllowed = MathF.Sqrt(vPrev * vPrev + 2.0f * maxA_acc * ds);
                    if (vAllowed < vProfile[i])
                        vProfile[i] = vAllowed;
                }
            }

            return vProfile;
        }
    }
}

