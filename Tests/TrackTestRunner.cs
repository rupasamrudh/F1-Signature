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
            Console.WriteLine("      F1 Signature M3 Track Spline Tests (T1-T11) ");
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

            Run("T1 Loop Closes (within 5% nominal spacing)", suite.T1_RealClosureAndNominalSpacing);
            Run("T2 No Self-Intersection",                  suite.T2_NoSelfIntersection);
            Run("T3 Anticlockwise Area (Area > 0)",          suite.T3_AnticlockwiseDirection);
            Run("T4 Minimum Radius (>=10m) & Curvature",    suite.T4_MinimumRadiusAndCurvatureChange);
            Run("T5 Elevation Continuity & Gradient (<=10%)", suite.T5_ElevationRangeContinuityAndGradient);
            Run("T6 Corner Table (15 corners & dirs)",       suite.T6_CornerTableAndExpectedDirections);
            Run("T7 Sector Lengths & Splits",               suite.T7_SectorLengthsAndProportions);
            Run("T8 Query Accuracy (<0.1m) & Speed (<0.05ms)", suite.T8_QueryAccuracyAndPerformance);
            Run("T9 Surface Classification",                suite.T9_SurfaceClassification);
            Run("T10 Spawn Slot Position & Tangent Heading", suite.T10_SpawnSlot_PositionAndTangentHeading);
            Run("T11 QuerySampleS Matches All Samples (<=0.5m)", suite.T11_QuerySampleS_MatchesEverySampleWithin0_5m);
            Run("T12 LapTimer 200 km/h Lap & Sector Sum (<=0.05s)", suite.T12_LapTimer_200Kmh_LapAndSectors);
            Run("T13 Slope Physics (Downhill vs Uphill <=5%)", suite.T13_SlopePhysics_DownhillVsUphill);
            Run("T14 Surface Grip & Drag (Grass vs Asphalt)", suite.T14_SurfaceGripAndDrag_Effects);

            Console.WriteLine("==================================================");
            Console.WriteLine($"Results: {passed} passed, {failed} failed.");
            Console.WriteLine("==================================================");

            return failed == 0 ? 0 : 1;
        }
    }
}

