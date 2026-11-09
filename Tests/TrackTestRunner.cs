// TrackTestRunner.cs — Standalone console runner for TrackSplineTests.
using System;
using NUnit.Framework;

namespace F1Sim.Tests
{
    class TrackTestRunner
    {
        static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       F1 Signature M3 Track Spline Tests (T1-T9) ");
            Console.WriteLine("==================================================");

            var suite = new TrackSplineTests();
            suite.Setup();

            int passed = 0, failed = 0;

            void Run(string name, Action a)
            {
                try
                {
                    a();
                    Console.WriteLine($"[PASS] {name}");
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FAIL] {name}: {ex.Message}");
                    failed++;
                }
            }

            Run("T1 Loop Closes & Length (within 1%)",       suite.T1_LoopCloses);
            Run("T2 No Self-Intersection",                  suite.T2_NoSelfIntersection);
            Run("T3 Anticlockwise Area (Area > 0)",          suite.T3_AnticlockwiseDirection);
            Run("T4 Minimum Radius (>=10m) & Curvature",    suite.T4_MinimumRadiusAndCurvatureChange);
            Run("T5 Elevation Profile & Peaks",             suite.T5_ElevationRangeAndKeyPoints);
            Run("T6 Corner Table (15 corners & dirs)",       suite.T6_CornerTable_15Corners_Directions);
            Run("T7 Sector Lengths & Splits",               suite.T7_SectorLengthsAndProportions);
            Run("T8 Query Accuracy (<0.1m) & Speed (<0.05ms)", suite.T8_QueryAccuracyAndPerformance);
            Run("T9 Surface Classification",                suite.T9_SurfaceClassification);

            Console.WriteLine("==================================================");
            Console.WriteLine($"Results: {passed} passed, {failed} failed.");
            Console.WriteLine("==================================================");

            return failed == 0 ? 0 : 1;
        }
    }
}

