// Powertrain.cs — Pure C#, no UnityEngine. ICE torque curve, 8-speed auto gearbox, electric placeholder.
using System;

namespace F1Sim.Simulation
{
    public class PowertrainState
    {
        public int   Gear      = 1;        // 1..8
        public float EngineRpm = 1000f;
        public float WheelOmegaRL;         // rear-left wheel angular velocity (rad/s)
        public float WheelOmegaRR;         // rear-right wheel angular velocity (rad/s)
        public bool  IsShifting;
        public float ShiftTimer;
        public int   TargetGear = 1;

        public void Reset()
        {
            Gear          = 1;
            EngineRpm     = 1000f;
            WheelOmegaRL  = 0f;
            WheelOmegaRR  = 0f;
            IsShifting    = false;
            ShiftTimer    = 0f;
            TargetGear    = 1;
        }
    }

    public static class Powertrain
    {
        // ICE torque curve: array of (RPM, torque Nm) pairs, monotone in RPM.
        // Hand-tuned so peak ICE power is ~400 kW around 11,500-12,000 RPM (332 Nm @ 11,500 RPM = 399.8 kW).
        // Torque in Nm, power = torque * RPM * 2π / 60.
        static readonly float[] TorqueRpmTable =
        {
            1000f, 2000f, 4000f, 6000f, 8000f, 10000f, 11500f, 13000f, 14000f, 15000f
        };
        static readonly float[] TorqueNmTable =
        {
            100f, 180f, 270f, 320f, 345f, 350f, 332f, 290f, 260f, 230f
        };

        // Gear ratios (G1..G8) and final drive.
        // finalDrive=8.5, tireRadius=0.330m → top speed in 8th at 15000 RPM ≈ 338 km/h.
        public static float GearRatio(CarParams p, int gear)
        {
            int g = Math.Clamp(gear, 1, 8) - 1;
            return p.gearRatios[g];
        }

        static float LookupTorque(float rpm)
        {
            rpm = Math.Clamp(rpm, TorqueRpmTable[0], TorqueRpmTable[^1]);
            for (int i = 1; i < TorqueRpmTable.Length; i++)
            {
                if (rpm <= TorqueRpmTable[i])
                {
                    float t = (rpm - TorqueRpmTable[i - 1]) / (TorqueRpmTable[i] - TorqueRpmTable[i - 1]);
                    return TorqueNmTable[i - 1] + t * (TorqueNmTable[i] - TorqueNmTable[i - 1]);
                }
            }
            return TorqueNmTable[^1];
        }

        /// Returns (wheelTorque Nm delivered to rear wheels, engineRpm, gear).
        /// Updates PowertrainState in place.
        public static (float wheelTorque, float rpm, int gear) Step(
            CarParams p, PowertrainState s, float throttle, float vx, float dt,
            float fzRL, float fzRR)
        {
            float tireRadius = p.tireRadius;

            // --- Gearbox logic ---
            if (s.IsShifting)
            {
                s.ShiftTimer -= dt;
                if (s.ShiftTimer <= 0f)
                {
                    s.Gear      = s.TargetGear;
                    s.IsShifting = false;
                }
                // During shift: torque cut
                s.EngineRpm = Math.Clamp(s.EngineRpm, p.idleRpm, p.redlineRpm);
                return (0f, s.EngineRpm, s.Gear);
            }

            float gr = GearRatio(p, s.Gear) * p.finalDrive;
            // Estimate engine RPM from wheel speed
            float wheelOmegaAvg = MathF.Max(vx / tireRadius, 0f);
            float engineOmegaEst = wheelOmegaAvg * gr;
            float rpmEst = engineOmegaEst * 60f / (2f * MathF.PI);
            rpmEst = Math.Clamp(rpmEst, p.idleRpm, p.redlineRpm);
            s.EngineRpm = rpmEst;

            // Upshift: near 95% redline
            float upshiftRpm = p.redlineRpm * 0.95f;
            if (s.Gear < 8 && s.EngineRpm >= upshiftRpm)
            {
                s.TargetGear = s.Gear + 1;
                s.IsShifting  = true;
                s.ShiftTimer  = p.shiftTime;
                return (0f, s.EngineRpm, s.Gear);
            }

            // Downshift: when RPM drops below downshift threshold and not already in 1st
            float downshiftRpm = p.redlineRpm * p.downshiftFrac;
            // Hysteresis: only downshift if next gear RPM would still be above idle
            if (s.Gear > 1 && s.EngineRpm < downshiftRpm)
            {
                float grLower  = GearRatio(p, s.Gear - 1) * p.finalDrive;
                float rpmLower = wheelOmegaAvg * grLower * 60f / (2f * MathF.PI);
                if (rpmLower < p.redlineRpm * 0.92f) // hysteresis: won't immediately upshift again
                {
                    s.TargetGear = s.Gear - 1;
                    s.IsShifting  = true;
                    s.ShiftTimer  = p.shiftTime;
                    return (0f, s.EngineRpm, s.Gear);
                }
            }

            // --- Torque computation ---
            float iceTorque = LookupTorque(s.EngineRpm) * throttle;

            // TODO M4: replace with energy system
            // Flat 350 kW at full throttle (2026 MGU-K limit), tapering linearly to 0 between 290 and 345 km/h
            float speedKmh = vx * 3.6f;
            float deployTaper = 1f;
            if (speedKmh > p.electricTaperStartKmh)
            {
                deployTaper = Math.Clamp(1f - (speedKmh - p.electricTaperStartKmh) / (p.electricTaperEndKmh - p.electricTaperStartKmh), 0f, 1f);
            }
            float electricPower = p.electricDeployW * throttle * deployTaper;
            float activeEngineOmega = MathF.Max(s.EngineRpm * (2f * MathF.PI / 60f), 1f);
            float electricTorque = electricPower / activeEngineOmega;

            float totalTorque = iceTorque + electricTorque;

            // Wheel torque → drive force (single shaft, open diff + lock fraction)
            float wheelTorque = totalTorque * gr * p.drivetrainEff;

            return (wheelTorque, s.EngineRpm, s.Gear);
        }
    }
}

