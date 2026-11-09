using System;
using NUnit.Framework;

namespace F1Sim.Tests
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       F1 Signature Acceptance Tests (T1 - T6)     ");
            Console.WriteLine("==================================================");

            var testSuite = new BicycleModelTests();
            int passed = 0;
            int failed = 0;

            void RunTest(string testName, Action action)
            {
                try
                {
                    action();
                    Console.WriteLine($"[PASS] {testName}");
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FAIL] {testName}: {ex.Message}");
                    failed++;
                }
            }

            RunTest("T1: Straight line with disturbance (20s, steer=0.02*sin, Vy=0.2)", () =>
            {
                testSuite.T1_StraightLineWithDisturbance_NoSpin_SpeedOver300_TractionCapped();
            });

            RunTest("T2: Throttle delay from 40 km/h (1s 0 thr, then full thr, disturbance)", () =>
            {
                testSuite.T2_ThrottleDelayWithDisturbance_NoSpin_SpeedOver300(40f);
            });

            RunTest("T2: Throttle delay from 80 km/h (1s 0 thr, then full thr, disturbance)", () =>
            {
                testSuite.T2_ThrottleDelayWithDisturbance_NoSpin_SpeedOver300(80f);
            });

            RunTest("T3: Assist sign test (initBeta = +0.4 rad, |beta| < 0.1 at 2s, steer > 0)", () =>
            {
                testSuite.T3_AssistSignTest_BetaReducedWithin2s_SteerSignCounteractsSlide(0.4f);
            });

            RunTest("T3: Assist sign test (initBeta = -0.4 rad, |beta| < 0.1 at 2s, steer < 0)", () =>
            {
                testSuite.T3_AssistSignTest_BetaReducedWithin2s_SteerSignCounteractsSlide(-0.4f);
            });

            RunTest("T4: Hard braking (300 km/h to 0, straight line, 100-220m, decel < 6.5g)", () =>
            {
                testSuite.T4_HardBraking_StraightLine_StoppingDistanceAndDecelValid();
            });

            RunTest("T5: Brake then turn (160 km/h, 1s 0.6 brake, release, full steer right)", () =>
            {
                testSuite.T5_BrakeThenTurn_NoSpin_LatGUnder5(1.0f);
            });

            RunTest("T5: Brake then turn (160 km/h, 1s 0.6 brake, release, full steer left)", () =>
            {
                testSuite.T5_BrakeThenTurn_NoSpin_LatGUnder5(-1.0f);
            });

            RunTest("T6: Earlier tests (top speed 310-350 km/h, 0-100 km/h in 2.5-4.0 s)", () =>
            {
                testSuite.T6_TopSpeedAndAccelerationWithinTarget();
            });

            Console.WriteLine("==================================================");
            Console.WriteLine($"Results: {passed} passed, {failed} failed.");
            Console.WriteLine("==================================================");

            return failed == 0 ? 0 : 1;
        }
    }
}
