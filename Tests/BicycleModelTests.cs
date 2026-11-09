using System;
using NUnit.Framework;
using F1Sim.Simulation;

namespace F1Sim.Tests
{
    [TestFixture]
    public class BicycleModelTests
    {
        private const float dt = 1f / 120f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>
        /// T1. Straight line with disturbance: full throttle from standstill for 20 s,
        /// steer input = 0.02 * sin(2*pi*3*t), Vy initial 0.2 m/s.
        /// Expect: no spin (|beta| stays under 0.1 rad), speed increases steadily to at least 300 km/h,
        /// rear traction use never exceeds tractionFrac + 0.02.
        /// </summary>
        [Test]
        public void T1_StraightLineWithDisturbance_NoSpin_SpeedOver300_TractionCapped()
        {
            var model = new BicycleModel(new CarParams());
            model.Vy = 0.2f;

            float maxBeta = 0f;
            float maxTrac = 0f;

            for (float t = 0f; t < 20f; t += dt)
            {
                float steer = 0.02f * MathF.Sin(2f * MathF.PI * 3f * t);
                model.Step(dt, steer, 1.0f, 0f);

                Assert.That(IsFinite(model.Vx) && IsFinite(model.Vy) && IsFinite(model.R), "State non-finite in T1");

                float beta = MathF.Abs(model.Beta);
                if (beta > maxBeta) maxBeta = beta;
                if (model.RearTractionUse > maxTrac) maxTrac = model.RearTractionUse;

                Assert.That(beta, Is.LessThan(0.1f), $"Spun out in T1 at t={t:F2}s with beta={beta:F4} rad");
            }

            float finalSpeedKmh = model.Vx * 3.6f;
            Assert.That(maxBeta, Is.LessThan(0.1f), $"Peak |beta| exceeded 0.1 rad: {maxBeta:F4}");
            Assert.That(finalSpeedKmh, Is.GreaterThanOrEqualTo(300f), $"Final speed was {finalSpeedKmh:F1} km/h, expected >= 300");
            Assert.That(maxTrac, Is.LessThanOrEqualTo(model.P.tractionFrac + 0.02f),
                $"Rear traction use was {maxTrac:F4}, exceeded tractionFrac + 0.02");
        }

        /// <summary>
        /// T2. Same as T1 but with zero throttle for the first second, then full throttle,
        /// repeated from 40 km/h and 80 km/h starting speeds.
        /// </summary>
        [TestCase(40f)]
        [TestCase(80f)]
        public void T2_ThrottleDelayWithDisturbance_NoSpin_SpeedOver300(float startSpeedKmh)
        {
            var model = new BicycleModel(new CarParams());
            model.Vx = startSpeedKmh / 3.6f;
            model.Vy = 0.2f;

            float maxBeta = 0f;
            float maxTrac = 0f;

            for (float t = 0f; t < 20f; t += dt)
            {
                float steer = 0.02f * MathF.Sin(2f * MathF.PI * 3f * t);
                float thr = t < 1.0f ? 0f : 1.0f;
                model.Step(dt, steer, thr, 0f);

                Assert.That(IsFinite(model.Vx) && IsFinite(model.Vy) && IsFinite(model.R), "State non-finite in T2");

                float beta = MathF.Abs(model.Beta);
                if (beta > maxBeta) maxBeta = beta;
                if (model.RearTractionUse > maxTrac) maxTrac = model.RearTractionUse;

                Assert.That(beta, Is.LessThan(0.1f), $"Spun out in T2 at start {startSpeedKmh}km/h, t={t:F2}s with beta={beta:F4} rad");
            }

            float finalSpeedKmh = model.Vx * 3.6f;
            Assert.That(maxBeta, Is.LessThan(0.1f), $"Peak |beta| exceeded 0.1 rad: {maxBeta:F4}");
            Assert.That(finalSpeedKmh, Is.GreaterThanOrEqualTo(300f), $"Final speed was {finalSpeedKmh:F1} km/h, expected >= 300");
            Assert.That(maxTrac, Is.LessThanOrEqualTo(model.P.tractionFrac + 0.02f),
                $"Rear traction use was {maxTrac:F4}, exceeded tractionFrac + 0.02");
        }

        /// <summary>
        /// T3. Assist sign test: at Vx 25 m/s with initial beta of +0.4 rad, then of -0.4 rad,
        /// zero steering input, assist = 0.6. Expect |beta| below 0.1 rad within 2 s in both cases
        /// and the final steerAngle during the recovery has the sign that reduces beta.
        /// </summary>
        [TestCase(0.4f)]
        [TestCase(-0.4f)]
        public void T3_AssistSignTest_BetaReducedWithin2s_SteerSignCounteractsSlide(float initBeta)
        {
            var p = new CarParams();
            p.stabilityAssist = 0.6f;
            var model = new BicycleModel(p);
            model.Vx = 25f;
            model.Vy = 25f * MathF.Tan(initBeta);

            float betaAt2s = 0f;
            float earlySteerAngle = 0f;

            for (float t = 0f; t <= 2.0f; t += dt)
            {
                model.Step(dt, 0f, 0f, 0f);

                if (t >= 0.05f && t <= 0.25f && earlySteerAngle == 0f)
                {
                    earlySteerAngle = model.finalSteerAngle;
                }
                betaAt2s = model.Beta;
            }

            Assert.That(MathF.Abs(betaAt2s), Is.LessThan(0.1f),
                $"Beta after 2s was {betaAt2s:F4} rad ({betaAt2s * 57.29578f:F1}°), expected < 0.1 rad");

            // For initBeta > 0 (tail sliding left), front wheel must steer left (positive delta)
            // For initBeta < 0 (tail sliding right), front wheel must steer right (negative delta)
            if (initBeta > 0f)
            {
                Assert.That(earlySteerAngle, Is.GreaterThan(0f),
                    $"Assist steered in the wrong direction for positive beta: {earlySteerAngle:F4} rad");
            }
            else
            {
                Assert.That(earlySteerAngle, Is.LessThan(0f),
                    $"Assist steered in the wrong direction for negative beta: {earlySteerAngle:F4} rad");
            }
        }

        /// <summary>
        /// T4. Hard braking: from 300 km/h, brake 1.0 until stopped, steering 0.
        /// Expect it stops in a straight line (|beta| under 0.05 rad), stopping distance between 100 and 220 m,
        /// peak deceleration under 6.5 g.
        /// </summary>
        [Test]
        public void T4_HardBraking_StraightLine_StoppingDistanceAndDecelValid()
        {
            var model = new BicycleModel(new CarParams());
            model.Vx = 300f / 3.6f; // 83.33 m/s

            float startX = model.X;
            float startZ = model.Z;
            float maxBeta = 0f;
            float peakDecelG = 0f;
            float prevVx = model.Vx;

            while (model.Vx > 0.05f)
            {
                model.Step(dt, 0f, 0f, 1.0f);

                Assert.That(IsFinite(model.Vx) && IsFinite(model.Vy) && IsFinite(model.R), "State non-finite during braking");

                float beta = MathF.Abs(model.Beta);
                if (beta > maxBeta) maxBeta = beta;

                float decel = (prevVx - model.Vx) / dt / 9.81f;
                if (decel > peakDecelG) peakDecelG = decel;
                prevVx = model.Vx;
            }

            float stopDist = MathF.Sqrt((model.X - startX) * (model.X - startX) + (model.Z - startZ) * (model.Z - startZ));

            Assert.That(maxBeta, Is.LessThan(0.05f), $"Car deviated during straight braking: maxBeta={maxBeta:F4} rad");
            Assert.That(stopDist, Is.InRange(100f, 220f), $"Stopping distance was {stopDist:F1} m, expected 100-220 m");
            Assert.That(peakDecelG, Is.LessThan(6.5f), $"Peak deceleration was {peakDecelG:F2} g, expected < 6.5 g");
        }

        /// <summary>
        /// T5. Brake then turn: from 160 km/h, brake 0.6 for 1 s, release, full steering input for 4 s.
        /// Expect no spin (|beta| under 0.6 rad) and peak |LatG| under 5.
        /// </summary>
        [TestCase(1.0f)]
        [TestCase(-1.0f)]
        public void T5_BrakeThenTurn_NoSpin_LatGUnder5(float steerDir)
        {
            var model = new BicycleModel(new CarParams());
            model.Vx = 160f / 3.6f;

            // 0.6 braking for 1 s
            for (float t = 0f; t < 1.0f; t += dt)
            {
                model.Step(dt, 0f, 0f, 0.6f);
            }

            float maxBeta = 0f;
            float maxLatG = 0f;

            // Release brake, full steering input for 4 s
            for (float t = 0f; t < 4.0f; t += dt)
            {
                model.Step(dt, steerDir, 0f, 0f);

                Assert.That(IsFinite(model.Vx) && IsFinite(model.Vy) && IsFinite(model.R), "State non-finite in T5");

                float beta = MathF.Abs(model.Beta);
                float latG = MathF.Abs(model.LatG);
                if (beta > maxBeta) maxBeta = beta;
                if (latG > maxLatG) maxLatG = latG;

                Assert.That(beta, Is.LessThan(0.6f), $"Car spun in T5 at t={t:F2}s with beta={beta:F4} rad");
                Assert.That(latG, Is.LessThan(5.0f), $"LatG exceeded in T5 at t={t:F2}s with LatG={latG:F2}");
            }

            Assert.That(maxBeta, Is.LessThan(0.6f), $"Peak |beta| = {maxBeta:F4} rad, expected < 0.6 rad");
            Assert.That(maxLatG, Is.LessThan(5.0f), $"Peak |LatG| = {maxLatG:F2}, expected < 5.0");
        }

        /// <summary>
        /// T6. Earlier tests still pass: top speed 310 to 350 km/h, 0 to 100 km/h between 2.5 and 4.0 s.
        /// </summary>
        [Test]
        public void T6_TopSpeedAndAccelerationWithinTarget()
        {
            var model = new BicycleModel(new CarParams());
            float timeTo100 = -1f;

            for (float t = 0f; t < 30.0f; t += dt)
            {
                model.Step(dt, 0f, 1.0f, 0f);

                if (timeTo100 < 0f && model.Vx * 3.6f >= 100f)
                {
                    timeTo100 = t;
                }
            }

            float topSpeedKmh = model.Vx * 3.6f;
            Assert.That(topSpeedKmh, Is.InRange(310f, 350f),
                $"Top speed was {topSpeedKmh:F1} km/h, expected 310-350 km/h");
            Assert.That(timeTo100, Is.InRange(2.5f, 4.0f),
                $"0-100 km/h time was {timeTo100:F2} s, expected 2.5-4.0 s");
            Assert.That(MathF.Abs(model.Vy), Is.LessThan(0.01f), $"Vy was {model.Vy:F6} m/s, expected ~0");
        }
    }
}
