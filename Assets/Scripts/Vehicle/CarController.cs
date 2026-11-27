using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using F1Sim.Simulation;
using F1Sim.Simulation.Track;
using F1Sim.Track;

[RequireComponent(typeof(CarInput))]
public class CarController : MonoBehaviour
{
    public CarParams config = new CarParams();

    // ── Properties exposed to CarInput and CarAudioManager ────────────────
    public float SpeedKmh => model != null ? model.TotalSpeedKmh : 0f;
    public float ForwardSpeedMs => model != null ? model.Vx : 0f;
    public float LongAccel => model != null ? model.Ax : 0f;
    public float LongG     => model != null ? model.Ax / 9.81f : 0f;
    public float LatG     => model != null ? model.LatG : 0f;
    public float YawRate  => model != null ? model.YawRate : 0f;
    public float SteerAngleDeg => model != null ? model.FinalSteerAngleDeg : 0f;
    public float SteerAngleDegFL => model != null ? model.SteerFLDeg : SteerAngleDeg;
    public float SteerAngleDegFR => model != null ? model.SteerFRDeg : SteerAngleDeg;
    public float Throttle => input != null ? input.Throttle : 0f;
    public float Brake => input != null ? input.Brake : 0f;
    public float EngineRpm => model != null ? model.EngineRpm : 1000f;
    public int Gear => model != null ? model.Gear : 1;
    public bool IsShifting => model != null ? model.IsShifting : false;
    public float MaxTireSlip => model != null ? model.MaxTireSlip : 0f;

    // ── Pit Limiter & Aero DRS Properties ───────────────────────────────────
    public bool isPitLimiterActive = false;
    public const float PIT_LIMIT_KMH = 80.0f;
    public bool IsDrsActive => model != null && model.Aero.CurrentMode == AeroMode.Straight;

    public void TogglePitLimiter()
    {
        isPitLimiterActive = !isPitLimiterActive;
    }

    // ── Collision Event for CarAudioManager ───────────────────────────────
    public event Action<float, Vector3> OnBarrierCollision;

    // ── HUD brake-bias adjustment ─────────────────────────────────────────
    [Header("HUD")]
    public float brakeBiasStep = 0.01f;

    // ── Public Surface Multipliers ─────────────────────────────────────────
    [Header("Surface Multipliers")]
    public float asphaltGrip = 1.0f;
    public float kerbGrip    = 0.92f;
    public float gravelGrip  = 0.45f;
    public float grassGrip   = 0.55f;

    public float asphaltDrag = 1.0f;
    public float kerbDrag    = 1.05f;
    public float gravelDrag  = 3.50f;
    public float grassDrag   = 1.80f;

    [System.Obsolete("Replaced by physical 3D model barriers & dynamic envelope")]
    public float barrierOffset = 18.0f;

    CarInput        input;
    VehicleDynamics model;
    Telemetry       telemetry;
    TrackSpline     spline;
    TrackConfig     trackConfig;
    LapTimer        lapTimer;
    DemoDriver      demoDriver;
    ChaseCamera     chaseCam;

    public bool isAutoLapActive = false;

    Vector3 prevPos, currPos;
    Quaternion prevRot, currRot;
    float   prevYaw, currYaw;
    Vector3    startPos;
    Quaternion startRot;
    float   startYaw;
    float   autoResetTimer;
    float   offTrackTimer;

    // ── Visual Wheel Geometry (Ferrari SF-26 Prefab) ───────────────────────
    public const float kVisualWheelbase   = 3.533f;
    public const float kVisualFrontAxleZ  = 1.522f;
    public const float kVisualRearAxleZ   = -2.011f;
    public const float kVisualAxleMidZ    = (kVisualFrontAxleZ + kVisualRearAxleZ) * 0.5f; // -0.2445f
    public const float kVisualTrackWidthF = 1.600f;
    public const float kVisualTrackWidthR = 1.526f;
    public const float kVisualTireRadius  = 0.358f;

    // ── Persistent Spline & Suspension Tracking ────────────────────────────
    int   carSplineIndex = 0;
    float smoothGroundY;
    float smoothPitchDeg;
    float smoothRollDeg;
    bool  isGroundInitialized = false;
    bool  isCarOverRoadOrKerb = false;

    // Real-time wheel ground tracking exposed to WheelVisualBinder for suspension travel
    [System.NonSerialized] public float lastGroundFL;
    [System.NonSerialized] public float lastGroundFR;
    [System.NonSerialized] public float lastGroundRL;
    [System.NonSerialized] public float lastGroundRR;

    // ── Minimap ────────────────────────────────────────────────────────────
    Texture2D minimapTex;

    // ── Version reset banner timer ───────────────────────────────────────
    float resetBannerTimer;

    // ── Track Data Missing & Build Stamp ─────────────────────────────────
    public bool isTrackDataMissing = false;
    string buildStampLine = "";
    GUIStyle stampStyle;

    // ── Track Visibility & Debug Visuals ─────────────────────────────────
    public bool isDebugActive = false;
    GameObject debugVisualRoot;
    LineRenderer debugLine;
    int totalTrackChunks = 0;
    List<Renderer> trackChunkRenderers = new List<Renderer>();
    string spawnOffsetError = "";

    // ── Profiler / Performance Stats ──────────────────────────────────────
    Stopwatch stepTimer = new Stopwatch();
    float     avgStepMs;
    const float kAlpha = 0.05f; // EMA smoothing
    float     fpsAvg = 60f;
    float     frameTimeAvgMs = 16.6f;

    // ── HUD styles ────────────────────────────────────────────────────────
    GUIStyle labelStyle;
    GUIStyle boxStyle;
    GUIStyle shiftStyle;
    GUIStyle bannerStyle;
    GUIStyle speedNumStyle;
    GUIStyle speedUnitStyle;
    GUIStyle gearStyle;
    GUIStyle hudSubLabelStyle;
    Texture2D ledGreenTex;
    Texture2D ledRedTex;
    Texture2D ledBlueTex;
    Texture2D ledOffTex;
    Texture2D ledShiftTex;
    Texture2D thrBarTex;
    Texture2D brkBarTex;
    Texture2D barBgTex;
    Texture2D clusterBgTex;
    bool     stylesReady;

    string lastSavePath = "";
    float csvBannerTimer = 0f;

    public void ShowResetBanner(float duration = 5.0f)
    {
        resetBannerTimer = duration;
    }

    void Awake()
    {
        Physics.queriesHitBackfaces = true;
        input = GetComponent<CarInput>();

        // Ensure Vehicle Audio System is initialized on the vehicle hierarchy
        if (GetComponent<CarAudioManager>() == null)
        {
            gameObject.AddComponent<CarAudioManager>();
        }

        // Build Stamp line calculation & startup log
        int chunksLoaded = 0;
        var trackObj = GameObject.Find("Track_Built");
        if (trackObj == null) trackObj = GameObject.Find("Track");
        if (trackObj != null)
        {
            for (int i = 0; i < trackObj.transform.childCount; i++)
            {
                if (trackObj.transform.GetChild(i).name.StartsWith("Chunk_"))
                    chunksLoaded++;
            }
        }
        buildStampLine = F1Sim.Build.BuildInfoLoader.GetStampLine(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            chunksLoaded);
        UnityEngine.Debug.Log($"[CarController] {buildStampLine}");

        // Parameter version check
        if (config.paramsVersion != CarParams.CURRENT_VERSION)
        {
            var def = new CarParams { paramsVersion = CarParams.CURRENT_VERSION };
            config = def;
            ShowResetBanner();
        }

        // Load track data
        trackConfig = TrackDataLoader.LoadTrack();
        if (trackConfig == null)
        {
            isTrackDataMissing = true;
            UnityEngine.Debug.LogError("[CarController] TRACK DATA MISSING! Resources/Tracks/f1sig_track failed to load.");
            return;
        }

        spline = new TrackSpline(trackConfig);
        lapTimer = new LapTimer(trackConfig);
        demoDriver = new DemoDriver(spline);
        minimapTex = MinimapDrawer.GenerateMinimapTexture(trackConfig, 160, 110);

        // Set air density from track data
        config.rho = trackConfig.airDensity;

        // Load saved best lap
        float savedBest = PlayerPrefs.GetFloat("F1Sig_BestLap", 0f);
        if (savedBest > 0f) lapTimer.SetBestLap(savedBest);

        // Spawn slot positioning if available
        if (trackConfig.spawn_slot != null)
        {
            var sp = trackConfig.spawn_slot;
            startYaw = sp.yaw_deg * Mathf.Deg2Rad;
            ComputeGroundAlignment(sp.x, sp.z, startYaw, out float groundY, out float startPitch, out float startRoll, out _);
            startPos = new Vector3(sp.x, groundY, sp.z);
            startRot = Quaternion.Euler(startPitch, sp.yaw_deg, startRoll);
            smoothGroundY = groundY;
            smoothPitchDeg = startPitch;
            smoothRollDeg = startRoll;
        }
        else
        {
            startPos = transform.position;
            startRot = transform.rotation;
            startYaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            smoothGroundY = startPos.y;
            smoothPitchDeg = 0f;
            smoothRollDeg = 0f;
        }

        transform.position = startPos;
        transform.rotation = startRot;

        model = new VehicleDynamics(config)
        {
            X   = startPos.x,
            Z   = startPos.z,
            Yaw = startYaw
        };

        currPos = prevPos = startPos;
        currRot = prevRot = startRot;
        currYaw = prevYaw = startYaw;
        isGroundInitialized = true;
        carSplineIndex = 0;

        // Immediately align chase camera behind car
        var chaseCam = FindAnyObjectByType<ChaseCamera>();
        if (chaseCam != null) chaseCam.SnapToTarget();

        // Assert: car lateral offset at start must be under 3 m
        if (spline != null)
        {
            var startQ = spline.Query(startPos.x, startPos.z);
            carSplineIndex = startQ.nearestIndex;
            if (lapTimer != null) lapTimer.Reset(startQ.s);
            float startOffset = MathF.Abs(startQ.lateralOffset);
            if (startOffset >= 3.0f)
            {
                spawnOffsetError = $"SPAWN OFFSET ERROR: {startOffset:F2}m";
                UnityEngine.Debug.LogError($"[CarController] {spawnOffsetError} at start position ({startPos.x:F2}, {startPos.z:F2})");
            }
            else
            {
                UnityEngine.Debug.Log($"[CarController] Spawn verified: s={startQ.s:F1}m, offset={startQ.lateralOffset:F2}m (<3m), surface={startQ.surface}");
            }
        }

        // Cache track chunk renderers for visibility counting
        if (trackObj != null)
        {
            totalTrackChunks = 0;
            trackChunkRenderers.Clear();
            for (int i = 0; i < trackObj.transform.childCount; i++)
            {
                var child = trackObj.transform.GetChild(i);
                if (child.name.StartsWith("Chunk_"))
                {
                    totalTrackChunks++;
                    var r = child.GetComponentInChildren<Renderer>();
                    if (r != null) trackChunkRenderers.Add(r);
                }
            }
        }

        SetupDebugVisuals();

        telemetry = new Telemetry(120f, 300);
        telemetry.StartRecording();
    }

    public void ResetCar()
    {
        SaveTelemetry();
        ComputeGroundAlignment(startPos.x, startPos.z, startYaw, out float groundY, out float startPitch, out float startRoll, out _);
        startPos = new Vector3(startPos.x, groundY, startPos.z);
        startRot = Quaternion.Euler(startPitch, startYaw * Mathf.Rad2Deg, startRoll);
        model.Reset(startPos.x, startPos.z, startYaw);
        currPos = prevPos = startPos;
        currRot = prevRot = startRot;
        currYaw = prevYaw = startYaw;
        smoothGroundY = groundY;
        smoothPitchDeg = startPitch;
        smoothRollDeg = startRoll;
        carSplineIndex = 0;
        transform.position = startPos;
        transform.rotation = startRot;
        autoResetTimer = 0f;
        offTrackTimer = 0f;
        avgStepMs = 0f;
        isGroundInitialized = true;
        if (spline != null)
        {
            var startQ = spline.Query(startPos.x, startPos.z);
            carSplineIndex = startQ.nearestIndex;
            if (lapTimer != null) lapTimer.Reset(startQ.s);
        }
        else
        {
            carSplineIndex = 0;
            if (lapTimer != null) lapTimer.Reset();
        }
        var chaseCam = FindAnyObjectByType<ChaseCamera>();
        if (chaseCam != null) chaseCam.SnapToTarget();
        telemetry.StartRecording();
        if (input != null) input.ResetInput();
    }

    public void ResetToNearestTrack()
    {
        if (spline == null) { ResetCar(); return; }
        var q = spline.Query(model.X, model.Z, carSplineIndex);
        carSplineIndex = q.nearestIndex;
        float yawRad = q.headingRad;
        model.Reset(q.x, q.z, yawRad);
        ComputeGroundAlignment(q.x, q.z, yawRad, out float groundY, out float pitchDeg, out float rollDeg, out _);
        currPos = prevPos = new Vector3(q.x, groundY, q.z);
        currYaw = prevYaw = yawRad;
        smoothGroundY = groundY;
        smoothPitchDeg = pitchDeg;
        smoothRollDeg = rollDeg;
        isGroundInitialized = true;
        transform.position = currPos;
        transform.rotation = Quaternion.Euler(pitchDeg, yawRad * Mathf.Rad2Deg, rollDeg);
        var chaseCam = FindAnyObjectByType<ChaseCamera>();
        if (chaseCam != null) chaseCam.SnapToTarget();
        offTrackTimer = 0f;
    }

    public void TeleportToCorner(int cornerIndex)
    {
        if (trackConfig == null || trackConfig.corners == null || trackConfig.corners.Count == 0) return;
        int idx = (cornerIndex + trackConfig.corners.Count) % trackConfig.corners.Count;
        var c = trackConfig.corners[idx];
        float targetS = (c.apex_s - 150f + trackConfig.total_length_m) % trackConfig.total_length_m;
        var q = spline.QueryAtS(targetS);
        carSplineIndex = q.nearestIndex;

        model.Reset(q.x, q.z, q.headingRad);
        model.Vx = 150f / 3.6f; // 150 km/h
        ComputeGroundAlignment(q.x, q.z, q.headingRad, out float groundY, out float pitchDeg, out float rollDeg, out _);
        currPos = prevPos = new Vector3(q.x, groundY, q.z);
        currYaw = prevYaw = q.headingRad;
        smoothGroundY = groundY;
        smoothPitchDeg = pitchDeg;
        smoothRollDeg = rollDeg;
        isGroundInitialized = true;
        transform.position = currPos;
        transform.rotation = Quaternion.Euler(pitchDeg, q.headingRad * Mathf.Rad2Deg, rollDeg);
        var chaseCam = FindAnyObjectByType<ChaseCamera>();
        if (chaseCam != null) chaseCam.SnapToTarget();
    }

    void SaveTelemetry()
    {
        if (telemetry == null || telemetry.Count == 0) return;
        string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = System.IO.Path.Combine(Application.persistentDataPath, $"telemetry_{ts}.csv");
        try
        {
            System.IO.File.WriteAllText(path, telemetry.ToCsv(config));
            lastSavePath = path;
            csvBannerTimer = 4.0f;
            UnityEngine.Debug.Log($"[Telemetry] Saved {telemetry.Count} frames → {path}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[Telemetry] Save failed: {e.Message}");
        }
    }

    void FixedUpdate()
    {
        if (isTrackDataMissing || model == null) return;

        prevPos = currPos;
        prevRot = currRot;
        prevYaw = currYaw;

        float steerCmd = input.Steer;
        float thrCmd   = input.Throttle;
        float brkCmd   = input.Brake;

        if (isAutoLapActive && demoDriver != null)
        {
            var (aSteer, aThr, aBrk) = demoDriver.Step(model.X, model.Z, model.Yaw, model.Vx, model.Vy);
            steerCmd = aSteer;
            thrCmd = aThr;
            brkCmd = aBrk;
        }

        // ── Pit Limiter (80 km/h Governor) ────────────────────────────────
        if (isPitLimiterActive)
        {
            float speedKmh = model.TotalSpeedKmh;
            if (model.Vx > 0.05f) // Apply to forward driving
            {
                if (speedKmh >= PIT_LIMIT_KMH)
                {
                    thrCmd = 0f;
                    if (speedKmh > PIT_LIMIT_KMH + 0.3f)
                    {
                        // Mild automated governor braking if entering pitlane hot
                        float overspeedKmh = speedKmh - PIT_LIMIT_KMH;
                        float govBrake = Mathf.Clamp01(overspeedKmh * 0.15f);
                        brkCmd = Mathf.Max(brkCmd, govBrake);
                    }
                }
                else if (speedKmh >= PIT_LIMIT_KMH - 6.0f)
                {
                    // Smooth taper between 74 km/h and 80 km/h to avoid oscillations
                    float headroom = (PIT_LIMIT_KMH - speedKmh) / 6.0f;
                    float maxAllowedThr = Mathf.Clamp01(headroom * 0.85f + 0.15f);
                    thrCmd = Mathf.Min(thrCmd, maxAllowedThr);
                }
            }
        }

        // ── 3D Track Query for Wheel Surfaces & Gravity Pitch ─────────────
        float carS = 0f;
        float tangentHeading = 0f;
        bool allWheelsOff = false;

        if (spline != null)
        {
            var qCar = spline.Query(model.X, model.Z, carSplineIndex);
            carSplineIndex = qCar.nearestIndex;
            carS = qCar.s;
            tangentHeading = qCar.headingRad;
            model.trackPitchRad = Mathf.Atan(qCar.slope);

            // Real 3D Physical Ground Height, Pitch, Roll & Ride Height for Ferrari SF-26
            ComputeGroundAlignment(model.X, model.Z, model.Yaw, out float targetY, out float targetPitchDeg, out float targetRollDeg, out isCarOverRoadOrKerb);

            // Set surface grip & rolling drag
            bool inPit = IsInPitCorridor(model.X, model.Z);
            if (inPit || isCarOverRoadOrKerb)
            {
                (model.surfaceGripFL, model.surfaceDragFL) = (asphaltGrip, asphaltDrag);
                (model.surfaceGripFR, model.surfaceDragFR) = (asphaltGrip, asphaltDrag);
                (model.surfaceGripRL, model.surfaceDragRL) = (asphaltGrip, asphaltDrag);
                (model.surfaceGripRR, model.surfaceDragRR) = (asphaltGrip, asphaltDrag);
                allWheelsOff = false;
            }
            else
            {
                (model.surfaceGripFL, model.surfaceDragFL) = GetSurfaceProps(qCar.surface);
                (model.surfaceGripFR, model.surfaceDragFR) = GetSurfaceProps(qCar.surface);
                (model.surfaceGripRL, model.surfaceDragRL) = GetSurfaceProps(qCar.surface);
                (model.surfaceGripRR, model.surfaceDragRR) = GetSurfaceProps(qCar.surface);

                allWheelsOff = (qCar.surface != SurfaceType.Asphalt);
            }

            // ── Physical Barrier Collision Resolution (Dual 3D Mesh + Envelope) ───
            ResolveBarrierCollisions(qCar);

            float dt = Time.fixedDeltaTime;

            if (!isGroundInitialized)
            {
                smoothGroundY = targetY;
                smoothPitchDeg = targetPitchDeg;
                smoothRollDeg = targetRollDeg;
                isGroundInitialized = true;
            }
            else
            {
                // Stiff, high-downforce F1 pushrod suspension tracking:
                // Hard bump & rebound coupling ensures the chassis stays firmly planted on the track
                // Eliminates mushy, jelly float while moving and steering
                if (targetY > smoothGroundY)
                {
                    // Instant clearance floor: never allow chassis origin to fall below minimum ground clearance envelope
                    smoothGroundY = Mathf.Max(smoothGroundY, targetY - 0.002f);
                    smoothGroundY = Mathf.Lerp(smoothGroundY, targetY, 1f - Mathf.Exp(-85f * dt));
                }
                else
                {
                    smoothGroundY = Mathf.Lerp(smoothGroundY, targetY, 1f - Mathf.Exp(-50f * dt));
                }
                smoothPitchDeg = Mathf.LerpAngle(smoothPitchDeg, targetPitchDeg, 1f - Mathf.Exp(-60f * dt));
                smoothRollDeg = Mathf.LerpAngle(smoothRollDeg, targetRollDeg, 1f - Mathf.Exp(-60f * dt));
            }
        }

        stepTimer.Restart();
        float prevStepX = model.X;
        float prevStepZ = model.Z;
        model.Step(Time.fixedDeltaTime, steerCmd, thrCmd, brkCmd);
        stepTimer.Stop();

        // ── Continuous Collision Detection (CCD) Anti-Tunneling Sweep ────
        EnforceSweptContinuousCollision(prevStepX, prevStepZ);

        // Enforce physical barriers immediately after vehicle motion step
        if (spline != null)
        {
            var qAfter = spline.Query(model.X, model.Z, carSplineIndex);
            ResolveBarrierCollisions(qAfter);

            currPos = new Vector3(model.X, smoothGroundY, model.Z);
            currRot = Quaternion.Euler(smoothPitchDeg, model.Yaw * Mathf.Rad2Deg, smoothRollDeg);
        }
        else
        {
            currPos = new Vector3(model.X, startPos.y, model.Z);
            currRot = Quaternion.Euler(0f, model.Yaw * Mathf.Rad2Deg, 0f);
        }

        float ms = (float)stepTimer.Elapsed.TotalMilliseconds;
        avgStepMs = avgStepMs + kAlpha * (ms - avgStepMs);

        // Step LapTimer
        if (lapTimer != null)
        {
            lapTimer.Step(Time.fixedDeltaTime, carS, model.Yaw, tangentHeading, allWheelsOff);
            if (lapTimer.BestLapTime > 0f)
            {
                PlayerPrefs.SetFloat("F1Sig_BestLap", lapTimer.BestLapTime);
            }
        }

        telemetry.Record(Time.time, model, thrCmd, brkCmd,
            input != null ? input.RawRollDeg : 0f,
            input != null ? input.RelDeg : 0f,
            input != null ? input.SteerTarget : 0f,
            input != null ? input.Steer : 0f);

        currYaw = model.Yaw;

        // Auto-reset check: only if car somehow goes > 6m outside the physical barrier
        // Do not auto-reset when the vehicle is driving inside the pitlane or over road mesh
        if (spline != null && !IsInPitCorridor(model.X, model.Z) && !isCarOverRoadOrKerb)
        {
            var qCheck = spline.Query(model.X, model.Z, carSplineIndex);
            float offTrackLimit = Mathf.Max(qCheck.barrierLeft, qCheck.barrierRight) + 6.0f;
            if (Mathf.Abs(qCheck.lateralOffset) > offTrackLimit)
            {
                offTrackTimer += Time.fixedDeltaTime;
                if (offTrackTimer >= 5.0f) ResetToNearestTrack();
            }
            else
            {
                offTrackTimer = 0f;
            }
        }
        else
        {
            offTrackTimer = 0f;
        }

        if (model.TotalSpeedKmh < 3f && MathF.Abs(model.Beta) > 1.2f)
        {
            autoResetTimer += Time.fixedDeltaTime;
            if (autoResetTimer >= 3f) ResetCar();
        }
        else
        {
            autoResetTimer = 0f;
        }
    }

    /// <summary>
    /// Checks whether world coordinates (x, z) are located within the Interlagos pitlane corridor or pit exit chute.
    /// The physical 3D pit wall, armco barriers, and garage structures handle collision via real MeshColliders.
    /// </summary>
    public bool IsInPitCorridor(float x, float z)
    {
        // 1. Main pit straight / garages / pit entry (X in [-70, -5], Z in [-330, 385])
        if (x >= -70f && x <= -5f && z >= -330f && z <= 385f)
            return true;

        // 2. Complete Pit Exit chute from pit exit lights through Senna S and Curva do Sol (Turn 3)
        // all the way to the Reta Oposta blend (X in [-470, -15], Z in [335, 495])
        if (x >= -470f && x <= -15f && z >= 335f && z <= 495f)
            return true;

        return false;
    }

    (float grip, float drag) GetSurfaceProps(SurfaceType surface)
    {
        return surface switch
        {
            SurfaceType.Asphalt => (asphaltGrip, asphaltDrag),
            SurfaceType.Kerb    => (kerbGrip, kerbDrag),
            SurfaceType.Gravel  => (gravelGrip, gravelDrag),
            SurfaceType.Grass   => (grassGrip, grassDrag),
            _ => (1f, 1f)
        };
    }

    bool IsBarrierCollider(Collider col, Vector3 normal)
    {
        if (col == null || col.transform.root == transform) return false;
        string colName = col.gameObject.name.ToLowerInvariant();

        // 1. Any collider in Track_Barriers or with barrier/wall/armco/fence/tyre/guard/concw in its name
        if (colName.Contains("barrier") || colName.Contains("wall") || colName.Contains("armco") ||
            colName.Contains("fence") || colName.Contains("tyre") || colName.Contains("guard") ||
            colName.Contains("concw"))
        {
            return true;
        }

        // 2. Trackside structures (pit building facades, bridge pillars, grandstand concrete bases, gantries)
        // or steep Terrain cliffs: Any vertical surface (|normal.y| < 0.65f) is a solid barrier!
        if (colName.Contains("trackside") || colName.Contains("terrain") || colName.Contains("building") || colName.Contains("structure"))
        {
            if (Mathf.Abs(normal.y) < 0.65f)
            {
                return true;
            }
        }

        return false;
    }

    void ApplyRigidCollisionResponse(Vector3 normal, float sinY, float cosY)
    {
        float wVx = model.Vx * sinY - model.Vy * cosY;
        float wVz = model.Vx * cosY + model.Vy * sinY;
        float vn = wVx * normal.x + wVz * normal.z;

        if (vn < 0f)
        {
            OnBarrierCollision?.Invoke(Mathf.Abs(vn), normal);

            float vtX = wVx - vn * normal.x;
            float vtZ = wVz - vn * normal.z;

            // Restitution 0.12 (solid wall bounce) + 40% tangential friction dissipation along the barrier
            float newWvx = vtX * 0.60f - normal.x * (vn * 0.12f);
            float newWvz = vtZ * 0.60f - normal.z * (vn * 0.12f);

            float newBodyVx = newWvx * sinY + newWvz * cosY;
            float newBodyVy = -newWvx * cosY + newWvz * sinY;

            // Allow small backward bounce (up to -2 m/s) to push the car cleanly away from the wall
            model.Vx = Mathf.Max(-2.0f, newBodyVx);
            model.Vy = newBodyVy;
        }
    }

    void EnforceSweptContinuousCollision(float prevX, float prevZ)
    {
        float dx = model.X - prevX;
        float dz = model.Z - prevZ;
        float moveDist = Mathf.Sqrt(dx * dx + dz * dz);
        if (moveDist < 0.0005f) return;

        Vector3 moveDir = new Vector3(dx, 0f, dz) / moveDist;
        float carYaw = model.Yaw;
        float sinY = Mathf.Sin(carYaw);
        float cosY = Mathf.Cos(carYaw);
        Vector3 fwd = new Vector3(sinY, 0f, cosY);
        Vector3 right = new Vector3(cosY, 0f, -sinY);

        float sweepY = smoothGroundY + 0.45f;
        Vector3 prevCenter = new Vector3(prevX, sweepY, prevZ);

        // 5 Swept Spheres covering the full F1 car bounding volume
        (Vector3 offset, float radius)[] sweepVolumes = new (Vector3, float)[]
        {
            (fwd * 1.80f, 0.90f),                      // Front wing & nose
            (Vector3.zero, 1.00f),                     // Mid chassis / sidepods
            (-fwd * 1.50f, 0.90f),                     // Rear axle & rear wing
            (fwd * 1.50f + right * 0.85f, 0.40f),      // Front-right wheel / wing tip
            (fwd * 1.50f - right * 0.85f, 0.40f)       // Front-left wheel / wing tip
        };

        float minAllowedDist = moveDist;
        Vector3 impactNormal = Vector3.zero;
        bool sweptHit = false;

        foreach (var (offset, r) in sweepVolumes)
        {
            Vector3 origin = prevCenter + offset;
            RaycastHit[] hits = Physics.SphereCastAll(origin, r, moveDir, moveDist + 0.15f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.distance <= 0f) continue;
                if (IsBarrierCollider(h.collider, h.normal))
                {
                    if (h.distance < minAllowedDist)
                    {
                        minAllowedDist = h.distance;
                        Vector3 n = new Vector3(h.normal.x, 0f, h.normal.z);
                        if (n.sqrMagnitude > 0.01f) impactNormal = n.normalized;
                        else impactNormal = -moveDir;
                        sweptHit = true;
                    }
                }
            }
        }

        if (sweptHit && minAllowedDist < moveDist)
        {
            float safeDist = Mathf.Max(0f, minAllowedDist - 0.02f);
            model.X = prevX + moveDir.x * safeDist;
            model.Z = prevZ + moveDir.z * safeDist;

            ApplyRigidCollisionResponse(impactNormal, sinY, cosY);
        }
    }

    // ── Physical Barrier Collision Resolution (Dual 3D Mesh + Envelope) ───
    void ResolveBarrierCollisions(TrackQueryResult q)
    {
        float carYaw = model.Yaw;
        float sinY = Mathf.Sin(carYaw);
        float cosY = Mathf.Cos(carYaw);

        // 1. Physical 3D MeshCollider Contact Feelers
        (float angle, float radius)[] feelers = new (float, float)[]
        {
            (0f, 2.75f),     // Nose center
            (20f, 2.50f),    // Front-right wing
            (45f, 2.00f),    // Front-right corner / wheel
            (90f, 1.10f),    // Right sidepod / flank
            (135f, 1.90f),   // Rear-right corner / wheel
            (160f, 2.40f),   // Rear-right wing
            (180f, 2.55f),   // Tail / diffuser center
            (200f, 2.40f),   // Rear-left wing
            (225f, 1.90f),   // Rear-left corner / wheel
            (270f, 1.10f),   // Left sidepod / flank
            (315f, 2.00f),   // Front-left corner / wheel
            (340f, 2.50f)    // Front-left wing
        };

        float maxPenetration = 0f;
        Vector3 pushNormalAvg = Vector3.zero;
        int hitCount = 0;

        float[] heights = new float[] { smoothGroundY + 0.30f, smoothGroundY + 0.60f };

        foreach (float hY in heights)
        {
            Vector3 origin = new Vector3(model.X, hY, model.Z);
            foreach (var (angle, radius) in feelers)
            {
                float rad = carYaw + angle * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

                RaycastHit[] hits = Physics.RaycastAll(origin, dir, radius + 0.20f, ~0, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (IsBarrierCollider(h.collider, h.normal))
                    {
                        if (h.distance < radius)
                        {
                            float penetration = radius - h.distance;
                            Vector3 n = new Vector3(h.normal.x, 0f, h.normal.z);
                            if (n.sqrMagnitude < 0.01f) n = -dir;
                            else n.Normalize();

                            if (penetration > maxPenetration)
                            {
                                maxPenetration = penetration;
                            }
                            pushNormalAvg += n;
                            hitCount++;
                        }
                    }
                }
            }
        }

        if (hitCount > 0 && maxPenetration > 0f)
        {
            pushNormalAvg.Normalize();
            float pushDist = maxPenetration + 0.05f;
            model.X += pushNormalAvg.x * pushDist;
            model.Z += pushNormalAvg.z * pushDist;

            ApplyRigidCollisionResponse(pushNormalAvg, sinY, cosY);
        }

        // 2. Physical Barrier Spline Envelope (Backup Hard Enclosing Boundary)
        // Only active when the vehicle is genuinely off-track in terrain/runoff and outside the pit corridor
        if (spline != null && !IsInPitCorridor(model.X, model.Z) && !isCarOverRoadOrKerb)
        {
            float carHalfW = 1.05f;
            float minSafeMargin = q.trackWidth * 0.5f + 1.2f;
            float maxLeft = Mathf.Max(minSafeMargin, q.barrierLeft - carHalfW);
            float maxRight = Mathf.Max(minSafeMargin, q.barrierRight - carHalfW);

            float nx = -Mathf.Cos(q.headingRad);
            float nz = Mathf.Sin(q.headingRad);

            if (q.lateralOffset > maxLeft)
            {
                float excess = q.lateralOffset - maxLeft;
                model.X -= nx * (excess + 0.05f);
                model.Z -= nz * (excess + 0.05f);

                if (model.Vy > 0f) model.Vy = -model.Vy * 0.15f;
                model.Vx *= 0.60f;
                OnBarrierCollision?.Invoke(excess * 5f + Mathf.Abs(model.Vy), -new Vector3(nx, 0f, nz));
            }
            else if (q.lateralOffset < -maxRight)
            {
                float excess = -maxRight - q.lateralOffset;
                model.X += nx * (excess + 0.05f);
                model.Z += nz * (excess + 0.05f);

                if (model.Vy < 0f) model.Vy = -model.Vy * 0.15f;
                model.Vx *= 0.60f;
                OnBarrierCollision?.Invoke(excess * 5f + Mathf.Abs(model.Vy), new Vector3(nx, 0f, nz));
            }
        }
    }

    void OnCollisionEnter(Collision col)
    {
        float mag = col.relativeVelocity.magnitude;
        Vector3 norm = col.contactCount > 0 ? col.contacts[0].normal : Vector3.up;
        OnBarrierCollision?.Invoke(mag, norm);
    }

    float GetWheelGroundHeight(float wx, float wz, float fallbackY, out bool isRoadOrKerb)
    {
        isRoadOrKerb = false;
        // 1. Raycast down from above the current vehicle position
        float startY = Mathf.Max(currPos.y, fallbackY) + 3.0f;
        Vector3 rayStart = new Vector3(wx, startY, wz);
        var hits = Physics.RaycastAll(rayStart, Vector3.down, 12.0f, ~0, QueryTriggerInteraction.Ignore);

        float bestRoadY = float.MinValue;
        float bestGroundY = float.MinValue;
        bool foundRoad = false;
        bool foundGround = false;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.transform.root == transform) continue;

            string colName = h.collider.gameObject.name;
            // Reject barriers, walls, fencing, and vertical surfaces
            if (colName.Contains("Barrier") || colName.Contains("wall") || colName.Contains("armco") ||
                colName.Contains("fence") || colName.Contains("guard") || colName.Contains("concw"))
            {
                continue;
            }
            if (h.normal.y < 0.35f) continue;

            if (colName.Contains("Road") || colName.Contains("Kerb"))
            {
                if (h.point.y > bestRoadY)
                {
                    bestRoadY = h.point.y;
                    foundRoad = true;
                }
            }
            else
            {
                if (h.point.y > bestGroundY)
                {
                    bestGroundY = h.point.y;
                    foundGround = true;
                }
            }
        }

        if (foundRoad)
        {
            isRoadOrKerb = true;
            return bestRoadY;
        }
        if (foundGround)
        {
            return bestGroundY;
        }

        // 2. High raycast fallback if car was slightly misaligned
        Vector3 highRayStart = new Vector3(wx, fallbackY + 8.0f, wz);
        var highHits = Physics.RaycastAll(highRayStart, Vector3.down, 16.0f, ~0, QueryTriggerInteraction.Ignore);
        bestRoadY = float.MinValue;
        bestGroundY = float.MinValue;
        foundRoad = false;
        foundGround = false;

        for (int i = 0; i < highHits.Length; i++)
        {
            var h = highHits[i];
            if (h.transform.root == transform) continue;

            string colName = h.collider.gameObject.name;
            if (colName.Contains("Barrier") || colName.Contains("wall") || colName.Contains("armco") ||
                colName.Contains("fence") || colName.Contains("guard") || colName.Contains("concw"))
            {
                continue;
            }
            if (h.normal.y < 0.35f) continue;

            if (colName.Contains("Road") || colName.Contains("Kerb"))
            {
                if (h.point.y > bestRoadY)
                {
                    bestRoadY = h.point.y;
                    foundRoad = true;
                }
            }
            else
            {
                if (h.point.y > bestGroundY)
                {
                    bestGroundY = h.point.y;
                    foundGround = true;
                }
            }
        }

        if (foundRoad)
        {
            isRoadOrKerb = true;
            return bestRoadY;
        }
        if (foundGround)
        {
            return bestGroundY;
        }

        return fallbackY;
    }

    float GetWheelGroundHeight(float wx, float wz, float fallbackY)
    {
        return GetWheelGroundHeight(wx, wz, fallbackY, out _);
    }

    public void ComputeGroundAlignment(float posX, float posZ, float yawRad, out float targetY, out float targetPitchDeg, out float targetRollDeg, out bool isRoadOrKerb)
    {
        float cosY = Mathf.Cos(yawRad), sinY = Mathf.Sin(yawRad);
        Vector2 fwd = new Vector2(sinY, cosY);
        Vector2 right = new Vector2(cosY, -sinY);
        Vector2 cPos = new Vector2(posX, posZ);

        float halfTwF = kVisualTrackWidthF * 0.5f;
        float halfTwR = kVisualTrackWidthR * 0.5f;

        // In Ferrari SF-26 prefab local space:
        // Wheel_FL is at local X = +0.799m (+right), Z = +1.522m
        // Wheel_FR is at local X = -0.800m (-right), Z = +1.522m
        // Wheel_RL is at local X = +0.763m (+right), Z = -2.011m
        // Wheel_RR is at local X = -0.763m (-right), Z = -2.011m
        Vector2 posFL = cPos + fwd * kVisualFrontAxleZ + right * halfTwF;
        Vector2 posFR = cPos + fwd * kVisualFrontAxleZ - right * halfTwF;
        Vector2 posRL = cPos + fwd * kVisualRearAxleZ + right * halfTwR;
        Vector2 posRR = cPos + fwd * kVisualRearAxleZ - right * halfTwR;

        float groundFL = 0f, groundFR = 0f, groundRL = 0f, groundRR = 0f;
        bool roadFL = false, roadFR = false, roadRL = false, roadRR = false;

        if (spline != null)
        {
            var qFL = spline.Query(posFL.x, posFL.y, carSplineIndex);
            var qFR = spline.Query(posFR.x, posFR.y, carSplineIndex);
            var qRL = spline.Query(posRL.x, posRL.y, carSplineIndex);
            var qRR = spline.Query(posRR.x, posRR.y, carSplineIndex);

            groundFL = GetWheelGroundHeight(posFL.x, posFL.y, qFL.heightY, out roadFL);
            groundFR = GetWheelGroundHeight(posFR.x, posFR.y, qFR.heightY, out roadFR);
            groundRL = GetWheelGroundHeight(posRL.x, posRL.y, qRL.heightY, out roadRL);
            groundRR = GetWheelGroundHeight(posRR.x, posRR.y, qRR.heightY, out roadRR);
        }

        lastGroundFL = groundFL;
        lastGroundFR = groundFR;
        lastGroundRL = groundRL;
        lastGroundRR = groundRR;

        isRoadOrKerb = roadFL || roadFR || roadRL || roadRR;

        float groundF = (groundFL + groundFR) * 0.5f;
        float groundR = (groundRL + groundRR) * 0.5f;
        float groundPosX = (groundFL + groundRL) * 0.5f;
        float groundNegX = (groundFR + groundRR) * 0.5f;

        // In Unity Euler angles around local X axis:
        // Positive rotation tilts nose DOWN, negative rotation tilts nose UP.
        // On an uphill slope (groundF > groundR), (groundR - groundF) is negative -> targetPitchDeg is negative -> tilts nose UP!
        targetPitchDeg = Mathf.Atan2(groundR - groundF, kVisualWheelbase) * Mathf.Rad2Deg;

        // In Unity Euler angles around local Z axis (roll):
        // Positive rotation around +Z tilts +X upward and -X downward.
        // When groundPosX > groundNegX (+X side is higher), targetRollDeg is positive -> tilts +X side up!
        targetRollDeg = Mathf.Atan2(groundPosX - groundNegX, (kVisualTrackWidthF + kVisualTrackWidthR) * 0.5f) * Mathf.Rad2Deg;

        targetPitchDeg = Mathf.Clamp(targetPitchDeg, -30f, 30f);
        targetRollDeg = Mathf.Clamp(targetRollDeg, -30f, 30f);

        // Anti-clipping envelope calculation:
        // Computes the required vehicle origin Y so that NO wheel ever penetrates the ground mesh
        float pitchRad = targetPitchDeg * Mathf.Deg2Rad;
        float rollRad = targetRollDeg * Mathf.Deg2Rad;
        float cosP = Mathf.Cos(pitchRad), sinP = Mathf.Sin(pitchRad);
        float cosR = Mathf.Cos(rollRad), sinR = Mathf.Sin(rollRad);

        // For each wheel (lx, ly, lz):
        // Local displacement under R_x(pitch) * R_z(roll): py = lx * sinR * cosP - lz * sinP
        // Bottom of tire in world space: Y_chassis + py - kVisualTireRadius
        // Clearance requirement: Y_chassis >= ground_i + kVisualTireRadius - py
        float reqFL = groundFL + kVisualTireRadius - (( halfTwF * sinR) * cosP - kVisualFrontAxleZ * sinP);
        float reqFR = groundFR + kVisualTireRadius - (((-halfTwF) * sinR) * cosP - kVisualFrontAxleZ * sinP);
        float reqRL = groundRL + kVisualTireRadius - (( halfTwR * sinR) * cosP - kVisualRearAxleZ * sinP);
        float reqRR = groundRR + kVisualTireRadius - (((-halfTwR) * sinR) * cosP - kVisualRearAxleZ * sinP);

        const float kContactBuffer = 0.012f; // 12mm contact cushion ensuring zero facet clipping
        targetY = Mathf.Max(Mathf.Max(reqFL, reqFR), Mathf.Max(reqRL, reqRR)) + kContactBuffer;
    }

    void Update()
    {
        if (isTrackDataMissing) return;

        // Frame rate stats EMA
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f)
        {
            float currentFps = 1f / dt;
            fpsAvg = fpsAvg + 0.05f * (currentFps - fpsAvg);
            frameTimeAvgMs = frameTimeAvgMs + 0.05f * (dt * 1000f - frameTimeAvgMs);
        }

        if (csvBannerTimer > 0f) csvBannerTimer -= dt;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.kKey.wasPressedThisFrame) SaveTelemetry();
            if (kb.rKey.wasPressedThisFrame) ResetCar();
            if (kb.tKey.wasPressedThisFrame) ResetToNearestTrack();
            if (kb.xKey.wasPressedThisFrame) ToggleAero();
            if (kb.pKey.wasPressedThisFrame || kb.lKey.wasPressedThisFrame) TogglePitLimiter();
            if (kb.leftBracketKey.wasPressedThisFrame)
                config.brakeBiasFront = Mathf.Clamp(config.brakeBiasFront - brakeBiasStep, 0.4f, 0.9f);
            if (kb.rightBracketKey.wasPressedThisFrame)
                config.brakeBiasFront = Mathf.Clamp(config.brakeBiasFront + brakeBiasStep, 0.4f, 0.9f);
        }

        // Render-time interpolation
        float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        transform.position = Vector3.Lerp(prevPos, currPos, t);
        transform.rotation = Quaternion.Slerp(prevRot, currRot, t);
    }

    public void ToggleAero()
    {
        model.Aero.CurrentMode = model.Aero.CurrentMode == AeroMode.Corner
            ? AeroMode.Straight
            : AeroMode.Corner;
    }

    int currentCornerTeleportIdx = 0;

    void OnGUI()
    {
        if (isTrackDataMissing)
        {
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var errStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.08f),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            errStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), "TRACK DATA MISSING", errStyle);
            return;
        }

        InitStyles();

        float sw = Screen.width;
        float sh = Screen.height;
        float s  = Mathf.Min(sw / 800f, sh / 480f);

        var qCarCurrent = spline != null ? spline.Query(model.X, model.Z) : default;

        // ── Main telemetry panel (top-left) ───────────────────────────────
        if (isDebugActive)
        {
            const float panelX = 8f;
            const float panelY = 8f;
            const float panelW = 390f;
            const float rowH   = 20f;
            const float padV   = 6f;

            string aeroStr  = model.Aero.CurrentMode == AeroMode.Corner ? "CRN" : "STR";
            string shiftStr = model.IsShifting ? " ⚡" : "";

            string[] rows =
            {
                $"<b>VEHICLE</b> {model.TotalSpeedKmh:0} km/h ({model.Vx:0.0} m/s)  LatG {model.LatG:+0.00;-0.00}g  Ax {model.Ax/9.81f:+0.00;-0.00}g  \u03b2 {model.BetaDeg:+0.0;-0.0}\u00b0",
                $"<b>INPUT</b> RawRoll {input.RawRollDeg:+0.0;-0.0}\u00b0  CalibTilt {input.RelDeg:+0.0;-0.0}\u00b0  NormIn {input.SteerTarget:+0.00;-0.00}  Out {input.Steer:+0.00;-0.00}",
                $"<b>STEER</b> Req \u03b4 {model.DriverSteerAngleDeg:+0.0;-0.0}\u00b0  FL {model.SteerFLDeg:+0.0;-0.0}\u00b0  FR {model.SteerFRDeg:+0.0;-0.0}\u00b0  Lim {model.MechanicalSteerLimitDeg:0.0}\u00b0  Asst {model.DeltaAssistDeg:+0.00;-0.00}\u00b0",
                $"<b>SLIP \u03b1</b> FL {model.SlipAngleFLDeg:+0.0;-0.0}\u00b0  FR {model.SlipAngleFRDeg:+0.0;-0.0}\u00b0  RL {model.SlipAngleRLDeg:+0.0;-0.0}\u00b0  RR {model.SlipAngleRRDeg:+0.0;-0.0}\u00b0",
                $"<b>FORCES</b> FyF {model.FyFrontTotal:0}N  FyR {model.FyRearTotal:0}N  FzF {model.FzFrontTotal:0}N  FzR {model.FzRearTotal:0}N",
                $"<b>GRIP/SAT</b> \u03bcF {model.MuFront:0.00}  \u03bcR {model.MuRear:0.00}  SatF {model.FrontTireSat*100f:0}%  SatR {model.RearTireSat*100f:0}%  Trac {model.RearTractionUse*100f:0}%",
                $"<b>POWERTRAIN</b> Gear {(model.Gear == -1 ? "R" : model.Gear.ToString())}  RPM {model.EngineRpm:0}{shiftStr}  Aero {aeroStr}  Thr {input.Throttle:0.00}  Brk {input.Brake:0.00}",
                $"<b>SYSTEM</b> FPS {fpsAvg:0} ({frameTimeAvgMs:0.1}ms)  Sim {avgStepMs:0.00}ms  CSV: {telemetry.Count} frames [K: Save]"
            };

            float panelH = rows.Length * rowH + 2f * padV;
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), GUIContent.none, boxStyle);
            for (int i = 0; i < rows.Length; i++)
            {
                GUI.Label(new Rect(panelX + 8f, panelY + padV + i * rowH, panelW - 16f, rowH), rows[i], labelStyle);
            }

            // Build Stamp Line (Debug Mode)
            if (stampStyle == null)
            {
                stampStyle = new GUIStyle(labelStyle) { fontSize = 10, fontStyle = FontStyle.Normal };
                stampStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 0.85f);
            }
            GUI.Label(new Rect(panelX + 4f, panelY + panelH + 2f, panelW + 200f, 16f), buildStampLine, stampStyle);

            // Track Visibility & Surface Info Line
            int visChunks = 0;
            for (int i = 0; i < trackChunkRenderers.Count; i++)
            {
                if (trackChunkRenderers[i] != null && trackChunkRenderers[i].isVisible)
                    visChunks++;
            }
            bool inPitHUD = IsInPitCorridor(model.X, model.Z);
            string surfName = inPitHUD ? "asphalt (pitlane)" : (isCarOverRoadOrKerb ? "asphalt" : qCarCurrent.surface.ToString().ToLowerInvariant());
            string trackStatusLine = $"s={qCarCurrent.s:F0} off={qCarCurrent.lateralOffset:F1} surf={surfName} chunks={visChunks}/{totalTrackChunks}";
            GUI.Label(new Rect(panelX + 4f, panelY + panelH + 18f, panelW + 200f, 16f), trackStatusLine, stampStyle);
        }
        else
        {
            // Discreet clean FPS badge in top left when not in debug mode
            if (stampStyle == null)
            {
                stampStyle = new GUIStyle(labelStyle) { fontSize = 10, fontStyle = FontStyle.Normal };
                stampStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 0.7f);
            }
            GUI.Label(new Rect(12f, 10f, 140f, 16f), $"FPS {fpsAvg:0}", stampStyle);
        }

        if (!string.IsNullOrEmpty(spawnOffsetError) && isDebugActive)
        {
            GUI.Label(new Rect(sw * 0.5f - 200f, 130f, 400f, 25f), $"<color=red><b>{spawnOffsetError}</b></color>", labelStyle);
        }

        // ── Minimap (Top Center) ──────────────────────────────────────────
        if (minimapTex != null)
        {
            float mapW = 160f;
            float mapH = 110f;
            float mapX = sw * 0.5f - mapW * 0.5f;
            float mapY = 10f;
            GUI.DrawTexture(new Rect(mapX, mapY, mapW, mapH), minimapTex);

            // Car marker dot
            Vector2 carMap = MinimapDrawer.GetNormalizedCarPosition(model.X, model.Z, trackConfig, (int)mapW, (int)mapH);
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(mapX + carMap.x - 3f, mapY + (mapH - carMap.y) - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ── Lap Timing Panel (Top Right) ──────────────────────────────────
        if (lapTimer != null)
        {
            float lapW = 220f;
            float lapH = 110f;
            float lapX = sw - lapW - 10f;
            float lapY = 10f;
            GUI.Box(new Rect(lapX, lapY, lapW, lapH), GUIContent.none, boxStyle);

            string lapTimeStr = lapTimer.HasLapStarted ? FormatTime(lapTimer.CurrentLapTime) : "--:--.---";
            string deltaStr = (lapTimer.HasLapStarted && lapTimer.HasBestLap) ? $"{lapTimer.GetLiveDelta(qCarCurrent.s):+0.00;-0.00}" : "--";
            GUI.Label(new Rect(lapX + 8f, lapY + 5f, lapW, 20f), $"<b>LAP TIME</b> {lapTimeStr} ({deltaStr})", labelStyle);
            GUI.Label(new Rect(lapX + 8f, lapY + 25f, lapW, 20f), $"LAST: {FormatTime(lapTimer.LastLapTime)}  BEST: {FormatTime(lapTimer.BestLapTime)}", labelStyle);

            string s1Str = (lapTimer.HasLapStarted && lapTimer.CurrentS1Time > 0f) ? $"{lapTimer.CurrentS1Time:F2}s" : "--";
            string s2Str = (lapTimer.HasLapStarted && lapTimer.CurrentS2Time > 0f) ? $"{lapTimer.CurrentS2Time:F2}s" : "--";
            string s3Str = (lapTimer.HasLapStarted && lapTimer.CurrentS3Time > 0f) ? $"{lapTimer.CurrentS3Time:F2}s" : "--";
            GUI.Label(new Rect(lapX + 8f, lapY + 45f, lapW, 20f), $"S1: {s1Str}  S2: {s2Str}  S3: {s3Str}", labelStyle);

            if (lapTimer.HasLapStarted && !lapTimer.IsLapValid)
                GUI.Label(new Rect(lapX + 8f, lapY + 68f, lapW, 20f), "<color=red><b>LAP INVALID</b></color>", labelStyle);
            if (lapTimer.IsWrongWay)
                GUI.Label(new Rect(lapX + 8f, lapY + 88f, lapW, 20f), "<color=orange><b>WRONG WAY!</b></color>", labelStyle);
        }

        // ── Digital Speedometer & Cockpit Cluster (Bottom Center) ───────
        DrawBottomSpeedometerCluster(sw, sh);

        // ── Top-Right Modern Action Grid (under Lap Timer) ───────────────
        float btnGridW = 220f;
        float btnGridX = sw - btnGridW - 10f;
        float bW = 106f;
        float bH = 32f;
        float bGap = 8f;
        float r1Y = (lapTimer != null) ? 126f : 10f;
        float r2Y = r1Y + bH + bGap;

        // Row 1 Left: Camera Mode
        string camSub = "CHASE";
        if (chaseCam == null)
        {
            chaseCam = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : FindAnyObjectByType<ChaseCamera>();
        }
        if (chaseCam != null)
        {
            camSub = chaseCam.currentMode switch
            {
                ChaseCamera.CameraMode.Chase => "CHASE",
                ChaseCamera.CameraMode.Bonnet => "BONNET",
                ChaseCamera.CameraMode.TCam => "T-CAM",
                _ => "CAM"
            };
        }
        Rect rectCam = new Rect(btnGridX, r1Y, bW, bH);
        CarInput.DrawModernButton(rectCam, "📷 " + camSub, new Color(0.3f, 0.8f, 1f), false);
        if (GUI.Button(rectCam, GUIContent.none, GUIStyle.none))
        {
            if (chaseCam != null) chaseCam.ToggleCameraMode();
        }

        // Row 1 Right: Auto Lap Mode
        Rect rectAuto = new Rect(btnGridX + bW + bGap, r1Y, bW, bH);
        Color autoColor = isAutoLapActive ? new Color(1f, 0.8f, 0.1f) : new Color(0.4f, 0.85f, 0.5f);
        CarInput.DrawModernButton(rectAuto, isAutoLapActive ? "AUTO LAP" : "MANUAL", autoColor, isAutoLapActive);
        if (GUI.Button(rectAuto, GUIContent.none, GUIStyle.none))
        {
            isAutoLapActive = !isAutoLapActive;
        }

        // Row 2 Left: Reset Car
        Rect rectReset = new Rect(btnGridX, r2Y, bW, bH);
        CarInput.DrawModernButton(rectReset, "🔄 RESET", new Color(1f, 0.45f, 0.45f), false);
        if (GUI.Button(rectReset, GUIContent.none, GUIStyle.none))
        {
            ResetCar();
        }

        // Row 2 Right: Telemetry / Debug Mode
        Rect rectDbg = new Rect(btnGridX + bW + bGap, r2Y, bW, bH);
        Color dbgColor = isDebugActive ? new Color(0.3f, 1f, 0.4f) : new Color(0.6f, 0.65f, 0.75f);
        CarInput.DrawModernButton(rectDbg, isDebugActive ? "DEBUG ON" : "DEBUG", dbgColor, isDebugActive);
        if (GUI.Button(rectDbg, GUIContent.none, GUIStyle.none))
        {
            isDebugActive = !isDebugActive;
            if (debugVisualRoot != null) debugVisualRoot.SetActive(isDebugActive);
        }

        // Row 3: Teleport Prev/Next (Shown only when isDebugActive)
        if (isDebugActive)
        {
            float r3Y = r2Y + bH + bGap;
            Rect rectTpPrev = new Rect(btnGridX, r3Y, bW, 28f);
            Rect rectTpNext = new Rect(btnGridX + bW + bGap, r3Y, bW, 28f);
            CarInput.DrawModernButton(rectTpPrev, "⏮ TP PREV", new Color(0.6f, 0.7f, 0.9f), false);
            if (GUI.Button(rectTpPrev, GUIContent.none, GUIStyle.none))
            {
                currentCornerTeleportIdx--;
                TeleportToCorner(currentCornerTeleportIdx);
            }
            CarInput.DrawModernButton(rectTpNext, "⏭ TP NEXT", new Color(0.6f, 0.7f, 0.9f), false);
            if (GUI.Button(rectTpNext, GUIContent.none, GUIStyle.none))
            {
                currentCornerTeleportIdx++;
                TeleportToCorner(currentCornerTeleportIdx);
            }

            // Row 4: CSV Telemetry Export
            float r4Y = r3Y + 34f;
            Rect rectCsv = new Rect(btnGridX, r4Y, btnGridW, 28f);
            CarInput.DrawModernButton(rectCsv, "💾 CSV EXPORT [K]", new Color(0.2f, 0.9f, 0.5f), false);
            if (GUI.Button(rectCsv, GUIContent.none, GUIStyle.none))
            {
                SaveTelemetry();
            }
        }

        // CSV Save Banner
        if (csvBannerTimer > 0f)
        {
            GUI.Box(new Rect(sw * 0.5f - 240f, 35f, 480f, 32f), GUIContent.none, boxStyle);
            GUI.Label(new Rect(sw * 0.5f - 230f, 40f, 460f, 24f),
                $"<color=#00FF88><b>TELEMETRY SAVED ({telemetry.Count} frames) \u2192 {System.IO.Path.GetFileName(lastSavePath)}</b></color>", labelStyle);
        }

        // Banner
        if (resetBannerTimer > 0f)
        {
            resetBannerTimer -= Time.deltaTime;
            GUI.Label(new Rect(sw * 0.5f - 150f, sh * 0.35f, 300f, 40f), $"PARAMS RESET v{CarParams.CURRENT_VERSION}", bannerStyle);
        }
    }

    static string FormatTime(float t)
    {
        if (t <= 0f) return "--:--.---";
        int min = (int)(t / 60f);
        float sec = t - min * 60f;
        if (sec >= 59.9995f)
        {
            min++;
            sec = 0f;
        }
        return $"{min}:{sec:00.000}";
    }

    void SetupDebugVisuals()
    {
        if (trackConfig == null || trackConfig.samples == null || trackConfig.samples.Count == 0) return;
        debugVisualRoot = new GameObject("Track_DebugVisuals");

        var lrObj = new GameObject("Centerline_Line");
        lrObj.transform.SetParent(debugVisualRoot.transform);
        debugLine = lrObj.AddComponent<LineRenderer>();
        debugLine.useWorldSpace = true;
        debugLine.startWidth = 0.3f;
        debugLine.endWidth = 0.3f;
        debugLine.loop = true;
        debugLine.positionCount = trackConfig.samples.Count;
        for (int i = 0; i < trackConfig.samples.Count; i++)
        {
            var smp = trackConfig.samples[i];
            debugLine.SetPosition(i, new Vector3(smp.x, smp.y + 0.3f, smp.z));
        }

        var unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Mobile/Diffuse");
        var lineMat = new Material(unlitShader) { color = Color.yellow };
        debugLine.material = lineMat;

        // Tall coloured poles: S/F (white), S1 (cyan), S2 (magenta)
        if (lapTimer != null && spline != null)
        {
            var qSF = spline.QueryAtS(lapTimer.StartFinishS);
            CreateDebugPole("Pole_SF", qSF.x, qSF.heightY, qSF.z, Color.white, lineMat);

            var qS1 = spline.QueryAtS(lapTimer.Sector1SplitS);
            CreateDebugPole("Pole_S1", qS1.x, qS1.heightY, qS1.z, Color.cyan, lineMat);

            var qS2 = spline.QueryAtS(lapTimer.Sector2SplitS);
            CreateDebugPole("Pole_S2", qS2.x, qS2.heightY, qS2.z, Color.magenta, lineMat);
        }
        else
        {
            var s0 = trackConfig.samples[0];
            CreateDebugPole("Pole_SF", s0.x, s0.y, s0.z, Color.white, lineMat);
        }

        debugVisualRoot.SetActive(isDebugActive);
    }

    void CreateDebugPole(string name, float x, float y, float z, Color color, Material baseMat)
    {
        var poleObj = new GameObject(name);
        poleObj.transform.SetParent(debugVisualRoot.transform);
        var lr = poleObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.startWidth = 0.5f;
        lr.endWidth = 0.5f;
        lr.positionCount = 2;
        lr.SetPosition(0, new Vector3(x, y, z));
        lr.SetPosition(1, new Vector3(x, y + 15f, z));
        var poleMat = new Material(baseMat) { color = color };
        lr.material = poleMat;
    }

    void InitStyles()
    {
        if (stylesReady) return;
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
        labelStyle.normal.textColor = Color.white;

        boxStyle = new GUIStyle(GUI.skin.box);
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.7f));
        tex.Apply();
        boxStyle.normal.background = tex;

        shiftStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        shiftStyle.normal.textColor = Color.red;

        bannerStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        bannerStyle.normal.textColor = Color.yellow;

        speedNumStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 38,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight,
            richText = true
        };
        speedNumStyle.normal.textColor = Color.white;

        speedUnitStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.LowerLeft,
            richText = true
        };
        speedUnitStyle.normal.textColor = new Color(0.2f, 0.85f, 1f, 0.95f);

        gearStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 38,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            richText = true
        };
        gearStyle.normal.textColor = Color.white;

        hudSubLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            richText = true
        };
        hudSubLabelStyle.normal.textColor = new Color(0.7f, 0.75f, 0.8f, 0.85f);

        ledGreenTex = MakeColorTex(new Color(0.1f, 0.95f, 0.2f, 1f));
        ledRedTex = MakeColorTex(new Color(0.95f, 0.15f, 0.15f, 1f));
        ledBlueTex = MakeColorTex(new Color(0.2f, 0.45f, 1f, 1f));
        ledOffTex = MakeColorTex(new Color(0.16f, 0.18f, 0.22f, 0.55f));
        ledShiftTex = MakeColorTex(new Color(0.95f, 0.35f, 1f, 1f));
        thrBarTex = MakeColorTex(new Color(0.1f, 0.95f, 0.3f, 0.95f));
        brkBarTex = MakeColorTex(new Color(0.95f, 0.2f, 0.2f, 0.95f));
        barBgTex = MakeColorTex(new Color(0.2f, 0.22f, 0.26f, 0.75f));
        clusterBgTex = MakeColorTex(new Color(0.04f, 0.05f, 0.07f, 0.88f));

        stylesReady = true;
    }

    Texture2D MakeColorTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void DrawBottomSpeedometerCluster(float sw, float sh)
    {
        float clusterW = 280f;
        float clusterH = 88f;
        float clusterX = (sw - clusterW) * 0.5f;
        float clusterY = sh - clusterH - 10f;

        // Background box
        if (clusterBgTex != null)
        {
            GUI.DrawTexture(new Rect(clusterX, clusterY, clusterW, clusterH), clusterBgTex);
        }
        else
        {
            GUI.Box(new Rect(clusterX, clusterY, clusterW, clusterH), GUIContent.none, boxStyle);
        }

        // 1. Shift Light LEDs (15 segments)
        // Green: 5 LEDs (6000 - 9500)
        // Red: 5 LEDs (9500 - 13000)
        // Blue: 5 LEDs (13000 - 14500)
        // Shift Flash: >= 14200 or model.IsShifting
        float rpm = model.EngineRpm;
        bool isShiftFlashing = (rpm >= 14200f || model.IsShifting) && (Mathf.Repeat(Time.unscaledTime * 14f, 1f) > 0.4f);

        int totalLeds = 15;
        float ledGap = 3f;
        float totalLedSpan = 252f;
        float ledW = (totalLedSpan - (totalLeds - 1) * ledGap) / totalLeds; // ~14f
        float ledH = 7f;
        float ledStartX = clusterX + (clusterW - totalLedSpan) * 0.5f;
        float ledY = clusterY + 7f;

        for (int i = 0; i < totalLeds; i++)
        {
            float x = ledStartX + i * (ledW + ledGap);
            Rect ledRect = new Rect(x, ledY, ledW, ledH);

            if (isShiftFlashing)
            {
                GUI.DrawTexture(ledRect, ledShiftTex);
                continue;
            }

            float ledRpmThreshold;
            if (i < 5) // Green (6000 to 9500)
                ledRpmThreshold = 6000f + i * (3500f / 5f);
            else if (i < 10) // Red (9500 to 13000)
                ledRpmThreshold = 9500f + (i - 5) * (3500f / 5f);
            else // Blue (13000 to 14500)
                ledRpmThreshold = 13000f + (i - 10) * (1500f / 5f);

            bool isLit = rpm >= ledRpmThreshold;
            Texture2D texToDraw;
            if (!isLit)
            {
                texToDraw = ledOffTex;
            }
            else if (i < 5)
            {
                texToDraw = ledGreenTex;
            }
            else if (i < 10)
            {
                texToDraw = ledRedTex;
            }
            else
            {
                texToDraw = ledBlueTex;
            }

            GUI.DrawTexture(ledRect, texToDraw);
        }

        // 2. Gear Display (Left)
        float gearX = clusterX + 14f;
        float gearY = clusterY + 18f;
        GUI.Label(new Rect(gearX, gearY, 44f, 14f), "GEAR", hudSubLabelStyle);
        string gearStr = model.Gear == -1 ? "R" : (model.Gear == 0 ? "N" : model.Gear.ToString());
        Color prevColor = gearStyle.normal.textColor;
        if (model.Gear == -1) gearStyle.normal.textColor = new Color(1f, 0.3f, 0.3f);
        else if (model.Gear == 0) gearStyle.normal.textColor = new Color(0.3f, 1f, 0.4f);
        else gearStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(gearX, gearY + 12f, 44f, 46f), gearStr, gearStyle);
        gearStyle.normal.textColor = prevColor;

        // Separator line
        GUI.DrawTexture(new Rect(gearX + 50f, gearY + 8f, 1f, 50f), barBgTex);

        // 3. Digital Speedometer (Center)
        float speedX = gearX + 56f;
        float speedY = clusterY + 16f;
        int speedVal = Mathf.RoundToInt(model.TotalSpeedKmh);
        GUI.Label(new Rect(speedX, speedY + 8f, 78f, 44f), speedVal.ToString(), speedNumStyle);
        GUI.Label(new Rect(speedX + 82f, speedY + 26f, 38f, 18f), "KM/H", speedUnitStyle);

        // RPM readout under speed
        GUI.Label(new Rect(speedX, speedY + 46f, 115f, 16f), $"RPM {rpm:0}", hudSubLabelStyle);

        // Separator line
        GUI.DrawTexture(new Rect(speedX + 125f, gearY + 8f, 1f, 50f), barBgTex);

        // 4. Pedal Bars & DRS / Aero (Right)
        float rightX = speedX + 132f;
        float rightY = clusterY + 19f;

        // Throttle & Brake horizontal mini bars
        float barW = 68f;
        float barH = 7f;

        // Throttle
        GUI.DrawTexture(new Rect(rightX, rightY + 3f, barW, barH), barBgTex);
        float thrFill = Mathf.Clamp01(input.Throttle);
        if (thrFill > 0.01f)
            GUI.DrawTexture(new Rect(rightX, rightY + 3f, barW * thrFill, barH), thrBarTex);

        // Brake
        GUI.DrawTexture(new Rect(rightX, rightY + 14f, barW, barH), barBgTex);
        float brkFill = Mathf.Clamp01(input.Brake);
        if (brkFill > 0.01f)
            GUI.DrawTexture(new Rect(rightX, rightY + 14f, barW * brkFill, barH), brkBarTex);

        // DRS / Aero & Pit Limiter Badge
        bool isStraight = model.Aero.CurrentMode == AeroMode.Straight;
        string aeroDrsText = isStraight ? "<color=#00FF66><b>[ DRS ]</b></color>" : "<color=#888888>[ CRN ]</color>";
        string badgeText = isPitLimiterActive ? "<color=#FFD600><b>[ PIT 80 ]</b></color>" : aeroDrsText;
        string latGText = $"{model.LatG:+0.0;-0.0}G";
        GUI.Label(new Rect(rightX, rightY + 26f, barW, 16f), badgeText, hudSubLabelStyle);
        GUI.Label(new Rect(rightX, rightY + 42f, barW, 16f), latGText, hudSubLabelStyle);
    }
}
