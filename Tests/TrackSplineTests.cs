// TrackSplineTests.cs — EditMode unit tests for TrackSpline and track data (T1-T14).
// Pure C#, uses System.MathF and NUnit.
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using F1Sim.Simulation;
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

            if (!File.Exists(trackPath)) trackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../Assets/Data/Tracks/f1sig_track.json");
            if (!File.Exists(elevPath)) elevPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../Assets/Data/Tracks/f1sig_elevation.json");

            config = ParseTrackJson(File.ReadAllText(trackPath));
            elevConfig = ParseElevJson(File.ReadAllText(elevPath));
            spline = new TrackSpline(config);
        }

        static TrackConfig ParseTrackJson(string json)
        {
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
                if (line.StartsWith("\"sectors\":")) { section = "sectors"; continue; }
                if (line.StartsWith("\"straight_mode_zones\":")) { section = "zones"; continue; }
                if (line.StartsWith("\"corners\":")) { section = "corners"; continue; }
                if (line.StartsWith("\"spawn_slot\":")) { section = "spawn"; continue; }
                if (line.StartsWith("\"samples\":")) { section = "samples"; continue; }

                if (section == "")
                {
                    if (line.StartsWith("\"total_length_m\":")) cfg.total_length_m = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"sample_spacing_m\":")) cfg.sample_spacing_m = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"scale_m_per_px\":")) cfg.scale_m_per_px = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"airDensity\":")) cfg.airDensity = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"default_width_m\":")) cfg.default_width_m = float.Parse(ExtractVal(line));
                }
                else if (section == "sectors")
                {
                    if (line == "{") currentSector = new TrackSector();
                    else if (line.StartsWith("}") || line.StartsWith("},"))
                    {
                        if (currentSector != null) { cfg.sectors.Add(currentSector); currentSector = null; }
                    }
                    else if (currentSector != null)
                    {
                        if (line.StartsWith("\"sector\":")) currentSector.sector = int.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"start_s\":")) currentSector.start_s = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"end_s\":")) currentSector.end_s = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"length_m\":")) currentSector.length_m = float.Parse(ExtractVal(line));
                    }
                }
                else if (section == "zones")
                {
                    if (line == "{") currentZone = new StraightModeZone();
                    else if (line.StartsWith("}") || line.StartsWith("},"))
                    {
                        if (currentZone != null) { cfg.straight_mode_zones.Add(currentZone); currentZone = null; }
                    }
                    else if (currentZone != null)
                    {
                        if (line.StartsWith("\"name\":")) currentZone.name = ExtractStr(line);
                        else if (line.StartsWith("\"start_s\":")) currentZone.start_s = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"end_s\":")) currentZone.end_s = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"length_m\":")) currentZone.length_m = float.Parse(ExtractVal(line));
                    }
                }
                else if (section == "corners")
                {
                    if (line == "{") currentCorner = new TrackCorner();
                    else if (line.StartsWith("}") || line.StartsWith("},"))
                    {
                        if (currentCorner != null) { cfg.corners.Add(currentCorner); currentCorner = null; }
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
                        else if (line.StartsWith("\"label_x_px\":")) currentCorner.label_x_px = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"label_y_px\":")) currentCorner.label_y_px = float.Parse(ExtractVal(line));
                    }
                }
                else if (section == "spawn")
                {
                    if (line.StartsWith("\"s\":")) cfg.spawn_slot.s = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"x\":")) cfg.spawn_slot.x = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"y\":")) cfg.spawn_slot.y = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"z\":")) cfg.spawn_slot.z = float.Parse(ExtractVal(line));
                    else if (line.StartsWith("\"yaw_deg\":")) cfg.spawn_slot.yaw_deg = float.Parse(ExtractVal(line));
                }
                else if (section == "samples")
                {
                    if (line == "{") currentSample = new TrackSample();
                    else if (line.StartsWith("}") || line.StartsWith("},"))
                    {
                        if (currentSample != null) { cfg.samples.Add(currentSample); currentSample = null; }
                    }
                    else if (currentSample != null)
                    {
                        if (line.StartsWith("\"index\":")) currentSample.index = int.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"s\":")) currentSample.s = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"x\":")) currentSample.x = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"y\":")) currentSample.y = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"z\":")) currentSample.z = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"width\":")) currentSample.width = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"banking_deg\":")) currentSample.banking_deg = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"pitch_deg\":")) currentSample.pitch_deg = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"curvature\":")) currentSample.curvature = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"radius_m\":")) currentSample.radius_m = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"px_x\":")) currentSample.px_x = float.Parse(ExtractVal(line));
                        else if (line.StartsWith("\"px_y\":")) currentSample.px_y = float.Parse(ExtractVal(line));
                    }
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

        // ── T1: Real Closure & Length ────────────────────────────────────────
        [Test]
        public void T1_RealClosureAndNominalSpacing()
        {
            var s0 = config.samples[0];
            var sEnd = config.samples[^1];

            float dx = s0.x - sEnd.x;
            float dz = s0.z - sEnd.z;
            float closingGap = MathF.Sqrt(dx * dx + dz * dz);

            float nominalSpacing = config.sample_spacing_m;
            float gapErrPct = MathF.Abs(closingGap - nominalSpacing) / nominalSpacing * 100.0f;

            // Total polygon perimeter
            float perimeter = 0f;
            for (int i = 0; i < config.samples.Count; i++)
            {
                int next = (i + 1) % config.samples.Count;
                float segDx = config.samples[next].x - config.samples[i].x;
                float segDz = config.samples[next].z - config.samples[i].z;
                perimeter += MathF.Sqrt(segDx * segDx + segDz * segDz);
            }

            float lenErrPct = MathF.Abs(perimeter - 4309.0f) / 4309.0f * 100.0f;
            Console.WriteLine($"T1 Real Closure: Closing gap={closingGap:0.000}m vs nominal={nominalSpacing:0.000}m (error={gapErrPct:0.2}%, limit <= 5%), Total length={perimeter:0.1}m (error={lenErrPct:0.2}%)");

            Assert.That(gapErrPct, Is.LessThanOrEqualTo(5.0f), $"Closing gap deviates from nominal spacing by {gapErrPct:0.2}%");
            Assert.That(lenErrPct, Is.LessThan(1.0f), $"Total length deviates by {lenErrPct:0.2}% from 4309m");
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
                    if (i == 0 && j == n - 1) continue;

                    float b1x = config.samples[j].x, b1z = config.samples[j].z;
                    float b2x = config.samples[(j + 1) % n].x, b2z = config.samples[(j + 1) % n].z;

                    if (SegmentsIntersect(a1x, a1z, a2x, a2z, b1x, b1z, b2x, b2z))
                    {
                        intersections++;
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

        // ── T5: Elevation Range, Continuity & Gradient Limit ──────────────────
        [Test]
        public void T5_ElevationRangeContinuityAndGradient()
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            float sAtMax = 0f, sAtMin = 0f;
            float maxStepDiff = 0f;
            float maxGrad = 0f;
            int n = config.samples.Count;

            for (int i = 0; i < n; i++)
            {
                var smp = config.samples[i];
                if (smp.y > maxY) { maxY = smp.y; sAtMax = smp.s; }
                if (smp.y < minY) { minY = smp.y; sAtMin = smp.s; }

                int next = (i + 1) % n;
                float diff = MathF.Abs(config.samples[next].y - smp.y);
                if (diff > maxStepDiff) maxStepDiff = diff;

                float grad = diff / config.sample_spacing_m;
                if (grad > maxGrad) maxGrad = grad;
            }

            float range = maxY - minY;
            Console.WriteLine($"T5 Elevation: Range={range:0.1}m (target 40-46m), MaxY={maxY:0.1}m at s={sAtMax:0}m, MinY={minY:0.1}m at s={sAtMin:0}m, Max step diff={maxStepDiff:0.000}m (limit <= 0.1m), Max gradient={maxGrad * 100:0.1}% (limit <= 10%)");

            Assert.That(range, Is.InRange(40.0f, 46.0f), $"Elevation range out of bounds: {range:0.1} m");
            Assert.That(maxGrad, Is.LessThanOrEqualTo(0.10f), $"Elevation gradient too steep: {maxGrad * 100:0.1}%");
            Assert.That(sAtMax <= 150.0f || sAtMax >= (config.total_length_m - 150.0f), Is.True, $"Peak elevation expected near S/F, got s={sAtMax:0.1} m");
            Assert.That(sAtMin, Is.InRange(2800f, 3400f), $"Lowest elevation expected near T11-T12, got s={sAtMin:0.1} m");
        }

        // ── T6: Corner Table & Expected Real Directions ───────────────────────
        [Test]
        public void T6_CornerTableAndExpectedDirections()
        {
            Assert.That(config.corners.Count, Is.EqualTo(15), "Track must have exactly 15 corners");

            // Real circuit reference directions for key turns
            var expectedDirs = new Dictionary<string, string>
            {
                { "T1",  "left"  },
                { "T2",  "right" },
                { "T3",  "left"  },
                { "T12", "left"  },
                { "T13", "left"  },
                { "T14", "left"  },
                { "T15", "left"  }
            };

            Console.WriteLine($"T6 Corner Table (15 corners in driving order):");
            int matches = 0;
            for (int i = 0; i < config.corners.Count; i++)
            {
                var c = config.corners[i];
                Assert.That(c.number, Is.EqualTo(i + 1));
                Assert.That(c.name, Is.EqualTo($"T{i + 1}"));

                string expected = expectedDirs.ContainsKey(c.name) ? expectedDirs[c.name] : "-";
                bool match = expected == "-" || expected == c.direction;
                if (match) matches++;

                Console.WriteLine($"  Turn {c.number,2} ({c.name,-3}): apex_s={c.apex_s,6:F1}m, radius={c.radius_m,5:F1}m, dir={c.direction,-5} (expected: {expected,-5})");
            }

            foreach (var kv in expectedDirs)
            {
                var c = config.corners.Find(x => x.name == kv.Key);
                Assert.That(c, Is.Not.Null);
                Assert.That(c.direction, Is.EqualTo(kv.Value), $"Corner {kv.Key} direction mismatch: got {c.direction}, expected {kv.Value}");
            }
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

            var queries = new (float px, float pz, float expectedS, float expectedOffset, int hintIdx)[testCount];
            for (int k = 0; k < testCount; k++)
            {
                int sampleIdx = rng.Next(0, config.samples.Count);
                var smp = config.samples[sampleIdx];
                int nextIdx = (sampleIdx + 1) % config.samples.Count;

                float tSeg = (float)(rng.NextDouble() * 0.8 + 0.1); // along interior of segment
                float knownS = smp.s + tSeg * config.sample_spacing_m;
                float trueOffset = (float)(rng.NextDouble() * 12.0 - 6.0);

                float tx = config.samples[nextIdx].x - smp.x;
                float tz = config.samples[nextIdx].z - smp.z;
                float tLen = MathF.Sqrt(tx * tx + tz * tz);
                tx /= tLen; tz /= tLen;

                float nx = -tz;
                float nz = tx;

                float baseX = smp.x + tSeg * (config.samples[nextIdx].x - smp.x);
                float baseZ = smp.z + tSeg * (config.samples[nextIdx].z - smp.z);

                float testPx = baseX + trueOffset * nx;
                float testPz = baseZ + trueOffset * nz;

                int hint = (sampleIdx + rng.Next(-5, 6) + config.samples.Count) % config.samples.Count;
                queries[k] = (testPx, testPz, knownS, trueOffset, hint);
            }

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

            float sCorner = config.corners[0].apex_s;
            float kCorner = 0.033f; // left turn

            var surfAsphalt = spline.ClassifySurface(sCorner, 0.0f, 14.0f, kCorner);
            Assert.That(surfAsphalt, Is.EqualTo(SurfaceType.Asphalt));

            var surfKerb = spline.ClassifySurface(sCorner, halfWidth + 0.5f, 14.0f, kCorner);
            Assert.That(surfKerb, Is.EqualTo(SurfaceType.Kerb));

            var surfGravel = spline.ClassifySurface(sCorner, halfWidth + kerbW + 2.0f, 14.0f, kCorner);
            Assert.That(surfGravel, Is.EqualTo(SurfaceType.Gravel));

            var surfGrass = spline.ClassifySurface(sCorner, halfWidth + kerbW + gravelW + 5.0f, 14.0f, kCorner);
            Assert.That(surfGrass, Is.EqualTo(SurfaceType.Grass));

            Console.WriteLine($"T9 Surface Classification: Verified Asphalt, Kerb, Gravel, Grass at expected offsets.");
        }

        // ── T10: Spawn Slot Position & Tangent Heading ────────────────────────
        [Test]
        public void T10_SpawnSlot_PositionAndTangentHeading()
        {
            var sp = config.spawn_slot;
            Assert.That(sp, Is.Not.Null, "Spawn slot must be present");
            Assert.That(sp.s, Is.EqualTo(4299.0f).Within(0.1f), "Spawn slot must be placed at s = 4299.0 m (10m behind S/F)");

            var q = spline.Query(sp.x, sp.z);
            float sDiff = MathF.Abs(q.s - 4299.0f);
            if (sDiff > config.total_length_m * 0.5f) sDiff = config.total_length_m - sDiff;

            // Track tangent heading at spawn point (deg)
            float tangentYawDeg = q.headingRad * (180.0f / MathF.PI);
            float headingDiff = MathF.Abs(sp.yaw_deg - tangentYawDeg);
            if (headingDiff > 180.0f) headingDiff = 360.0f - headingDiff;

            Console.WriteLine($"T10 Spawn Slot: s={q.s:0.2f}m (diff={sDiff:0.000}m, limit <= 0.5m), yaw={sp.yaw_deg:0.2}° vs tangent={tangentYawDeg:0.2}° (diff={headingDiff:0.00}°, limit <= 2°)");
            Assert.That(sDiff, Is.LessThanOrEqualTo(0.5f), $"Query(spawn).s deviates from 4299m by {sDiff:0.000} m");
            Assert.That(headingDiff, Is.LessThanOrEqualTo(2.0f), $"Spawn yaw deviates from track tangent by {headingDiff:0.00}°");
        }

        // ── T11: Every Sample Query(sample).s within 0.5m ─────────────────────
        [Test]
        public void T11_QuerySampleS_MatchesEverySampleWithin0_5m()
        {
            float maxDiff = 0f;
            int worstIdx = 0;

            for (int i = 0; i < config.samples.Count; i++)
            {
                var smp = config.samples[i];
                var q = spline.Query(smp.x, smp.z, i);

                float diff = MathF.Abs(q.s - smp.s);
                if (diff > config.total_length_m * 0.5f) diff = config.total_length_m - diff;

                if (diff > maxDiff)
                {
                    maxDiff = diff;
                    worstIdx = i;
                }
            }

            Console.WriteLine($"T11 Query All Samples: Max s difference={maxDiff:0.000}m at sample #{worstIdx} (limit <= 0.5m)");
            Assert.That(maxDiff, Is.LessThanOrEqualTo(0.5f), $"Query s error exceeds 0.5m at sample #{worstIdx}: {maxDiff:0.000} m");
        }

        // ── T12: LapTimer 200 km/h Lap, Sector Sum & Invalidation ─────────────
        [Test]
        public void T12_LapTimer_200Kmh_LapAndSectors()
        {
            var timer = new LapTimer(config);
            float speedMs = 200f / 3.6f;
            float dt = 1f / 120f;
            float expectedLapTime = config.total_length_m / speedMs; // ~77.562s

            float currentS = 0f;
            timer.Reset(currentS);

            // Run 1 full lap + 1 step
            float simTime = 0f;
            while (simTime < expectedLapTime + 1.0f)
            {
                currentS = (currentS + speedMs * dt) % config.total_length_m;
                var q = spline.QueryAtS(currentS);
                timer.Step(dt, currentS, q.headingRad, q.headingRad, false);
                simTime += dt;

                if (timer.LastLapTime > 0f) break;
            }

            Console.WriteLine($"T12 LapTimer: LastLapTime={timer.LastLapTime:F3}s vs Expected={expectedLapTime:F3}s, S1={timer.LastS1Time:F3}s, S2={timer.LastS2Time:F3}s, S3={timer.LastS3Time:F3}s");
            Assert.That(MathF.Abs(timer.LastLapTime - expectedLapTime), Is.LessThan(0.05f), $"Lap time mismatch: {timer.LastLapTime:F3}s vs {expectedLapTime:F3}s");

            float sectorSum = timer.LastS1Time + timer.LastS2Time + timer.LastS3Time;
            Assert.That(MathF.Abs(sectorSum - timer.LastLapTime), Is.LessThan(0.05f), $"Sectors do not sum to lap time: sum={sectorSum:F3}s, lap={timer.LastLapTime:F3}s");

            // Test skipping sector splits (e.g. starting mid-lap and crossing S/F without S1/S2)
            var timerSkip = new LapTimer(config);
            float midTrackS = config.sectors[1].end_s + 50f; // after S2
            timerSkip.Reset(midTrackS);
            timerSkip.Step(dt, midTrackS, 0f, 0f, false);
            timerSkip.Step(dt, config.total_length_m - 5f, 0f, 0f, false);
            // Finish lap crossing S/F without passing S1 or S2
            timerSkip.Step(dt, 10f, 0f, 0f, false);
            Assert.That(timerSkip.LastLapValid, Is.False, "Crossing S/F without passing splits must invalidate completed lap");

            // Test all wheels off track invalidates lap
            var timerOff = new LapTimer(config);
            timerOff.Reset(100f);
            timerOff.Step(dt, 100f, 0f, 0f, true); // off track
            Assert.That(timerOff.IsLapValid, Is.False, "All wheels off track must invalidate lap");

            // Test manual invalidation
            var timerManual = new LapTimer(config);
            timerManual.Reset(0f);
            timerManual.InvalidateLap();
            Assert.That(timerManual.IsLapValid, Is.False, "Manual invalidation must flag current lap invalid");

            // Test wrong-way detection
            var timerWrong = new LapTimer(config);
            timerWrong.Reset(100f);
            timerWrong.Step(dt, 100f, 0f, 0f, false);
            // Facing backward (> 120 deg = > 2.09 rad) for 2.1 s
            for (int i = 0; i < (int)(2.1f / dt); i++)
            {
                timerWrong.Step(dt, 100f, MathF.PI, 0f, false);
            }
            Assert.That(timerWrong.IsWrongWay, Is.True, "Wrong way detection must trigger after 2s of driving opposite to tangent");
        }

        // ── T13: Slope Physics (Downhill vs Uphill Acceleration) ───────────────
        [Test]
        public void T13_SlopePhysics_DownhillVsUphill()
        {
            var p = new CarParams();
            var vUp = new VehicleDynamics(p);
            var vDown = new VehicleDynamics(p);

            float slope5Pct = 0.05f;
            float pitchRad = MathF.Atan(slope5Pct);
            float dt = 1f / 120f;

            vUp.trackPitchRad = pitchRad;    // uphill
            vDown.trackPitchRad = -pitchRad; // downhill

            // Run coasting steps holding speed constant so low-pass filtered Ax converges to steady state
            for (int i = 0; i < 40; i++)
            {
                vUp.Vx = 100f / 3.6f;
                vDown.Vx = 100f / 3.6f;
                vUp.Step(dt, 0f, 0f, 0f);
                vDown.Step(dt, 0f, 0f, 0f);
            }

            float g = 9.81f;
            float expectedDiffAcc = 2.0f * g * MathF.Sin(pitchRad); // (a_down - a_up)
            float measuredDiffAcc = (vDown.Ax - vUp.Ax);

            Console.WriteLine($"T13 Slope Physics: diffAcc={measuredDiffAcc:F4} m/s^2 vs expected={expectedDiffAcc:F4} m/s^2 (5% slope)");
            Assert.That(vDown.Ax, Is.GreaterThan(vUp.Ax), "Downhill acceleration must be higher than uphill");
            Assert.That(MathF.Abs(measuredDiffAcc - expectedDiffAcc) / expectedDiffAcc, Is.LessThan(0.05f), "Slope acceleration diff must match 2*g*sin(pitch) within 5%");
        }

        // ── T14: Surface Grip & Drag (Grass vs Asphalt) ────────────────────────
        [Test]
        public void T14_SurfaceGripAndDrag_Effects()
        {
            var p = new CarParams();
            var vAsphalt = new VehicleDynamics(p);
            var vGrass = new VehicleDynamics(p);

            float dt = 1f / 120f;
            vAsphalt.Vx = 150f / 3.6f;
            vGrass.Vx = 150f / 3.6f;

            // vAsphalt default surface drag 1.0
            // vGrass on grass (2 wheels on grass, grass drag = 1.80)
            vGrass.surfaceDragRL = 1.80f;
            vGrass.surfaceDragRR = 1.80f;
            vGrass.surfaceGripRL = 0.55f;
            vGrass.surfaceGripRR = 0.55f;

            // Coast down (throttle 0, brake 0)
            vAsphalt.Step(dt, 0f, 0f, 0f);
            vGrass.Step(dt, 0f, 0f, 0f);

            Console.WriteLine($"T14 Surface Drag Decel: Asphalt Ax={vAsphalt.Ax:F3} m/s^2, Grass Ax={vGrass.Ax:F3} m/s^2");
            Assert.That(vGrass.Ax, Is.LessThan(vAsphalt.Ax), "Car with wheels on grass must experience more deceleration drag than pure asphalt");
        }
    }
}
