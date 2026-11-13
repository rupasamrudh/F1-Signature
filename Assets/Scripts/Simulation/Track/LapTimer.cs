// LapTimer.cs — Pure C# lap timer with sub-step crossing interpolation, sector tracking, wrong-way detection, and validity rules.
using System;

namespace F1Sim.Simulation.Track
{
    public enum SectorStatus
    {
        None,
        SlowerThanBest, // Yellow
        FasterThanBest, // Green
        BestEver        // Purple
    }

    public class LapTimer
    {
        public readonly float TrackLength;
        public readonly float Sector1SplitS;
        public readonly float Sector2SplitS;

        // Active lap state
        public float CurrentLapTime { get; private set; }
        public float LastLapTime { get; private set; }
        public float BestLapTime { get; private set; }
        public bool  HasBestLap => BestLapTime > 0f;

        public bool IsLapValid { get; private set; } = true;
        public bool LastLapValid { get; private set; } = true;
        public bool IsWrongWay { get; private set; } = false;
        public bool HasLapStarted => _hasStarted;

        // Current lap sector times
        public float CurrentS1Time { get; private set; }
        public float CurrentS2Time { get; private set; }
        public float CurrentS3Time { get; private set; }

        // Best lap sector times
        public float BestS1Time { get; private set; }
        public float BestS2Time { get; private set; }
        public float BestS3Time { get; private set; }

        // Last completed lap sectors
        public float LastS1Time { get; private set; }
        public float LastS2Time { get; private set; }
        public float LastS3Time { get; private set; }

        public SectorStatus S1Status { get; private set; } = SectorStatus.None;
        public SectorStatus S2Status { get; private set; } = SectorStatus.None;
        public SectorStatus S3Status { get; private set; } = SectorStatus.None;

        // Live delta table: time-at-distance every 10m on best lap
        public readonly float[] BestLapTimeAtDistance; // length = ceil(TrackLength / 10) + 1
        public readonly int DeltaSampleCount;

        // Tracking internals
        float _prevS;
        bool  _passedS1;
        bool  _passedS2;
        float _wrongWayTimer;
        bool  _hasStarted;

        // Buffer for recording time-at-distance during the current lap
        readonly float[] _currentLapTimeAtDistance;

        public LapTimer(TrackConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            TrackLength = config.total_length_m;

            // Sectors: 0 to S1, S1 to S2, S2 to TrackLength
            if (config.sectors != null && config.sectors.Count >= 2)
            {
                Sector1SplitS = config.sectors[0].end_s;
                Sector2SplitS = config.sectors[1].end_s;
            }
            else
            {
                Sector1SplitS = TrackLength / 3f;
                Sector2SplitS = 2f * TrackLength / 3f;
            }

            DeltaSampleCount = (int)MathF.Ceiling(TrackLength / 10f) + 1;
            BestLapTimeAtDistance = new float[DeltaSampleCount];
            _currentLapTimeAtDistance = new float[DeltaSampleCount];
            _prevS = -1f;
        }

        public void Reset(float currentS = 0f)
        {
            CurrentLapTime = 0f;
            CurrentS1Time = 0f;
            CurrentS2Time = 0f;
            CurrentS3Time = 0f;
            IsLapValid = true;
            IsWrongWay = false;
            _wrongWayTimer = 0f;
            _passedS1 = false;
            _passedS2 = false;
            _hasStarted = false;
            _prevS = currentS;
            Array.Clear(_currentLapTimeAtDistance, 0, _currentLapTimeAtDistance.Length);
        }

        public void SetBestLap(float bestTime, float s1 = 0f, float s2 = 0f, float s3 = 0f, float[] deltaTable = null)
        {
            BestLapTime = bestTime;
            BestS1Time = s1 > 0f ? s1 : bestTime / 3f;
            BestS2Time = s2 > 0f ? s2 : bestTime / 3f;
            BestS3Time = s3 > 0f ? s3 : bestTime / 3f;
            if (deltaTable != null && deltaTable.Length == DeltaSampleCount)
            {
                Array.Copy(deltaTable, BestLapTimeAtDistance, DeltaSampleCount);
            }
            else
            {
                // Generate uniform baseline
                for (int i = 0; i < DeltaSampleCount; i++)
                {
                    float s = i * 10f;
                    BestLapTimeAtDistance[i] = (s / TrackLength) * bestTime;
                }
            }
        }

        public void InvalidateLap()
        {
            IsLapValid = false;
        }

        public float GetLiveDelta(float currentS)
        {
            if (!HasBestLap) return 0f;
            int idx = (int)(currentS / 10f);
            if (idx < 0) idx = 0;
            if (idx >= DeltaSampleCount - 1) idx = DeltaSampleCount - 2;

            float frac = (currentS - idx * 10f) / 10f;
            float bestTimeAtS = BestLapTimeAtDistance[idx] + frac * (BestLapTimeAtDistance[idx + 1] - BestLapTimeAtDistance[idx]);
            return CurrentLapTime - bestTimeAtS;
        }

        /// Step lap timer by dt with current vehicle progress.
        public void Step(float dt, float currentS, float headingRad, float trackTangentHeadingRad, bool allWheelsOffTrack)
        {
            if (_prevS < 0f)
            {
                _prevS = currentS;
                return;
            }

            if (_hasStarted && allWheelsOffTrack)
            {
                InvalidateLap();
            }

            // Wrong-way detection: heading against track tangent by > 120 deg (2.094 rad) for 2 s
            float headingDiff = MathF.Abs(headingRad - trackTangentHeadingRad);
            while (headingDiff > MathF.PI) headingDiff = 2f * MathF.PI - headingDiff;

            if (headingDiff > (120f * MathF.PI / 180f))
            {
                _wrongWayTimer += dt;
                if (_wrongWayTimer >= 2.0f)
                {
                    IsWrongWay = true;
                    if (_hasStarted) InvalidateLap();
                }
            }
            else
            {
                _wrongWayTimer = 0f;
                IsWrongWay = false;
            }

            if (_hasStarted)
            {
                CurrentLapTime += dt;

                // Record time-at-distance table every 10m
                int dIdx = (int)(currentS / 10f);
                if (dIdx >= 0 && dIdx < DeltaSampleCount && _currentLapTimeAtDistance[dIdx] == 0f)
                {
                    _currentLapTimeAtDistance[dIdx] = CurrentLapTime;
                }

                // S1 Split crossing (Sector1SplitS)
                if (!_passedS1 && DidCrossForward(_prevS, currentS, Sector1SplitS))
                {
                    float frac = CalculateCrossingFraction(_prevS, currentS, Sector1SplitS);
                    CurrentS1Time = (CurrentLapTime - dt) + frac * dt;
                    _passedS1 = true;
                    UpdateS1Status();
                }

                // S2 Split crossing (Sector2SplitS)
                if (_passedS1 && !_passedS2 && DidCrossForward(_prevS, currentS, Sector2SplitS))
                {
                    float frac = CalculateCrossingFraction(_prevS, currentS, Sector2SplitS);
                    float s2CrossingTime = (CurrentLapTime - dt) + frac * dt;
                    CurrentS2Time = s2CrossingTime - CurrentS1Time;
                    _passedS2 = true;
                    UpdateS2Status();
                }
            }

            // S/F Line crossing (wrap-around from ~TrackLength to ~0)
            if (DidCrossStartFinish(_prevS, currentS))
            {
                float frac = CalculateStartFinishCrossingFraction(_prevS, currentS);
                float lapEndTime = (CurrentLapTime - dt) + frac * dt;

                if (!_hasStarted)
                {
                    // First time crossing start line: timed lap 1 begins
                    _hasStarted = true;
                    IsLapValid = true;
                    float nextLapInitialTime = (1f - frac) * dt;
                    CurrentLapTime = MathF.Max(0f, nextLapInitialTime);
                    CurrentS1Time = 0f;
                    CurrentS2Time = 0f;
                    CurrentS3Time = 0f;
                    _passedS1 = false;
                    _passedS2 = false;
                    Array.Clear(_currentLapTimeAtDistance, 0, _currentLapTimeAtDistance.Length);
                }
                else
                {
                    if (_passedS1 && _passedS2)
                    {
                        // Valid sequence of splits
                        CurrentS3Time = lapEndTime - (CurrentS1Time + CurrentS2Time);
                        LastLapTime = lapEndTime;
                        LastS1Time = CurrentS1Time;
                        LastS2Time = CurrentS2Time;
                        LastS3Time = CurrentS3Time;
                        LastLapValid = IsLapValid;

                        UpdateS3Status();

                        if (IsLapValid && (BestLapTime <= 0f || LastLapTime < BestLapTime))
                        {
                            BestLapTime = LastLapTime;
                            BestS1Time = LastS1Time;
                            BestS2Time = LastS2Time;
                            BestS3Time = LastS3Time;
                            Array.Copy(_currentLapTimeAtDistance, BestLapTimeAtDistance, DeltaSampleCount);
                            BestLapTimeAtDistance[DeltaSampleCount - 1] = BestLapTime;
                        }
                    }
                    else
                    {
                        // Shortcuts or skipped splits invalidate lap
                        InvalidateLap();
                        LastLapValid = false;
                    }

                    // Reset for next lap
                    float nextLapInitialTime = (1f - frac) * dt;
                    CurrentLapTime = MathF.Max(0f, nextLapInitialTime);
                    CurrentS1Time = 0f;
                    CurrentS2Time = 0f;
                    CurrentS3Time = 0f;
                    _passedS1 = false;
                    _passedS2 = false;
                    IsLapValid = true;
                    Array.Clear(_currentLapTimeAtDistance, 0, _currentLapTimeAtDistance.Length);
                }
            }

            _prevS = currentS;
        }

        bool DidCrossForward(float prevS, float currS, float splitS)
        {
            if (prevS <= currS)
                return prevS <= splitS && currS >= splitS;
            return false;
        }

        float CalculateCrossingFraction(float prevS, float currS, float splitS)
        {
            float ds = currS - prevS;
            if (MathF.Abs(ds) < 1e-4f) return 0.5f;
            return Math.Clamp((splitS - prevS) / ds, 0f, 1f);
        }

        bool DidCrossStartFinish(float prevS, float currS)
        {
            // Wrapped from near end (> 0.75 * L) to near start (< 0.25 * L)
            return prevS > TrackLength * 0.75f && currS < TrackLength * 0.25f;
        }

        float CalculateStartFinishCrossingFraction(float prevS, float currS)
        {
            float ds = (TrackLength - prevS) + currS;
            if (MathF.Abs(ds) < 1e-4f) return 0.5f;
            return Math.Clamp((TrackLength - prevS) / ds, 0f, 1f);
        }

        void UpdateS1Status()
        {
            if (BestS1Time <= 0f) S1Status = SectorStatus.BestEver;
            else if (CurrentS1Time < BestS1Time) S1Status = SectorStatus.BestEver;
            else S1Status = SectorStatus.SlowerThanBest;
        }

        void UpdateS2Status()
        {
            if (BestS2Time <= 0f) S2Status = SectorStatus.BestEver;
            else if (CurrentS2Time < BestS2Time) S2Status = SectorStatus.BestEver;
            else S2Status = SectorStatus.SlowerThanBest;
        }

        void UpdateS3Status()
        {
            if (BestS3Time <= 0f) S3Status = SectorStatus.BestEver;
            else if (CurrentS3Time < BestS3Time) S3Status = SectorStatus.BestEver;
            else S3Status = SectorStatus.SlowerThanBest;
        }
    }
}
