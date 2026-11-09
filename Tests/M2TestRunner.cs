// M2TestRunner.cs — Console test harness for M2 VehicleDynamicsTests.
using System;
using NUnit.Framework;

namespace F1Sim.Tests
{
    class M2Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("    F1 Signature M2 Acceptance Tests (T1 - T11)   ");
            Console.WriteLine("==================================================");

            var suite  = new VehicleDynamicsTests();
            int passed = 0, failed = 0;

            void Run(string name, Action a)
            {
                try   { a(); Console.WriteLine($"[PASS] {name}"); passed++; }
                catch (Exception ex) { Console.WriteLine($"[FAIL] {name}: {ex.Message}"); failed++; }
            }

            Run("T1  0→100 km/h (2.5–3.5 s)",              suite.T1_ZeroTo100_InTarget);
            Run("T2  0→200 km/h (5.0–7.0 s)",              suite.T2_ZeroTo200_InTarget);
            Run("T3  Top speed (310–350 km/h)",             suite.T3_TopSpeed_InRange);
            Run("T4  300→0 km/h (dist 100-150m, t 3.5-5.5s)", suite.T4_HardBraking_300_DistanceAndTime);
            Run("T5  200→0 km/h (1.5–2.6 s)",              suite.T5_HardBraking_200_Time);
            Run("T6a Assist +0.4 rad",                      () => suite.T6_AssistSign_BetaReducedWithin2s(0.4f));
            Run("T6b Assist -0.4 rad",                      () => suite.T6_AssistSign_BetaReducedWithin2s(-0.4f));
            Run("T7  Skidpad LatG (2.5–3.5 g)",             suite.T7_Skidpad_PeakLatG);
            Run("T8  Straight throttle noise (no spin)",     suite.T8_StraightThrottle_NoSpin);
            Run("T9  Brake+turn no spin",                   suite.T9_BrakeThenTurn_NoSpin);
            Run("T10 Aero blend (straight < corner down)",  suite.T10_AeroBlend_ChangesDownforce);
            Run("T11 Gear shifts (reaches G7+)",            suite.T11_GearShifts_AllGearsUsed);
            Run("T12 Split-mu straightline diff lock",      suite.T12_SplitMu_DiffLock_CarStaysStraight);
            Run("T13 Step steer rise time & overshoot",     suite.T13_StepSteer_RiseTimeAndOvershoot);
            Run("T14 Corner exit stability",                suite.T14_CornerExit_Stability);
            Run("T15a Flick test 100 km/h",                 () => suite.T15_FlickTest_NoSpin(100f));
            Run("T15b Flick test 220 km/h",                 () => suite.T15_FlickTest_NoSpin(220f));
            Run("T16 Legacy payload reset test",            suite.T16_LegacyPayload_ResetsDefaults_WhenParamsVersionZero);

            Console.WriteLine("==================================================");
            Console.WriteLine($"Results: {passed} passed, {failed} failed.");
            Console.WriteLine("==================================================");
            return failed == 0 ? 0 : 1;
        }
    }
}

