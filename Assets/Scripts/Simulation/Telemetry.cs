// Telemetry.cs — Pure C#, no UnityEngine. Records sim state at up to 120 Hz.
// File writing (Application.persistentDataPath) is done by TelemetryWriter in Vehicle/.
using System;
using System.Collections.Generic;
using System.Text;

namespace F1Sim.Simulation
{
    public struct TelemetryFrame
    {
        public float Time;
        public float Speed;    // km/h total
        public float Vx;       // m/s
        public float Vy;       // m/s
        public float YawRate;  // rad/s
        public float SteerAngle; // deg
        public float Throttle;
        public float Brake;
        public int   Gear;
        public float Rpm;
        public float FzFL, FzFR, FzRL, FzRR;  // N
        public float AlphaFL, AlphaFR, AlphaRL, AlphaRR;  // rad
        public float LongG;  // g
        public float LatG;   // g
        public int   AeroMode; // 0=Corner, 1=Straight
        public float PeakBrakeG; // g peak deceleration of last braking event
    }

    public class Telemetry
    {
        readonly List<TelemetryFrame> _frames;
        readonly int   _maxFrames;
        float          _lastRecordTime;
        readonly float _interval;  // s between records

        public int   Count => _frames.Count;
        public bool  IsRecording { get; private set; }

        public Telemetry(float hz = 120f, int maxSeconds = 300)
        {
            _interval  = 1f / hz;
            _maxFrames = (int)(hz * maxSeconds);
            _frames    = new List<TelemetryFrame>(_maxFrames);
        }

        public void StartRecording()
        {
            _frames.Clear();
            _lastRecordTime = -_interval;
            IsRecording = true;
        }

        public void StopRecording() => IsRecording = false;

        public void Clear() => _frames.Clear();

        /// Call this every FixedUpdate / simulation step.
        public void Record(float time, VehicleDynamics v, float throttle, float brake)
        {
            if (!IsRecording) return;
            if (time - _lastRecordTime < _interval) return;
            if (_frames.Count >= _maxFrames) return;

            _lastRecordTime = time;
            const float g = 9.81f;
            _frames.Add(new TelemetryFrame
            {
                Time        = time,
                Speed       = v.TotalSpeedKmh,
                Vx          = v.Vx,
                Vy          = v.Vy,
                YawRate     = v.R,
                SteerAngle  = v.FinalSteerAngleDeg,
                Throttle    = throttle,
                Brake       = brake,
                Gear        = v.Gear,
                Rpm         = v.EngineRpm,
                FzFL        = v.FzFL,
                FzFR        = v.FzFR,
                FzRL        = v.FzRL,
                FzRR        = v.FzRR,
                AlphaFL     = v.Wheels[0].alphaLag,
                AlphaFR     = v.Wheels[1].alphaLag,
                AlphaRL     = v.Wheels[2].alphaLag,
                AlphaRR     = v.Wheels[3].alphaLag,
                LongG       = v.Ax / g,
                LatG        = v.LatG,
                AeroMode    = (int)v.Aero.CurrentMode,
                PeakBrakeG  = v.lastBrakingPeakDecelG,
            });
        }

        static readonly string Header =
            "time,speed_kmh,vx,vy,yaw_rate,steer_deg,throttle,brake,gear,rpm," +
            "fz_fl,fz_fr,fz_rl,fz_rr,alpha_fl,alpha_fr,alpha_rl,alpha_rr," +
            "long_g,lat_g,aero_mode,peak_brake_g";

        public string ToCsv(CarParams p = null)
        {
            var sb = new StringBuilder();

            // Header lines with effective parameters if provided
            if (p != null)
            {
                sb.AppendLine("# --- Effective CarParams ---");
                var fields = typeof(CarParams).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var f in fields)
                {
                    object val = f.GetValue(p);
                    if (val is Array arr)
                        sb.AppendLine($"# {f.Name}={string.Join(",", (float[])arr)}");
                    else
                        sb.AppendLine($"# {f.Name}={val}");
                }
                sb.AppendLine("# ---------------------------");
            }

            sb.AppendLine(Header);
            foreach (var f in _frames)
            {
                sb.Append(f.Time.ToString("F4")); sb.Append(',');
                sb.Append(f.Speed.ToString("F2")); sb.Append(',');
                sb.Append(f.Vx.ToString("F3")); sb.Append(',');
                sb.Append(f.Vy.ToString("F3")); sb.Append(',');
                sb.Append(f.YawRate.ToString("F4")); sb.Append(',');
                sb.Append(f.SteerAngle.ToString("F2")); sb.Append(',');
                sb.Append(f.Throttle.ToString("F3")); sb.Append(',');
                sb.Append(f.Brake.ToString("F3")); sb.Append(',');
                sb.Append(f.Gear); sb.Append(',');
                sb.Append(f.Rpm.ToString("F0")); sb.Append(',');
                sb.Append(f.FzFL.ToString("F0")); sb.Append(',');
                sb.Append(f.FzFR.ToString("F0")); sb.Append(',');
                sb.Append(f.FzRL.ToString("F0")); sb.Append(',');
                sb.Append(f.FzRR.ToString("F0")); sb.Append(',');
                sb.Append(f.AlphaFL.ToString("F4")); sb.Append(',');
                sb.Append(f.AlphaFR.ToString("F4")); sb.Append(',');
                sb.Append(f.AlphaRL.ToString("F4")); sb.Append(',');
                sb.Append(f.AlphaRR.ToString("F4")); sb.Append(',');
                sb.Append(f.LongG.ToString("F3")); sb.Append(',');
                sb.Append(f.LatG.ToString("F3")); sb.Append(',');
                sb.Append(f.AeroMode.ToString()); sb.Append(',');
                sb.AppendLine(f.PeakBrakeG.ToString("F2"));
            }
            return sb.ToString();
        }

        /// Subset of frames as a CSV for a specific time window.
        public string ToCsv(float tStart, float tEnd)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (var f in _frames)
            {
                if (f.Time < tStart || f.Time > tEnd) continue;
                sb.Append(f.Time.ToString("F4")); sb.Append(',');
                sb.Append(f.Speed.ToString("F2")); sb.Append(',');
                sb.AppendLine(f.Gear.ToString());
            }
            return sb.ToString();
        }

        public IReadOnlyList<TelemetryFrame> Frames => _frames;
    }
}

