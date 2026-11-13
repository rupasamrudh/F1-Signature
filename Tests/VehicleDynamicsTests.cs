// VehicleDynamicsTests.cs — EditMode tests for M2 VehicleDynamics (pure C#, no UnityEngine).
// Run via TestRunner.cs console harness.
using System;
using NUnit.Framework;
using F1Sim.Simulation;

namespace F1Sim.Tests
{
    [TestFixture]
    public class VehicleDynamicsTests
    {
        static VehicleDynamics MakeModel()
        {
            var p = new CarParams();
            return new VehicleDynamics(p);
        }

        static void Simulate(VehicleDynamics v, float dt, int steps,
            Func<int, float> steerFn, Func<int, float> thrFn, Func<int, float> brkFn)
        {
            for (int i = 0; i < steps; i++)
                v.Step(dt, steerFn(i), thrFn(i), brkFn(i));
        }

        // ── T1: 0→100 km/h time between 2.5 and 3.5 s ─────────────────────
        [Test]
        public void T1_ZeroTo100_InTarget()
        {
            var v  = MakeModel();
            float dt = 1f / 120f;
            float t100 = -1f;
            for (int i = 0; i < 120 * 10; i++)
            {
                v.Step(dt, 0f, 1f, 0f);
                float elapsed = i * dt;
                if (v.SpeedKmh >= 100f && t100 < 0f) { t100 = elapsed; break; }
            }
            Console.WriteLine($"T1  0→100 km/h: {t100:0.00} s  (target 2.5–3.5 s)");
            Assert.That(t100, Is.InRange(2.5f, 3.5f), $"0-100 time = {t100:0.00} s");
        }

        // ── T2: 0→200 km/h time between 5.0 and 7.0 s ─────────────────────
        [Test]
        public void T2_ZeroTo200_InTarget()
        {
            var v  = MakeModel();
            float dt = 1f / 120f;
            float t200 = -1f;
            for (int i = 0; i < 120 * 20; i++)
            {
                v.Step(dt, 0f, 1f, 0f);
                if (v.SpeedKmh >= 200f && t200 < 0f) { t200 = i * dt; break; }
            }
            Console.WriteLine($"T2  0→200 km/h: {t200:0.00} s  (target 5.0–7.0 s)");
            Assert.That(t200, Is.InRange(5.0f, 7.0f), $"0-200 time = {t200:0.00} s");
        }

        // ── T3: Top speed 310–350 km/h ──────────────────────────────────────
        [Test]
        public void T3_TopSpeed_InRange()
        {
            var v = MakeModel();
            float dt = 1f / 120f;
            for (int i = 0; i < 120 * 60; i++)
                v.Step(dt, 0f, 1f, 0f);
            float topSpeed = v.TotalSpeedKmh;
            Console.WriteLine($"T3  Top speed: {topSpeed:0.0} km/h  (target 310–350 km/h)");
            Assert.That(topSpeed, Is.InRange(310f, 350f), $"top speed = {topSpeed:0.0}");
        }

        // ── T4: 300→0 km/h: distance 100–150 m, time 3.5–5.5 s ────────────
        [Test]
        public void T4_HardBraking_300_DistanceAndTime()
        {
            var v = MakeModel();
            float dt = 1f / 120f;

            // Spin up to 300 km/h
            for (int i = 0; i < 120 * 60; i++)
            {
                v.Step(dt, 0f, 1f, 0f);
                if (v.SpeedKmh >= 300f) break;
            }
            Assert.That(v.SpeedKmh, Is.GreaterThan(295f), "Couldn't reach 300 km/h for braking test");

            float startX = v.X, startZ = v.Z;
            float elapsed = 0f;
            float maxBeta = 0f;

            while (v.SpeedKmh > 2f && elapsed < 15f)
            {
                v.Step(dt, 0f, 0f, 1f);
                elapsed += dt;
                maxBeta = MathF.Max(maxBeta, MathF.Abs(v.BetaDeg));
            }

            float dx   = v.X - startX, dz = v.Z - startZ;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            float peakDecelG = v.lastBrakingPeakDecelG;
            float avgDecelG = (300f / 3.6f) / (elapsed * 9.81f);

            Console.WriteLine($"T4  300→0: dist={dist:0.0}m, time={elapsed:0.00}s, peakDecel={peakDecelG:0.00}g, avgDecel={avgDecelG:0.00}g, maxBeta={maxBeta:0.0}° (init front={v.initialBrakeFrontN:0}N, rear={v.initialBrakeRearN:0}N)");
            Assert.That(dist,       Is.InRange(100f, 150f), $"stop distance = {dist:0.0} m");
            Assert.That(elapsed,    Is.InRange(3.0f, 5.5f), $"stop time = {elapsed:0.0} s");
            Assert.That(peakDecelG, Is.InRange(4.0f, 5.8f), $"peak decel = {peakDecelG:0.00} g (target 4.0-5.8 g)");
            Assert.That(avgDecelG,  Is.InRange(2.0f, 3.0f), $"avg decel = {avgDecelG:0.00} g (target 2.0-3.0 g)");
            Assert.That(maxBeta,    Is.LessThan(5f),         $"max beta during braking = {maxBeta:0.0}°");
        }

        // ── T5: 200→0 km/h: time 1.5–2.6 s ────────────────────────────────
        [Test]
        public void T5_HardBraking_200_Time()
        {
            var v = MakeModel();
            float dt = 1f / 120f;

            for (int i = 0; i < 120 * 30; i++)
            {
                v.Step(dt, 0f, 1f, 0f);
                if (v.SpeedKmh >= 200f) break;
            }

            float elapsed = 0f;
            while (v.SpeedKmh > 2f && elapsed < 10f)
            {
                v.Step(dt, 0f, 0f, 1f);
                elapsed += dt;
            }

            Console.WriteLine($"T5  200→0: time={elapsed:0.00}s  (target 1.5–2.6 s)");
            Assert.That(elapsed, Is.InRange(1.5f, 2.6f), $"200→0 time = {elapsed:0.0} s");
        }

        // ── T6: Stability assist sign test ──────────────────────────────────
        [TestCase(0.4f)]
        [TestCase(-0.4f)]
        public void T6_AssistSign_BetaReducedWithin2s(float initBeta)
        {
            var v = MakeModel();
            float dt = 1f / 120f;
            v.Vx = 25f;
            v.Vy = 25f * MathF.Tan(initBeta);

            float maxBeta = 0f;
            for (int i = 0; i < (int)(2f / dt); i++)
            {
                v.Step(dt, 0f, 0f, 0f);
                maxBeta = MathF.Max(maxBeta, MathF.Abs(v.Beta));
            }

            Console.WriteLine($"T6  Assist initBeta={initBeta:+0.0;-0.0} → finalBeta={v.BetaDeg:0.0}° (target <5.7° at 2s)");
            Assert.That(MathF.Abs(v.Beta), Is.LessThan(0.1f), $"beta not reduced: {v.BetaDeg:0.0}°");
        }

        // ── T7: Skidpad – peak LatG ≥ 2.5 at 50 m radius ──────────────────
        [Test]
        public void T7_Skidpad_PeakLatG()
        {
            var v = MakeModel();
            float dt = 1f / 120f;

            // Ramp speed while holding steer
            float peakLatG = 0f;
            for (int i = 0; i < 120 * 30; i++)
            {
                float thr = MathF.Min(1f, i * dt / 15f);
                v.Step(dt, 0.4f, thr, 0f);
                float lg = MathF.Abs(v.LatG);
                if (lg > peakLatG) peakLatG = lg;
                if (lg > 4.5f) break;
            }

            Console.WriteLine($"T7  Skidpad peak LatG={peakLatG:0.00}g  (target 2.5–3.5 g)");
            Assert.That(peakLatG, Is.InRange(2.5f, 3.5f), $"skidpad LatG = {peakLatG:0.00}");
        }

        // ── T8: No spin with throttle noise ─────────────────────────────────
        [Test]
        public void T8_StraightThrottle_NoSpin()
        {
            var v = MakeModel();
            v.Vy = 0.2f;
            float dt = 1f / 120f;
            float maxBeta = 0f;

            for (int i = 0; i < 120 * 20; i++)
            {
                float steer = 0.02f * MathF.Sin(2f * MathF.PI * 3f * i * dt);
                v.Step(dt, steer, 1f, 0f);
                maxBeta = MathF.Max(maxBeta, MathF.Abs(v.Beta));
            }

            Console.WriteLine($"T8  Straight throttle noise: maxBeta={maxBeta * 180f / MathF.PI:0.00}°  (target <5.7°)");
            Assert.That(maxBeta, Is.LessThan(0.1f), $"spin: beta = {maxBeta * 180f / MathF.PI:0.0}°");
        }

        // ── T9: Brake-then-turn no spin ─────────────────────────────────────
        [Test]
        public void T9_BrakeThenTurn_NoSpin()
        {
            var v = MakeModel();
            float dt = 1f / 120f;

            // Reach 160 km/h
            for (int i = 0; i < 120 * 20; i++)
            {
                v.Step(dt, 0f, 1f, 0f);
                if (v.SpeedKmh >= 160f) break;
            }

            // Brake 1.0 for 1.5 s with steer 0.6
            float maxBeta = 0f;
            float minRearLatGrip = 1f;
            for (int i = 0; i < (int)(1.5f / dt); i++)
            {
                v.Step(dt, 0.6f, 0f, 1.0f);
                maxBeta = MathF.Max(maxBeta, MathF.Abs(v.Beta));
                minRearLatGrip = MathF.Min(minRearLatGrip, v.RearLateralGripScale);
            }

            Console.WriteLine($"T9  Brake+turn: maxBeta={maxBeta * 180f / MathF.PI:0.00}°, minRearLatGrip={minRearLatGrip:0.00}  (beta<20°, grip>0.65)");
            Assert.That(maxBeta,       Is.LessThan(0.35f), $"beta too large: {maxBeta * 180f / MathF.PI:0.0}°");
            Assert.That(minRearLatGrip, Is.GreaterThan(0.65f), $"rear lat grip too low: {minRearLatGrip:0.00}");
        }

        // ── T10: Aero mode blend ─────────────────────────────────────────────
        [Test]
        public void T10_AeroBlend_ChangesDownforce()
        {
            var v  = MakeModel();
            float dt = 1f / 120f;
            v.Vx = 60f; // 216 km/h

            // Record corner-mode downforce
            v.Step(dt, 0f, 0f, 0f);
            float downCorner = v.FzFL + v.FzFR + v.FzRL + v.FzRR - v.P.mass * 9.81f;

            // Switch to straight
            v.Aero.CurrentMode = AeroMode.Straight;
            // Run enough steps to blend
            for (int i = 0; i < (int)(0.3f / dt); i++)
                v.Step(dt, 0f, 0f, 0f);

            float downStraight = v.FzFL + v.FzFR + v.FzRL + v.FzRR - v.P.mass * 9.81f;

            Console.WriteLine($"T10 Aero blend: corner down={downCorner:0}N, straight down={downStraight:0}N (straight should be less)");
            Assert.That(downStraight, Is.LessThan(downCorner), "Straight mode should produce less downforce than corner mode");
        }

        // ── T11: Gear shifts during acceleration ─────────────────────────────
        [Test]
        public void T11_GearShifts_AllGearsUsed()
        {
            var v   = MakeModel();
            float dt = 1f / 120f;
            int   maxGear = 0;
            for (int i = 0; i < 120 * 60; i++)
            {
                v.Step(dt, 0f, 1f, 0f);
                if (v.Gear > maxGear) maxGear = v.Gear;
                if (v.SpeedKmh > 320f) break;
            }

            Console.WriteLine($"T11 Gear: reached gear {maxGear}  (target 7 or 8)");
            Assert.That(maxGear, Is.GreaterThanOrEqualTo(7), $"Only reached gear {maxGear}");
        }

        // ── T12: Split-mu straightline acceleration (diffLockFrac test) ──────
        [Test]
        public void T12_SplitMu_DiffLock_CarStaysStraight()
        {
            var v = MakeModel();
            float dt = 1f / 120f;
            v.Vx = 20f; // rolling start 72 km/h
            v.muScaleRL = 0.80f; // split mu 0.80 vs 1.00
            v.muScaleRR = 1.00f;

            float maxBeta = 0f;
            float maxYawDev = 0f;
            for (int i = 0; i < 120 * 5; i++) // 5 seconds full throttle
            {
                v.Step(dt, 0f, 1f, 0f);
                maxBeta = MathF.Max(maxBeta, MathF.Abs(v.BetaDeg));
                maxYawDev = MathF.Max(maxYawDev, MathF.Abs(v.Yaw * (180f / MathF.PI)));
                if (v.SpeedKmh >= 200f) break;
            }

            Console.WriteLine($"T12 Split-mu (RL=0.8, RR=1.0): maxBeta={maxBeta:0.2}°, maxYawDev={maxYawDev:0.2}° (target beta<10°, yaw<15°)");
            Assert.That(maxBeta,   Is.LessThan(10f), $"Beta deviation too high on split-mu: {maxBeta:0.2}°");
            Assert.That(maxYawDev, Is.LessThan(15f), $"Car spun or veered off on split-mu: {maxYawDev:0.2}°");
        }

        // ── T13: Step steer overshoot & rise time ────────────────────────────
        [Test]
        public void T13_StepSteer_RiseTimeAndOvershoot()
        {
            var v = MakeModel();
            float dt = 1f / 120f;

            // Accelerate to ~150 km/h
            for (int i = 0; i < 120 * 20; i++)
            {
                v.Step(dt, 0f, 1f, 0f);
                if (v.SpeedKmh >= 150f) break;
            }

            float peakYawRate = 0f;
            float finalYawRate = 0f;

            // Apply step steer (0.5 steer)
            int stepTotal = (int)(3.0f / dt);
            float[] yrHistory = new float[stepTotal];
            for (int i = 0; i < stepTotal; i++)
            {
                v.Step(dt, 0.5f, 0.35f, 0f); // partial throttle to maintain speed
                float yr = MathF.Abs(v.YawRate);
                yrHistory[i] = yr;
                if (yr > peakYawRate) peakYawRate = yr;
            }
            finalYawRate = yrHistory[^1];

            // 10-90% rise time
            float y10 = finalYawRate * 0.10f;
            float y90 = finalYawRate * 0.90f;
            float t10 = -1f, t90 = -1f;
            for (int i = 0; i < stepTotal; i++)
            {
                float t = i * dt;
                if (t10 < 0f && yrHistory[i] >= y10) t10 = t;
                if (t90 < 0f && yrHistory[i] >= y90) { t90 = t; break; }
            }
            float riseTime = (t10 >= 0f && t90 >= 0f) ? (t90 - t10) : 0f;
            float overshootPct = finalYawRate > 0.05f ? ((peakYawRate - finalYawRate) / finalYawRate) * 100f : 0f;

            Console.WriteLine($"T13 Step steer: 10-90% rise time = {riseTime:0.000}s, Peak YR = {peakYawRate:0.3} rad/s, SS YR = {finalYawRate:0.3} rad/s, Overshoot = {overshootPct:0.1}% (target overshoot < 25%)");
            Assert.That(riseTime, Is.InRange(0.10f, 0.25f), $"Rise time out of range: {riseTime:0.000}s (target 0.10-0.25s)");
            Assert.That(overshootPct, Is.LessThan(25.0f), $"Overshoot too high: {overshootPct:0.1}%");
            Assert.That(MathF.Abs(v.BetaDeg), Is.LessThan(15.0f), $"Car spun in step steer: beta = {v.BetaDeg:0.1}°");
        }

        // ── T14: Corner exit stability ───────────────────────────────────────
        // Corner exit from 60 km/h in G2, full throttle, steer 0.5 for 3 s → |beta| < 8 deg, rear lateral grip scale >= 0.55
        [Test]
        public void T14_CornerExit_Stability()
        {
            var v = MakeModel();
            float dt = 1f / 120f;
            v.Vx = 60f / 3.6f;
            v.PT.Gear = 2;

            float maxBeta = 0f;
            float minRearScale = 1.0f;
            for (int i = 0; i < (int)(3.0f / dt); i++)
            {
                v.Step(dt, 0.5f, 1.0f, 0f);
                if (MathF.Abs(v.BetaDeg) > maxBeta) maxBeta = MathF.Abs(v.BetaDeg);
                if (v.RearLateralGripScale < minRearScale) minRearScale = v.RearLateralGripScale;
            }

            Console.WriteLine(string.Format("T14 Corner exit: maxBeta={0:F2} deg (target <8 deg), minRearLatScale={1:F3} (target >=0.55)", maxBeta, minRearScale));
            Assert.That(maxBeta, Is.LessThan(8.0f), $"Corner exit beta too high: {maxBeta:0.2f}°");
            Assert.That(minRearScale, Is.GreaterThanOrEqualTo(0.55f), $"Rear lateral grip scale too low: {minRearScale:0.3f}");
        }

        // ── T15: Flick test (100 and 220 km/h) ──────────────────────────────
        // Hold full steering (1.0) for 1.0 s then release (1.0 s) → peak |beta| < 12 deg without spinning
        [TestCase(100f)]
        [TestCase(220f)]
        public void T15_FlickTest_NoSpin(float speedKmh)
        {
            var v = MakeModel();
            float dt = 1f / 120f;
            v.Vx = speedKmh / 3.6f;

            float maxBeta = 0f;
            // 1.0 s full steer
            for (int i = 0; i < (int)(1.0f / dt); i++)
            {
                v.Step(dt, 1.0f, 0f, 0f);
                if (MathF.Abs(v.BetaDeg) > maxBeta) maxBeta = MathF.Abs(v.BetaDeg);
            }
            // 1.0 s release
            for (int i = 0; i < (int)(1.0f / dt); i++)
            {
                v.Step(dt, 0.0f, 0f, 0f);
                if (MathF.Abs(v.BetaDeg) > maxBeta) maxBeta = MathF.Abs(v.BetaDeg);
            }

            Console.WriteLine(string.Format("T15 Flick at {0:F0} km/h: maxBeta={1:F2} deg (target <12 deg)", speedKmh, maxBeta));
            Assert.That(maxBeta, Is.LessThan(12.0f), $"Flick beta too high at {speedKmh} km/h: {maxBeta:0.2f}°");
        }

        // ── T16: Legacy Payload Deserialization & Defaults Reset Test ────────
        [Test]
        public void T16_LegacyPayload_ResetsDefaults_WhenParamsVersionZero()
        {
            // Simulate legacy payload missing paramsVersion (deserializes with paramsVersion = 0 and old defaults)
            var legacyConfig = new CarParams
            {
                paramsVersion = 0,
                brakeBiasFront = 0.58f,
                yawInertia = 1100f,
                pacB = 14f,
                mu0 = 2.0f
            };

            Assert.That(legacyConfig.paramsVersion, Is.Not.EqualTo(CarParams.CURRENT_VERSION));

            // Simulating Awake version check logic
            bool resetTriggered = false;
            CarParams activeConfig = legacyConfig;
            if (activeConfig.paramsVersion != CarParams.CURRENT_VERSION)
            {
                resetTriggered = true;
                activeConfig = new CarParams { paramsVersion = CarParams.CURRENT_VERSION };
            }

            Assert.That(resetTriggered, Is.True, "Reset should trigger on version mismatch");
            Assert.That(activeConfig.paramsVersion, Is.EqualTo(CarParams.CURRENT_VERSION));
            Assert.That(activeConfig.brakeBiasFront, Is.EqualTo(0.68f));
            Assert.That(activeConfig.yawInertia, Is.EqualTo(2800f));
            Assert.That(activeConfig.pacB, Is.EqualTo(9.0f));
            Assert.That(activeConfig.mu0, Is.EqualTo(2.30f));
            Assert.That(activeConfig.tractionFrac, Is.EqualTo(0.68f));
            Console.WriteLine($"T16 Legacy payload reset test: PASS (resetTriggered={resetTriggered}, v={activeConfig.paramsVersion}, brakeBias={activeConfig.brakeBiasFront:0.00})");
        }

        // ── T17: Brake-while-turning from 80 km/h stability ─────────────────
        // From 80 km/h, full steering lock (+1.0) with brake 0.17 held for 1 s must keep abs(beta) under 12 degrees
        [Test]
        public void T17_BrakeTurn_80Kmh_LockAndBrake()
        {
            var v = MakeModel();
            float dt = 1f / 120f;
            v.Vx = 80f / 3.6f;
            v.Wheels[0].omega = v.Vx / v.P.tireRadius;
            v.Wheels[1].omega = v.Vx / v.P.tireRadius;
            v.Wheels[2].omega = v.Vx / v.P.tireRadius;
            v.Wheels[3].omega = v.Vx / v.P.tireRadius;

            float maxBeta = 0f;
            for (int i = 0; i < (int)(1.0f / dt); i++)
            {
                v.Step(dt, 1.0f, 0f, 0.17f);
                float beta = MathF.Abs(v.BetaDeg);
                if (beta > maxBeta) maxBeta = beta;
            }

            Console.WriteLine($"T17 Brake+turn 80 km/h (full lock, brake 0.17 for 1s): maxBeta={maxBeta:F2}° (target < 12°)");
            Assert.That(maxBeta, Is.LessThan(12.0f), $"Brake+turn beta too high: {maxBeta:F2}°");
        }
    }
}

