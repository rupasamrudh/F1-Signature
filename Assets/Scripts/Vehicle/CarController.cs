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
    public float LatG     => model != null ? model.LatG : 0f;
    public float YawRate  => model != null ? model.YawRate : 0f;
    public float SteerAngleDeg => model != null ? model.FinalSteerAngleDeg : 0f;
    public float Throttle => input != null ? input.Throttle : 0f;
    public float Brake => input != null ? input.Brake : 0f;
    public float EngineRpm => model != null ? model.EngineRpm : 1000f;
    public int Gear => model != null ? model.Gear : 1;
    public bool IsShifting => model != null ? model.IsShifting : false;
    public float MaxTireSlip => model != null ? model.MaxTireSlip : 0f;

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

    // ── Persistent Spline & Suspension Tracking ────────────────────────────
    int   carSplineIndex = 0;
    float smoothGroundY;
    float smoothPitchDeg;
    float smoothRollDeg;
    bool  isGroundInitialized = false;

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

    public void ShowResetBanner(float duration = 5.0f)
    {
        resetBannerTimer = duration;
    }

    void Awake()
    {
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
            startPos = new Vector3(sp.x, sp.y + config.tireRadius, sp.z);
            startYaw = sp.yaw_deg * Mathf.Deg2Rad;
            startRot = Quaternion.Euler(0f, sp.yaw_deg, 0f);
        }
        else
        {
            startPos = transform.position;
            startRot = transform.rotation;
            startYaw = transform.eulerAngles.y * Mathf.Deg2Rad;
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
        smoothGroundY = startPos.y;
        smoothPitchDeg = 0f;
        smoothRollDeg = 0f;
        isGroundInitialized = true;
        carSplineIndex = 0;

        // Immediately align chase camera behind car
        var chaseCam = FindAnyObjectByType<ChaseCamera>();
        if (chaseCam != null) chaseCam.SnapToTarget();

        // Assert: car lateral offset at start must be under 3 m
        if (spline != null)
        {
            var startQ = spline.Query(startPos.x, startPos.z);
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
        model.Reset(startPos.x, startPos.z, startYaw);
        currPos = prevPos = startPos;
        currRot = prevRot = startRot;
        currYaw = prevYaw = startYaw;
        smoothGroundY = startPos.y;
        smoothPitchDeg = 0f;
        smoothRollDeg = 0f;
        isGroundInitialized = true;
        carSplineIndex = 0;
        transform.position = startPos;
        transform.rotation = startRot;
        autoResetTimer = 0f;
        offTrackTimer = 0f;
        avgStepMs = 0f;
        isGroundInitialized = false;
        carSplineIndex = 0;
        if (lapTimer != null) lapTimer.Reset();
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
        float groundY = GetWheelGroundHeight(q.x, q.z, q.y);
        currPos = prevPos = new Vector3(q.x, groundY + config.tireRadius, q.z);
        currYaw = prevYaw = yawRad;
        isGroundInitialized = false;
        transform.position = currPos;
        transform.rotation = Quaternion.Euler(0f, yawRad * Mathf.Rad2Deg, 0f);
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
        float groundY = GetWheelGroundHeight(q.x, q.z, q.y);
        currPos = prevPos = new Vector3(q.x, groundY + config.tireRadius, q.z);
        currYaw = prevYaw = q.headingRad;
        isGroundInitialized = false;
        transform.position = currPos;
        transform.rotation = Quaternion.Euler(0f, q.headingRad * Mathf.Rad2Deg, 0f);
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

            // Compute wheel world positions
            float cosY = Mathf.Cos(model.Yaw), sinY = Mathf.Sin(model.Yaw);
            float L = config.wheelbase;
            float a = L * (1f - config.frontWeightFrac);
            float b = L * config.frontWeightFrac;
            float twF = config.trackWidthF * 0.5f;
            float twR = config.trackWidthR * 0.5f;

            // Forward: (sinY, cosY), Right: (cosY, -sinY)
            Vector2 fwd = new Vector2(sinY, cosY);
            Vector2 right = new Vector2(cosY, -sinY);

            Vector2 cPos = new Vector2(model.X, model.Z);
            Vector2 posFL = cPos + fwd * a - right * twF;
            Vector2 posFR = cPos + fwd * a + right * twF;
            Vector2 posRL = cPos - fwd * b - right * twR;
            Vector2 posRR = cPos - fwd * b + right * twR;

            var qFL = spline.Query(posFL.x, posFL.y, carSplineIndex);
            var qFR = spline.Query(posFR.x, posFR.y, carSplineIndex);
            var qRL = spline.Query(posRL.x, posRL.y, carSplineIndex);
            var qRR = spline.Query(posRR.x, posRR.y, carSplineIndex);

            // Set surface grip & rolling drag
            (model.surfaceGripFL, model.surfaceDragFL) = GetSurfaceProps(qFL.surface);
            (model.surfaceGripFR, model.surfaceDragFR) = GetSurfaceProps(qFR.surface);
            (model.surfaceGripRL, model.surfaceDragRL) = GetSurfaceProps(qRL.surface);
            (model.surfaceGripRR, model.surfaceDragRR) = GetSurfaceProps(qRR.surface);

            allWheelsOff = (qFL.surface != SurfaceType.Asphalt) && (qFR.surface != SurfaceType.Asphalt) &&
                           (qRL.surface != SurfaceType.Asphalt) && (qRR.surface != SurfaceType.Asphalt);

            // ── Physical Barrier Collision Resolution (Dual 3D Mesh + Envelope) ───
            ResolveBarrierCollisions(qCar);

            // Real 3D Physical Ground Height from Track Mesh Colliders for each wheel
            float groundFL = GetWheelGroundHeight(posFL.x, posFL.y, qFL.heightY);
            float groundFR = GetWheelGroundHeight(posFR.x, posFR.y, qFR.heightY);
            float groundRL = GetWheelGroundHeight(posRL.x, posRL.y, qRL.heightY);
            float groundRR = GetWheelGroundHeight(posRR.x, posRR.y, qRR.heightY);

            float avgWheelY = (groundFL + groundFR + groundRL + groundRR) * 0.25f;
            float targetPitchDeg = Mathf.Atan2((groundRL + groundRR) * 0.5f - (groundFL + groundFR) * 0.5f, L) * Mathf.Rad2Deg;
            float targetRollDeg = Mathf.Atan2((groundFR + groundRR) * 0.5f - (groundFL + groundRL) * 0.5f, twF + twR) * Mathf.Rad2Deg;

            targetPitchDeg = Mathf.Clamp(targetPitchDeg, -30f, 30f);
            targetRollDeg = Mathf.Clamp(targetRollDeg, -30f, 30f);

            float targetY = avgWheelY + config.tireRadius;
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
                // F1-grade critical suspension damping:
                // Fast 35 rad/s rate settles in ~30ms, completely eliminating vibration, jitter, and bouncing
                smoothGroundY = Mathf.Lerp(smoothGroundY, targetY, 1f - Mathf.Exp(-35f * dt));
                smoothPitchDeg = Mathf.LerpAngle(smoothPitchDeg, targetPitchDeg, 1f - Mathf.Exp(-30f * dt));
                smoothRollDeg = Mathf.LerpAngle(smoothRollDeg, targetRollDeg, 1f - Mathf.Exp(-30f * dt));
            }

            currPos = new Vector3(model.X, smoothGroundY, model.Z);
            currRot = Quaternion.Euler(smoothPitchDeg, model.Yaw * Mathf.Rad2Deg, smoothRollDeg);
        }
        else
        {
            currPos = new Vector3(model.X, startPos.y, model.Z);
            currRot = Quaternion.Euler(0f, model.Yaw * Mathf.Rad2Deg, 0f);
        }

        stepTimer.Restart();
        model.Step(Time.fixedDeltaTime, steerCmd, thrCmd, brkCmd);
        stepTimer.Stop();

        // Enforce physical barriers immediately after vehicle motion step
        if (spline != null)
        {
            var qAfter = spline.Query(model.X, model.Z, carSplineIndex);
            ResolveBarrierCollisions(qAfter);
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

        telemetry.Record(Time.time, model, thrCmd, brkCmd);

        currYaw = model.Yaw;

        // Auto-reset check: only if car somehow goes > 6m outside the physical barrier
        if (spline != null)
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

    // ── Physical Barrier Collision Resolution (Dual 3D Mesh + Envelope) ───
    void ResolveBarrierCollisions(TrackQueryResult q)
    {
        float carYaw = model.Yaw;
        float cosY = Mathf.Cos(carYaw);
        float sinY = Mathf.Sin(carYaw);

        Vector3 carCenter = new Vector3(model.X, smoothGroundY + 0.45f, model.Z);

        // 1. Physical 3D MeshCollider Raycast Feelers against Track_Barriers & Track_Trackside
        (float angle, float radius)[] feelers = new (float, float)[]
        {
            (0f, 2.7f),     // Nose
            (25f, 2.4f),    // Front-Right corner
            (50f, 1.8f),    // Right front wing
            (90f, 1.15f),   // Right sidepod / wheels
            (130f, 1.8f),   // Right rear corner
            (155f, 2.3f),   // Rear-Right wing
            (180f, 2.5f),   // Tail / diffuser
            (205f, 2.3f),   // Rear-Left wing
            (230f, 1.8f),   // Left rear corner
            (270f, 1.15f),  // Left sidepod / wheels
            (310f, 1.8f),   // Left front wing
            (335f, 2.4f)    // Front-Left corner
        };

        bool had3DHit = false;
        Vector3 totalPush = Vector3.zero;
        Vector3 hitNormalAvg = Vector3.zero;
        int hitCount = 0;

        foreach (var (angle, radius) in feelers)
        {
            float rad = carYaw + angle * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

            RaycastHit[] hits = Physics.RaycastAll(carCenter, dir, radius + 0.35f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.transform.root == transform) continue;
                string colName = h.collider.gameObject.name.ToLowerInvariant();
                if (colName.Contains("barrier") || colName.Contains("trackside") || colName.Contains("wall") || colName.Contains("fence") || colName.Contains("armco"))
                {
                    if (h.distance < radius)
                    {
                        float penetration = radius - h.distance;
                        Vector3 n = new Vector3(h.normal.x, 0f, h.normal.z);
                        if (n.sqrMagnitude < 0.01f) n = -dir;
                        else n.Normalize();

                        totalPush += n * (penetration + 0.08f);
                        hitNormalAvg += n;
                        hitCount++;
                        had3DHit = true;
                    }
                }
            }
        }

        if (had3DHit && hitCount > 0)
        {
            hitNormalAvg.Normalize();
            model.X += totalPush.x / hitCount;
            model.Z += totalPush.z / hitCount;

            // Reflect world velocity against physical barrier surface normal
            float wVx = model.Vx * sinY - model.Vy * cosY;
            float wVz = model.Vx * cosY + model.Vy * sinY;
            float vn = wVx * hitNormalAvg.x + wVz * hitNormalAvg.z;

            if (vn < 0f)
            {
                OnBarrierCollision?.Invoke(Mathf.Abs(vn), hitNormalAvg);
                float vtX = wVx - vn * hitNormalAvg.x;
                float vtZ = wVz - vn * hitNormalAvg.z;

                // Restitution 0.15 + tangential friction dissipation
                float newWvx = vtX * 0.70f - hitNormalAvg.x * (vn * 0.15f);
                float newWvz = vtZ * 0.70f - hitNormalAvg.z * (vn * 0.15f);

                model.Vx = Mathf.Max(0f, newWvx * sinY + newWvz * cosY);
                model.Vy = -newWvx * cosY + newWvz * sinY;
            }
        }

        // 2. Physical Barrier Spline Envelope from 3D model
        float carHalfW = 1.05f;
        float maxLeft = q.barrierLeft - carHalfW;
        float maxRight = q.barrierRight - carHalfW;

        float nx = -Mathf.Cos(q.headingRad);
        float nz = Mathf.Sin(q.headingRad);

        if (q.lateralOffset > maxLeft)
        {
            float excess = q.lateralOffset - maxLeft;
            OnBarrierCollision?.Invoke(Mathf.Abs(model.Vy) + excess * 3f, -new Vector3(nx, 0f, nz));
            model.X -= nx * (excess + 0.08f);
            model.Z -= nz * (excess + 0.08f);

            model.Vy = -Mathf.Abs(model.Vy) * 0.15f;
            model.Vx *= 0.70f;
        }
        else if (q.lateralOffset < -maxRight)
        {
            float excess = -maxRight - q.lateralOffset;
            OnBarrierCollision?.Invoke(Mathf.Abs(model.Vy) + excess * 3f, new Vector3(nx, 0f, nz));
            model.X += nx * (excess + 0.08f);
            model.Z += nz * (excess + 0.08f);

            model.Vy = Mathf.Abs(model.Vy) * 0.15f;
            model.Vx *= 0.70f;
        }
    }

    void OnCollisionEnter(Collision col)
    {
        float mag = col.relativeVelocity.magnitude;
        Vector3 norm = col.contactCount > 0 ? col.contacts[0].normal : Vector3.up;
        OnBarrierCollision?.Invoke(mag, norm);
    }

    float GetWheelGroundHeight(float wx, float wz, float fallbackY)
    {
        // 1. Raycast down from above the current vehicle position
        Vector3 rayStart = new Vector3(wx, currPos.y + 2.0f, wz);
        var hits = Physics.RaycastAll(rayStart, Vector3.down, 5.0f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.transform.root != transform)
            {
                return h.point.y;
            }
        }

        // 2. High raycast fallback if car was slightly misaligned
        Vector3 highRayStart = new Vector3(wx, fallbackY + 5.0f, wz);
        var highHits = Physics.RaycastAll(highRayStart, Vector3.down, 12.0f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in highHits)
        {
            if (h.transform.root != transform)
            {
                return h.point.y;
            }
        }

        return fallbackY;
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

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.rKey.wasPressedThisFrame) ResetCar();
            if (kb.tKey.wasPressedThisFrame) ResetToNearestTrack();
            if (kb.xKey.wasPressedThisFrame) ToggleAero();
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

        // ── Main telemetry panel (top-left) ───────────────────────────────
        const float panelX = 8f;
        const float panelY = 8f;
        const float panelW = 340f;
        const float rowH   = 22f;
        const float padV   = 6f;

        string aeroStr  = model.Aero.CurrentMode == AeroMode.Corner ? "CRN" : "STR";
        string shiftStr = model.IsShifting ? " ⚡" : "";
        string assistStr = config.stabilityAssist > 0f ? "ON" : "OFF";

        string[] rows =
        {
            $"<b>Speed</b> {model.TotalSpeedKmh:0} km/h   Vx {model.VxKmh:0} km/h",
            $"Lat G {model.LatG:0.00}   beta {model.BetaDeg:0.0}°",
            $"Steer {input.Steer:0.00}  Thr {input.Throttle:0.00}  Brk {input.Brake:0.00}",
            $"rear trac {model.RearTractionUse:0.00}  lat scale {model.RearLateralGripScale:0.00}",
            $"<b>Gear {(model.Gear == -1 ? "R" : model.Gear.ToString())}</b>  RPM {model.EngineRpm:0}{shiftStr}  Aero {aeroStr}",
            $"FPS {fpsAvg:0} ({frameTimeAvgMs:0.1}ms)  Sim {avgStepMs:0.00}ms",
            $"Brake bias {config.brakeBiasFront:0.00}  PkDecel {model.lastBrakingPeakDecelG:0.00}g"
        };

        float panelH = rows.Length * rowH + 2f * padV;
        GUI.Box(new Rect(panelX, panelY, panelW, panelH), GUIContent.none, boxStyle);
        for (int i = 0; i < rows.Length; i++)
        {
            GUI.Label(new Rect(panelX + 8f, panelY + padV + i * rowH, panelW - 16f, rowH), rows[i], labelStyle);
        }

        // ── Build Stamp Line (Always Visible) ─────────────────────────────
        if (stampStyle == null)
        {
            stampStyle = new GUIStyle(labelStyle) { fontSize = 10, fontStyle = FontStyle.Normal };
            stampStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 0.85f);
        }
        GUI.Label(new Rect(panelX + 4f, panelY + panelH + 2f, panelW + 200f, 16f), buildStampLine, stampStyle);

        // ── Track Visibility & Surface Info Line ──────────────────────────
        int visChunks = 0;
        for (int i = 0; i < trackChunkRenderers.Count; i++)
        {
            if (trackChunkRenderers[i] != null && trackChunkRenderers[i].isVisible)
                visChunks++;
        }
        var qCarCurrent = spline != null ? spline.Query(model.X, model.Z) : default;
        string surfName = qCarCurrent.surface.ToString().ToLowerInvariant();
        string trackStatusLine = $"s={qCarCurrent.s:F0} off={qCarCurrent.lateralOffset:F1} surf={surfName} chunks={visChunks}/{totalTrackChunks}";
        GUI.Label(new Rect(panelX + 4f, panelY + panelH + 18f, panelW + 200f, 16f), trackStatusLine, stampStyle);

        if (!string.IsNullOrEmpty(spawnOffsetError))
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

        // ── Top-Right Control Buttons (under Lap Timer) ───────────────────
        float row1Y = (lapTimer != null) ? 126f : 10f;
        float row2Y = row1Y + 36f;

        // Row 1: Mode Toggles (Auto Lap & Camera Mode)
        // Auto Lap toggle (Left)
        if (GUI.Button(new Rect(sw - 230f, row1Y, 105f, 32f), isAutoLapActive ? "Manual" : "Auto Lap"))
        {
            isAutoLapActive = !isAutoLapActive;
        }

        // Camera Toggle (Right)
        if (chaseCam == null)
        {
            chaseCam = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : FindAnyObjectByType<ChaseCamera>();
        }
        if (chaseCam != null)
        {
            string camText = chaseCam.currentMode switch
            {
                ChaseCamera.CameraMode.Chase => "📷 Chase",
                ChaseCamera.CameraMode.Bonnet => "📷 Bonnet",
                ChaseCamera.CameraMode.TCam => "📷 T-Cam",
                _ => "📷 Cam"
            };
            if (GUI.Button(new Rect(sw - 120f, row1Y, 110f, 32f), camText))
            {
                chaseCam.ToggleCameraMode();
            }
        }

        // Row 2: Navigation & Debug (TP Prev, TP Next, Debug)
        if (GUI.Button(new Rect(sw - 230f, row2Y, 68f, 28f), "TP Prev"))
        {
            currentCornerTeleportIdx--;
            TeleportToCorner(currentCornerTeleportIdx);
        }
        if (GUI.Button(new Rect(sw - 157f, row2Y, 68f, 28f), "TP Next"))
        {
            currentCornerTeleportIdx++;
            TeleportToCorner(currentCornerTeleportIdx);
        }
        if (GUI.Button(new Rect(sw - 84f, row2Y, 74f, 28f), isDebugActive ? "Debug ON" : "Debug"))
        {
            isDebugActive = !isDebugActive;
            if (debugVisualRoot != null) debugVisualRoot.SetActive(isDebugActive);
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
        var s0 = trackConfig.samples[0];
        CreateDebugPole("Pole_SF", s0.x, s0.y, s0.z, Color.white, lineMat);

        if (lapTimer != null && spline != null)
        {
            var qS1 = spline.QueryAtS(lapTimer.Sector1SplitS);
            CreateDebugPole("Pole_S1", qS1.x, qS1.heightY, qS1.z, Color.cyan, lineMat);

            var qS2 = spline.QueryAtS(lapTimer.Sector2SplitS);
            CreateDebugPole("Pole_S2", qS2.x, qS2.heightY, qS2.z, Color.magenta, lineMat);
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

        // DRS / Aero Badge
        bool isStraight = model.Aero.CurrentMode == AeroMode.Straight;
        string drsText = isStraight ? "<color=#00FF66><b>[ DRS ]</b></color>" : "<color=#888888>[ CRN ]</color>";
        string latGText = $"{model.LatG:+0.0;-0.0}G";
        GUI.Label(new Rect(rightX, rightY + 26f, barW, 16f), drsText, hudSubLabelStyle);
        GUI.Label(new Rect(rightX, rightY + 42f, barW, 16f), latGText, hudSubLabelStyle);
    }
}
