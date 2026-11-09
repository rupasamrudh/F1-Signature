// TrackSplineTests.cs — NUnit tests T1 to T9 for M3 Part 1 track data and spline library.
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using NUnit.Framework;
using F1Sim.Simulation.Track;

namespace F1Sim.Tests
{
    [TestFixture]
    public class TrackSplineTests
    {
        TrackConfig config;
        ElevationConfig elevConfig;
        TrackSpline spline;

        [OneTimeSetUp]
        public void Setup()
        {
            string trackPath = "Assets/Data/Tracks/f1sig_track.json";
            string elevPath = "Assets/Data/Tracks/f1sig_elevation.json";

            // Fallback for scratch execution
            if (!File.Exists(trackPath)) trackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../Assets/Data/Tracks/f1sig_track.json");
            if (!File.Exists(elevPath)) elevPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../Assets/Data/Tracks/f1sig_elevation.json");

            config = ParseTrackJson(File.ReadAllText(trackPath));
            elevConfig = ParseElevJson(File.ReadAllText(elevPath));
            spline = new TrackSpline(config);
        }

        static TrackConfig ParseTrackJson(string json)
        {
            // Simple robust JSON parser for standard TrackConfig in pure C# test environment
            var cfg = new TrackConfig();
            cfg.samples = new List<TrackSample>();
            cfg.corners = new List<TrackCorner>();
            cfg.sectors = new List<TrackSector>();
            cfg.straight_mode_zones = new List<StraightModeZone>();

            using var reader = new StringReader(json);
            string line;
            string section = "";
            TrackSample currentSample = null;
            TrackCorner currentCorner = null;
            TrackSector currentSector = null;
            StraightModeZone currentZone = null;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("\"total_length_m\":"))
                    cfg.total_length_m = float.Parse(ExtractVal(line));
                else if (line.StartsWith("\"airDensity\":"))
                    cfg.airDensity = float.Parse(ExtractVal(line));
                else if (line.StartsWith("\"scale_m_per_px\":"))
                    cfg.scale_m_per_px = float.Parse(ExtractVal(line));
                else if (line.StartsWith("\"samples\":"))
                    section = "samples";
                else if (line.StartsWith("\"corners\":"))
                    section = "corners";
                else if (line.StartsWith("\"sectors\":"))
                    section = "sectors";
                else if (line.StartsWith("\"straight_mode_zones\":"))
                    section = "zones";
                else if (line == "{")
                {
                    if (section == "samples") currentSample = new TrackSample();
                    else if (section == "corners") currentCorner = new TrackCorner();
                    else if (section == "sectors") currentSector = new TrackSector();
                    else if (section == "zones") currentZone = new StraightModeZone();
                }
                else if (line.StartsWith("}") || line.StartsWith("},"))
                {
                    if (currentSample != null) { cfg.samples.Add(currentSample); currentSample = null; }
                    if (currentCorner != null) { cfg.corners.Add(currentCorner); currentCorner = null; }
                    if (currentSector != null) { cfg.sectors.Add(currentSector); currentSector = null; }
                    if (currentZone != null) { cfg.straight_mode_zones.Add(currentZone); currentZone = null; }
                }
                else if (currentSample != null)
                {
                    if (line.StartsWith("\"index\":")) currentSample.index = int.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"s\":")) currentSample.s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"x\":")) currentSample.x = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"y\":")) currentSample.y = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"z\":")) currentSample.z = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"width\":")) currentSample.width = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"curvature\":")) currentSample.curvature = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"radius_m\":")) currentSample.radius_m = float.Parse(ExtractVal(line));
                }
                else if (currentCorner != null)
                {
                    if (line.StartsWith("\"number\":")) currentCorner.number = int.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"name\":")) currentCorner.name = ExtractStr(line);
                    else if (line.StartsWith("\"apex_s\":")) currentCorner.apex_s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"entry_s\":")) currentCorner.entry_s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"exit_s\":")) currentCorner.exit_s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"direction\":")) currentCorner.direction = ExtractStr(line);
                    else if (line.StartsWith("\"radius_m\":")) currentCorner.radius_m = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"curvature\":")) currentCorner.curvature = float.Parse(ExtractVal(line));
                }
                else if (currentSector != null)
                {
                    if (line.StartsWith("\"sector\":")) currentSector.sector = int.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"start_s\":")) currentSector.start_s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"end_s\":")) currentSector.end_s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"length_m\":")) currentSector.length_m = float.Parse(ExtractVal(line));
                }
                else if (currentZone != null)
                {
                    if (line.StartsWith("\"name\":")) currentZone.name = ExtractStr(line);
                    else if (line.StartsWith("\"start_s\":")) currentZone.start_s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"end_s\":")) currentZone.end_s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"length_m\":")) currentZone.length_m = float.Parse(ExtractVal(line));
                }
            }

            return cfg;
        }

        static ElevationConfig ParseElevJson(string json)
        {
            var cfg = new ElevationConfig();
            cfg.control_points = new List<ElevationControlPoint>();
            using var reader = new StringReader(json);
            string line;
            ElevationControlPoint cp = null;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line == "{") cp = new ElevationControlPoint();
                else if (line.StartsWith("}") || line.StartsWith("},"))
                {
                    if (cp != null && cp.corner != null) { cfg.control_points.Add(cp); cp = null; }
                }
                else if (cp != null)
                {
                    if (line.StartsWith("\"corner\":")) cp.corner = ExtractStr(line);
                    else if (line.StartsWith("\"s\":")) cp.s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"height\":")) cp.height = float.Parse(ExtractVal(line));
                }
            }
            return cfg;
        }

        static string ExtractVal(string l)
        {
            int colon = l.IndexOf(':');
            string v = l.Substring(colon + 1).Trim().TrimEnd(',');
            return v;
        }
        static string ExtractStr(string l)
        {
            int colon = l.IndexOf(':');
            string v = l.Substring(colon + 1).Trim().Trim('"', ',', ' ');
            return v;
        }

        // ── T1: Loop Closes ──────────────────────────────────────────────────
        [Test]
        public void T1_LoopCloses()
        {
            var s0 = config.samples[0];
            var sEnd = config.samples[^1];

            float dx = s0.x - sEnd.x;
            float dz = s0.z - sEnd.z;
            float closingDist = MathF.Sqrt(dx * dx + dz * dz);

            // Compute total closed polygon perimeter
            float perimeter = 0f;
            for (int i = 0; i < config.samples.Count; i++)
            {
                int next = (i + 1) % config.samples.Count;
                float segDx = config.samples[next].x - config.samples[i].x;
                float segDz = config.samples[next].z - config.samples[i].z;
                perimeter += MathF.Sqrt(segDx * segDx + segDz * segDz);
            }

            float errPct = MathF.Abs(perimeter - 4309.0f) / 4309.0f * 100.0f;
            Console.WriteLine($"T1 Loop Closes: Closing dist={closingDist:0.00}m (target <= 2.0m), Total length={perimeter:0.1}m (err={errPct:0.2}%, target < 1%)");

            Assert.That(closingDist, Is.LessThanOrEqualTo(2.0f), $"Closing distance too high: {closingDist:0.00} m");
            Assert.That(errPct, Is.LessThan(1.0f), $"Total length deviates by {errPct:0.2}% from 4309m");
        }

        // ── T2: No Self-Intersection ─────────────────────────────────────────
        [Test]
        public void T2_NoSelfIntersection()
        {
            int n = config.samples.Count;
            int intersections = 0;

            for (int i = 0; i < n; i++)
            {
                float a1x = config.samples[i].x, a1z = config.samples[i].z;
                float a2x = config.samples[(i + 1) % n].x, a2z = config.samples[(i + 1) % n].z;

                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue; // adjacent

                    float b1x = config.samples[j].x, b1z = config.samples[j].z;
                    float b2x = config.samples[(j + 1) % n].x, b2z = config.samples[(j + 1) % n].z;

                    if (SegmentsIntersect(a1x, a1z, a2x, a2z, b1x, b1z, b2x, b2z))
                    {
                        intersections++;
                        Console.WriteLine($"Intersection found between seg {i} and seg {j}");
                    }
                }
            }

            Console.WriteLine($"T2 Self-Intersection: Found {intersections} crossings (target 0)");
            Assert.That(intersections, Is.EqualTo(0), $"Found {intersections} self-intersections");
        }

        static bool SegmentsIntersect(float p0x, float p0y, float p1x, float p1y, float p2x, float p2y, float p3x, float p3y)
        {
            float s10_x = p1x - p0x, s10_y = p1y - p0y;
            float s32_x = p3x - p2x, s32_y = p3y - p2y;
            float denom = s10_x * s32_y - s32_x * s10_y;
            if (MathF.Abs(denom) < 1e-8f) return false;

            bool denomPositive = denom > 0;
            float s02_x = p0x - p2x, s02_y = p0y - p2y;
            float s_numer = s10_x * s02_y - s10_y * s02_x;
            if ((s_numer < 0) == denomPositive) return false;

            float t_numer = s32_x * s02_y - s32_y * s02_x;
            if ((t_numer < 0) == denomPositive) return false;

            if (((s_numer > denom) == denomPositive) || ((t_numer > denom) == denomPositive)) return false;
            return true;
        }

        // ── T3: Anticlockwise Signed Area ─────────────────────────────────────
        [Test]
        public void T3_AnticlockwiseDirection()
        {
            float shoelace = 0f;
            int n = config.samples.Count;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                shoelace += config.samples[i].x * config.samples[next].z - config.samples[next].x * config.samples[i].z;
            }
            float signedArea = shoelace * 0.5f;

            Console.WriteLine($"T3 Anticlockwise Area: Signed Area={signedArea:0} m^2 (positive = anticlockwise)");
            Assert.That(signedArea, Is.GreaterThan(0f), "Track loop must run anticlockwise seen from above");
        }

        // ── T4: Minimum Radius & Curvature Continuity ─────────────────────────
        [Test]
        public void T4_MinimumRadiusAndCurvatureChange()
        {
            float minRadius = float.MaxValue;
            float maxDeltaK = 0f;
            int n = config.samples.Count;

            var tightest = new List<(int idx, float s, float r, float k)>();

            for (int i = 0; i < n; i++)
            {
                float r = config.samples[i].radius_m;
                if (r < minRadius) minRadius = r;
                tightest.Add((i, config.samples[i].s, r, config.samples[i].curvature));

                int next = (i + 1) % n;
                float dK = MathF.Abs(config.samples[next].curvature - config.samples[i].curvature);
                if (dK > maxDeltaK) maxDeltaK = dK;
            }

            tightest.Sort((a, b) => a.r.CompareTo(b.r));

            Console.WriteLine($"T4 Radius & Curvature: Min Radius={minRadius:0.1}m (target >= 10m), Max Delta-Curvature={maxDeltaK:0.00000} 1/m (target < 0.06 1/m)");
            Console.WriteLine("Top 15 tightest points across track:");
            for (int k = 0; k < Math.Min(15, tightest.Count); k++)
            {
                var tp = tightest[k];
                Console.WriteLine($"  #{k + 1,2}: s={tp.s,6:F1}m, radius={tp.r,5:F1}m, kappa={tp.k,+7:F4} 1/m");
            }

            Assert.That(minRadius, Is.GreaterThanOrEqualTo(10.0f), $"Minimum radius too sharp: {minRadius:0.1} m");
            Assert.That(maxDeltaK, Is.LessThan(0.06f), $"Curvature change between consecutive samples too high: {maxDeltaK:0.00000} 1/m");
        }

        // ── T5: Elevation Range & Key Peaks ──────────────────────────────────
        [Test]
        public void T5_ElevationRangeAndKeyPoints()
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            float sAtMax = 0f, sAtMin = 0f;

            foreach (var smp in config.samples)
            {
                if (smp.y > maxY) { maxY = smp.y; sAtMax = smp.s; }
                if (smp.y < minY) { minY = smp.y; sAtMin = smp.s; }
            }

            float range = maxY - minY;
            Console.WriteLine($"T5 Elevation: Range={range:0.1}m (target 38-48m), MaxY={maxY:0.1}m at s={sAtMax:0}m (target within 150m of S/F), MinY={minY:0.1}m at s={sAtMin:0}m");

            Assert.That(range, Is.InRange(38.0f, 48.0f), $"Elevation range out of bounds: {range:0.1} m");
            // Highest point within 150m of S/F (either s < 150 or s > 4309 - 150)
            bool peakNearSF = sAtMax <= 150.0f || sAtMax >= (config.total_length_m - 150.0f);
            Assert.That(peakNearSF, Is.True, $"Peak elevation is at s={sAtMax:0.1} m, not within 150m of S/F");

            // Lowest point near T11-T12 (around s in [2900, 3350] m)
            Assert.That(sAtMin, Is.InRange(2800f, 3400f), $"Lowest elevation is at s={sAtMin:0.1} m, expected in T11-T12 zone");
        }

        // ── T6: Corner Table Validation ──────────────────────────────────────
        [Test]
        public void T6_CornerTable_15Corners_Directions()
        {
            Assert.That(config.corners.Count, Is.EqualTo(15), "Track must have exactly 15 corners");

            Console.WriteLine($"T6 Corner Table: 15 corners verified in driving order:");
            for (int i = 0; i < config.corners.Count; i++)
            {
                var c = config.corners[i];
                Assert.That(c.number, Is.EqualTo(i + 1));
                Console.WriteLine($"  Turn {c.number,2} ({c.name,-3}): apex_s={c.apex_s,6:F1}m, radius={c.radius_m,5:F1}m, dir={c.direction,-5}");
            }

            // Real circuit verification: T1 left, T2 right, T3 left
            Assert.That(config.corners[0].direction, Is.EqualTo("left"), "T1 must be left");
            Assert.That(config.corners[1].direction, Is.EqualTo("right"), "T2 must be right");
            Assert.That(config.corners[2].direction, Is.EqualTo("left"), "T3 must be left");
        }

        // ── T7: Sector Lengths ───────────────────────────────────────────────
        [Test]
        public void T7_SectorLengthsAndProportions()
        {
            Assert.That(config.sectors.Count, Is.EqualTo(3), "Track must have 3 sectors");
            float s1 = config.sectors[0].length_m;
            float s2 = config.sectors[1].length_m;
            float s3 = config.sectors[2].length_m;
            float total = s1 + s2 + s3;

            Console.WriteLine($"T7 Sectors: S1={s1:0.1}m ({s1/total*100:0.1}%), S2={s2:0.1}m ({s2/total*100:0.1}%), S3={s3:0.1}m ({s3/total*100:0.1}%), Total={total:0.1}m");
            Assert.That(total, Is.EqualTo(config.total_length_m).Within(0.5f));
            Assert.That(s1, Is.InRange(1100f, 1350f));
            Assert.That(s2, Is.InRange(1700f, 2000f));
            Assert.That(s3, Is.InRange(1100f, 1300f));
        }

        // ── T8: Query Accuracy & Performance ─────────────────────────────────
        [Test]
        public void T8_QueryAccuracyAndPerformance()
        {
            var rng = new Random(42);
            int testCount = 1000;
            float maxErrS = 0f;
            float maxErrOffset = 0f;

            // Generate 1000 test points with known s and known lateral offset
            var queries = new (float px, float pz, float expectedS, float expectedOffset, int hintIdx)[testCount];
            for (int k = 0; k < testCount; k++)
            {
                int sampleIdx = rng.Next(0, config.samples.Count);
                var smp = config.samples[sampleIdx];

                // Known offset between -6m and +6m
                float trueOffset = (float)(rng.NextDouble() * 12.0 - 6.0);

                int nextIdx = (sampleIdx + 1) % config.samples.Count;
                float tx = config.samples[nextIdx].x - smp.x;
                float tz = config.samples[nextIdx].z - smp.z;
                float tLen = MathF.Sqrt(tx * tx + tz * tz);
                tx /= tLen; tz /= tLen;

                // Left normal
                float nx = -tz;
                float nz = tx;

                float testPx = smp.x + trueOffset * nx;
                float testPz = smp.z + trueOffset * nz;

                // Local hint within +- 10 indices
                int hint = (sampleIdx + rng.Next(-5, 6) + config.samples.Count) % config.samples.Count;
                queries[k] = (testPx, testPz, smp.s, trueOffset, hint);
            }

            // Warmup
            for (int k = 0; k < 100; k++)
                spline.Query(queries[k].px, queries[k].pz, queries[k].hintIdx);

            var sw = Stopwatch.StartNew();
            for (int k = 0; k < testCount; k++)
            {
                var q = queries[k];
                var res = spline.Query(q.px, q.pz, q.hintIdx);

                float errS = MathF.Abs(res.s - q.expectedS);
                if (errS > config.total_length_m * 0.5f) errS = config.total_length_m - errS;
                float errOff = MathF.Abs(res.lateralOffset - q.expectedOffset);

                if (errS > maxErrS) maxErrS = errS;
                if (errOff > maxErrOffset) maxErrOffset = errOff;
            }
            sw.Stop();

            float totalMs = (float)sw.Elapsed.TotalMilliseconds;
            float avgQueryMs = totalMs / testCount;

            Console.WriteLine($"T8 Query: Avg time={avgQueryMs:0.0000} ms (target < 0.05 ms), Max offset error={maxErrOffset:0.000}m (target <= 0.1m), Max s error={maxErrS:0.000}m");
            Assert.That(avgQueryMs, Is.LessThan(0.05f), $"Query too slow: {avgQueryMs:0.0000} ms");
            Assert.That(maxErrOffset, Is.LessThanOrEqualTo(0.1f), $"Lateral offset error too high: {maxErrOffset:0.000} m");
        }

        // ── T9: Surface Classification ───────────────────────────────────────
        [Test]
        public void T9_SurfaceClassification()
        {
            float halfWidth = 7.0f; // 14m width
            float kerbW = 1.0f;
            float gravelW = 8.0f;

            // In T1 (left turn -> inside is positive offset)
            float sCorner = config.corners[0].apex_s;
            float kCorner = 0.033f; // left turn

            // 1. Center of track -> Asphalt
            var surfAsphalt = spline.ClassifySurface(sCorner, 0.0f, 14.0f, kCorner);
            Assert.That(surfAsphalt, Is.EqualTo(SurfaceType.Asphalt));

            // 2. 7.5m left inside -> Kerb (halfWidth < offset <= halfWidth + 1m)
            var surfKerb = spline.ClassifySurface(sCorner, halfWidth + 0.5f, 14.0f, kCorner);
            Assert.That(surfKerb, Is.EqualTo(SurfaceType.Kerb));

            // 3. 10.0m left -> Gravel (halfWidth + kerbW < offset <= halfWidth + kerbW + gravelW)
            var surfGravel = spline.ClassifySurface(sCorner, halfWidth + kerbW + 2.0f, 14.0f, kCorner);
            Assert.That(surfGravel, Is.EqualTo(SurfaceType.Gravel));

            // 4. 20.0m left -> Grass
            var surfGrass = spline.ClassifySurface(sCorner, halfWidth + kerbW + gravelW + 5.0f, 14.0f, kCorner);
            Assert.That(surfGrass, Is.EqualTo(SurfaceType.Grass));

            Console.WriteLine($"T9 Surface Classification: Verified Asphalt, Kerb, Gravel, Grass at expected offsets.");
        }
    }
}

