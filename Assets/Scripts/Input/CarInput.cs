using UnityEngine;
using UnityEngine.InputSystem;

public class CarInput : MonoBehaviour
{
    public float Steer, Throttle, Brake; // Steer: +1 = right

    [Header("Version")]
    public const int CURRENT_INPUT_VERSION = 17;
    public int inputParamsVersion;

    public enum ControlMode { Tilt, Touch }

    [Header("Control Mode")]
    public ControlMode controlMode = ControlMode.Tilt;

    [Header("Tilt & Steering Configuration")]
    public bool useTilt = true;
    public float maxTiltDeg = 90.0f;       // Physical device roll for full lock (stable baseline: 90 deg full phone tilt)
    public float deadzoneDeg = 4.5f;       // Stable deadzone preventing resting sensor noise/drift (4.5 deg baseline)
    public float steerSmoothing = 20.0f;   // Direct yet smooth steering rack interpolation rate (s^-1)
    public float tiltFilter = 22.0f;       // Accelerometer low-pass filter rate (s^-1)
    public float minPlaneG = 0.08f;
    public float steerExpo = 1.40f;        // Progressive response curve: gentle near center, full lock reachable
    public bool invertTilt = false;
    public float calibrationDelay = 3f;

    [Header("Sensitivity Slider (0 to 10)")]
    [Range(0, 10)]
    public float sensitivitySlider = 2.0f; // Level 2 default (stable baseline: 90 deg max tilt)

    [Header("Speed-Sensitive Steering Lock Curve")]
    public bool useSpeedSensitiveLock = false;   // Disabled in input layer: VehicleDynamics handles grip limits directly
    public float speedLockMinMultiplier = 1.0f;  // Full command authority at speed so player can carry speed in corners
    public float speedLockMaxKmh = 320f;

    [Header("Tilt Auto-Center")]
    public bool autoCenter = true;
    public float autoCenterRate = 2f;       // deg/s
    public float autoCenterWindowDeg = 8f;  // |relDeg| must be below this

    [Header("Touch Steer Rates")]
    public float touchSteerAttackRate = 4.0f;   // Smooth turn-in rate (s^-1)
    public float touchSteerReleaseRate = 14.0f; // Rapid auto-centering release rate (s^-1)
    float touchSteerCurrent = 0f;
    bool isTouchSteeringLeft = false;
    bool isTouchSteeringRight = false;

    [Header("Pedals")]
    public bool showZones = true;
    [Range(0.08f, 0.40f)] public float pedalWidthRatio = 0.22f;
    [Range(0.15f, 0.60f)] public float pedalHeightRatio = 0.40f;
    [Range(0.05f, 0.35f)] public float brakeWidthRatio = 0.15f;
    [Range(0.10f, 0.50f)] public float brakeHeightRatio = 0.30f;
    [Range(0.01f, 0.50f)] public float pedalOpacity = 0.22f;
    [Range(0.02f, 0.80f)] public float pedalActiveOpacity = 0.55f;

    // State
    Vector3 lastAcc;
    Vector2 gravXY;
    bool tiltSeeded, calibrated;
    bool isTiltValid;
    float validContinuousTimer;
    float rollDeg, offsetDeg, relDeg, calibrateAt, steerTarget;
    float autoCenterTimer;
    Rect brakeZone, throttleZone;

    // Phase 3 Telemetry Exposures
    public float RawRollDeg => rollDeg;
    public float OffsetDeg => offsetDeg;
    public float RelDeg => relDeg;
    public float SteerTarget => steerTarget;
    public bool  IsCalibrated => calibrated;

    // Button Rects and hit detection state
    private Rect rectMode;
    private Rect rectCalibrate;
    private Rect rectSensMinus;
    private Rect rectSensLabel;
    private Rect rectSensPlus;
    private Rect rectInvert;
    private Rect rectZones;
    private float _calibFeedbackTimer = 0f;
    private int _lastButtonActionFrame = -1;

    // Driving Controls: Pit Limiter & DRS Buttons
    public Rect pitLimiterZone;
    public Rect drsZone;
    public Rect pitLimiterGuiRect;
    public Rect drsGuiRect;

    // Reference to CarController for auto-center conditions
    CarController ctrl;

    void Awake()
    {
        // Load persisted preferences
        controlMode = (ControlMode)PlayerPrefs.GetInt("F1Sig_ControlMode", (int)ControlMode.Tilt);
        useTilt = (controlMode == ControlMode.Tilt);
        invertTilt = PlayerPrefs.GetInt("F1Sig_InvertTilt", 0) == 1;
        showZones = PlayerPrefs.GetInt("F1Sig_ShowZones", 1) == 1;
        sensitivitySlider = PlayerPrefs.GetFloat("F1Sig_SteerSensSlider", 2.0f);
        UpdateMaxTiltFromSlider();

        // Purge legacy tune keys if present
        if (PlayerPrefs.HasKey("F1Sig_Tune_MaxTilt"))
        {
            PlayerPrefs.DeleteKey("F1Sig_Tune_MaxTilt");
            PlayerPrefs.DeleteKey("F1Sig_Tune_HiSpdMult");
            PlayerPrefs.DeleteKey("F1Sig_Tune_Expo");
            PlayerPrefs.DeleteKey("F1Sig_Tune_Smoothing");
            PlayerPrefs.DeleteKey("F1Sig_Tune_Deadzone");
            PlayerPrefs.Save();
        }

        if (inputParamsVersion != CURRENT_INPUT_VERSION)
        {
            Debug.LogWarning($"[CarInput] Config version mismatch (found v{inputParamsVersion}, current v{CURRENT_INPUT_VERSION}). Restoring stable baseline configuration:\n" +
                             $"  controlMode: TILT\n" +
                             $"  maxTiltDeg: 90.0 (slider level 2 baseline, range 95-65 deg)\n" +
                             $"  deadzoneDeg: 4.5\n" +
                             $"  steerExpo: 1.40\n" +
                             $"  steerSmoothing: 20.0\n" +
                             $"  tiltFilter: 22.0\n" +
                             $"  invertTilt: False");

            sensitivitySlider = 2.0f;
            controlMode = ControlMode.Tilt;
            useTilt = true;
            invertTilt = false;
            PlayerPrefs.SetInt("F1Sig_ControlMode", (int)ControlMode.Tilt);
            PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", 2.0f);
            PlayerPrefs.SetInt("F1Sig_InvertTilt", 0);
            PlayerPrefs.Save();

            deadzoneDeg = 4.5f;
            UpdateMaxTiltFromSlider();
            steerSmoothing = 20.0f;
            tiltFilter = 22.0f;
            useSpeedSensitiveLock = false;
            speedLockMinMultiplier = 1.0f;
            speedLockMaxKmh = 320f;
            minPlaneG = 0.08f;
            steerExpo = 1.40f;
            calibrationDelay = 3f;
            autoCenter = true;
            autoCenterRate = 2f;
            autoCenterWindowDeg = 8f;
            touchSteerAttackRate = 4.0f;
            touchSteerReleaseRate = 14.0f;
            inputParamsVersion = CURRENT_INPUT_VERSION;

            var c = GetComponent<CarController>();
            if (c != null) c.ShowResetBanner();
        }
    }

    /// <summary>
    /// Remaps 0-10 slider to maxTiltDeg:
    /// Level 0  -> 95 deg (calmest)
    /// Level 2  -> 90 deg (stable baseline default)
    /// Level 5  -> 80 deg
    /// Level 10 -> 65 deg (most sensitive)
    /// Note: deadzoneDeg remains independent at 4.5 deg so sensitivity adjustments do not alter deadzone.
    /// </summary>
    public void UpdateMaxTiltFromSlider()
    {
        float t = Mathf.Clamp01(sensitivitySlider / 10f);
        maxTiltDeg = Mathf.Lerp(95f, 65f, t);
    }

    void OnEnable()
    {
        Screen.orientation = ScreenOrientation.LandscapeLeft;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Application.targetFrameRate = 60;
        if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
    }

    void Start()
    {
        calibrateAt = Time.time + calibrationDelay;
        ctrl = GetComponent<CarController>();
    }

    public void Calibrate()
    {
        offsetDeg = rollDeg;
        calibrated = true;
        _calibFeedbackTimer = 1.2f;
    }

    public void ToggleControlMode()
    {
        if (_lastButtonActionFrame == Time.frameCount) return;
        _lastButtonActionFrame = Time.frameCount;

        controlMode = (controlMode == ControlMode.Tilt) ? ControlMode.Touch : ControlMode.Tilt;
        useTilt = (controlMode == ControlMode.Tilt);
        PlayerPrefs.SetInt("F1Sig_ControlMode", (int)controlMode);
        PlayerPrefs.Save();
    }

    public void OnCalibratePressed()
    {
        if (_lastButtonActionFrame == Time.frameCount) return;
        _lastButtonActionFrame = Time.frameCount;
        Calibrate();
    }

    public void OnSensMinusPressed()
    {
        if (_lastButtonActionFrame == Time.frameCount) return;
        _lastButtonActionFrame = Time.frameCount;

        sensitivitySlider = Mathf.Clamp(sensitivitySlider - 1f, 0f, 10f);
        if (controlMode == ControlMode.Tilt)
        {
            UpdateMaxTiltFromSlider();
        }
        else
        {
            touchSteerAttackRate = Mathf.Lerp(2.5f, 5.0f, sensitivitySlider / 10f);
        }
        PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", sensitivitySlider);
        PlayerPrefs.Save();
    }

    public void OnSensPlusPressed()
    {
        if (_lastButtonActionFrame == Time.frameCount) return;
        _lastButtonActionFrame = Time.frameCount;

        sensitivitySlider = Mathf.Clamp(sensitivitySlider + 1f, 0f, 10f);
        if (controlMode == ControlMode.Tilt)
        {
            UpdateMaxTiltFromSlider();
        }
        else
        {
            touchSteerAttackRate = Mathf.Lerp(2.5f, 5.0f, sensitivitySlider / 10f);
        }
        PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", sensitivitySlider);
        PlayerPrefs.Save();
    }

    public void ToggleInvertTilt()
    {
        if (_lastButtonActionFrame == Time.frameCount) return;
        _lastButtonActionFrame = Time.frameCount;

        invertTilt = !invertTilt;
        PlayerPrefs.SetInt("F1Sig_InvertTilt", invertTilt ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void ToggleShowZones()
    {
        if (_lastButtonActionFrame == Time.frameCount) return;
        _lastButtonActionFrame = Time.frameCount;

        showZones = !showZones;
        PlayerPrefs.SetInt("F1Sig_ShowZones", showZones ? 1 : 0);
        PlayerPrefs.Save();
    }

    void UpdateButtonRects(float sw, float sh)
    {
        float centerBtnX = sw * 0.5f;
        float btnY = 126f;
        float btnH = 34f;

        if (controlMode == ControlMode.Tilt)
        {
            float wMode = 96f, wCalib = 96f, wMinus = 36f, wSensLbl = 68f, wPlus = 36f, wInv = 78f;
            float gap = 6f;
            float totalW = wMode + gap + wCalib + gap + wMinus + 3f + wSensLbl + 3f + wPlus + gap + wInv;
            float curX = centerBtnX - totalW * 0.5f;

            rectMode      = new Rect(curX, btnY, wMode, btnH); curX += wMode + gap;
            rectCalibrate = new Rect(curX, btnY, wCalib, btnH); curX += wCalib + gap;
            rectSensMinus = new Rect(curX, btnY, wMinus, btnH); curX += wMinus + 3f;
            rectSensLabel = new Rect(curX, btnY, wSensLbl, btnH); curX += wSensLbl + 3f;
            rectSensPlus  = new Rect(curX, btnY, wPlus, btnH); curX += wPlus + gap;
            rectInvert    = new Rect(curX, btnY, wInv, btnH);
            rectZones     = Rect.zero;
        }
        else // Touch Mode
        {
            float wMode = 106f, wZones = 96f, wMinus = 36f, wSensLbl = 68f, wPlus = 36f;
            float gap = 6f;
            float totalW = wMode + gap + wZones + gap + wMinus + 3f + wSensLbl + 3f + wPlus;
            float curX = centerBtnX - totalW * 0.5f;

            rectMode      = new Rect(curX, btnY, wMode, btnH); curX += wMode + gap;
            rectZones     = new Rect(curX, btnY, wZones, btnH); curX += wZones + gap;
            rectSensMinus = new Rect(curX, btnY, wMinus, btnH); curX += wMinus + 3f;
            rectSensLabel = new Rect(curX, btnY, wSensLbl, btnH); curX += wSensLbl + 3f;
            rectSensPlus  = new Rect(curX, btnY, wPlus, btnH);
            rectCalibrate = Rect.zero;
            rectInvert    = Rect.zero;
        }
    }

    private static bool HitTest(Rect r, Vector2 p, float margin = 4f)
    {
        if (r.width <= 0f || r.height <= 0f) return false;
        return p.x >= r.xMin - margin && p.x <= r.xMax + margin &&
               p.y >= r.yMin - margin && p.y <= r.yMax + margin;
    }

    private static bool HitTestCircle(Rect r, Vector2 p, float margin = 4f)
    {
        if (r.width <= 0f || r.height <= 0f) return false;
        Vector2 center = r.center;
        float radius = r.width * 0.5f + margin;
        return (p - center).sqrMagnitude <= radius * radius;
    }

    private void CheckButtonPress(Vector2 guiPos)
    {
        if (HitTest(rectMode, guiPos))
        {
            ToggleControlMode();
            return;
        }

        if (controlMode == ControlMode.Tilt)
        {
            if (HitTest(rectCalibrate, guiPos)) OnCalibratePressed();
            else if (HitTest(rectSensMinus, guiPos)) OnSensMinusPressed();
            else if (HitTest(rectSensPlus, guiPos)) OnSensPlusPressed();
            else if (HitTest(rectInvert, guiPos)) ToggleInvertTilt();
        }
        else
        {
            if (HitTest(rectZones, guiPos)) ToggleShowZones();
            else if (HitTest(rectSensMinus, guiPos)) OnSensMinusPressed();
            else if (HitTest(rectSensPlus, guiPos)) OnSensPlusPressed();
        }

        if (HitTestCircle(pitLimiterGuiRect, guiPos) && ctrl != null)
        {
            ctrl.TogglePitLimiter();
        }
        else if (HitTestCircle(drsGuiRect, guiPos) && ctrl != null)
        {
            ctrl.ToggleAero();
        }
    }

    public void ResetInput()
    {
        Steer = 0f;
        Throttle = 0f;
        Brake = 0f;
        steerTarget = 0f;
        touchSteerCurrent = 0f;
        isTouchSteeringLeft = false;
        isTouchSteeringRight = false;
        relDeg = 0f;
        autoCenterTimer = 0f;
    }

    void Update()
    {
        float W = Screen.width, H = Screen.height;
        float thrW = W * pedalWidthRatio;
        float thrH = H * pedalHeightRatio;
        float brkW = W * brakeWidthRatio;
        float brkH = H * brakeHeightRatio;

        // Brake on left side (smaller), Throttle on right side (larger)
        brakeZone    = new Rect(W * 0.03f, 0f, brkW, brkH);
        throttleZone = new Rect(W * (1.0f - 0.03f - pedalWidthRatio), 0f, thrW, thrH);

        // Circular Tactical Buttons on the Left Side (directly above Brake pedal)
        // Enlarged button size for easy thumb targeting during mobile gameplay
        float btnD = Mathf.Clamp(H * 0.17f, 75f, 105f);
        float btnGap = 16f;
        float clusterW = btnD * 2f + btnGap;
        float startX = brakeZone.x + Mathf.Max(0f, (brkW - clusterW) * 0.5f);

        // Both circular buttons on left side (screen coords: y=0 is bottom):
        pitLimiterZone = new Rect(startX, brkH + 12f, btnD, btnD);
        drsZone        = new Rect(startX + btnD + btnGap, brkH + 12f, btnD, btnD);

        pitLimiterGuiRect = ToGui(pitLimiterZone);
        drsGuiRect        = ToGui(drsZone);

        float dt = Time.deltaTime;
        if (_calibFeedbackTimer > 0f)
            _calibFeedbackTimer -= dt;

        UpdateButtonRects(W, H);

        steerTarget = 0f;
        float thrTarget = 0f, brkTarget = 0f;
        isTouchSteeringLeft = false;
        isTouchSteeringRight = false;

        // Process Touchscreen touches
        // FULL MULTI-TOUCH SUPPORT:
        // Every active touch is evaluated simultaneously. A player can hold throttle with one finger
        // while tapping/holding DRS, Pit Limiter, Brake, or Steering with other fingers without conflict.
        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var touch in ts.touches)
            {
                if (!touch.press.isPressed) continue;
                Vector2 p = touch.position.ReadValue();
                Vector2 guiPos = new Vector2(p.x, H - p.y);

                // Top toolbar area: button taps only, never steering/pedals
                if (guiPos.y <= 170f)
                {
                    if (touch.press.wasPressedThisFrame)
                    {
                        CheckButtonPress(guiPos);
                    }
                    continue;
                }

                // Circular Pit Limiter and DRS buttons:
                // Evaluated per-touch so pressing either button does NOT cancel active throttle or brake touches
                if (HitTestCircle(pitLimiterZone, p))
                {
                    if (touch.press.wasPressedThisFrame && ctrl != null)
                        ctrl.TogglePitLimiter();
                    continue;
                }
                if (HitTestCircle(drsZone, p))
                {
                    if (touch.press.wasPressedThisFrame && ctrl != null)
                        ctrl.ToggleAero();
                    continue;
                }

                // Pedal detection (multi-touch independent — throttle and brake can be held simultaneously)
                bool inThrottle = throttleZone.Contains(p);
                bool inBrake    = brakeZone.Contains(p);

                if (inThrottle) thrTarget = 1f;
                if (inBrake)    brkTarget = 1f;

                // Touch steering: any touch NOT on a pedal zone or tactical button counts as a steer touch
                if (controlMode == ControlMode.Touch && !inThrottle && !inBrake)
                {
                    if (p.x < W * 0.5f) isTouchSteeringLeft  = true;
                    else                 isTouchSteeringRight = true;
                }
            }
        }

        // Process Mouse input (allows testing inside Unity Editor and Desktop builds)
        // Same fix applied: pedal and steering are independent, not mutually exclusive
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 mp = mouse.position.ReadValue();
            Vector2 guiPos = new Vector2(mp.x, H - mp.y);

            if (mouse.leftButton.wasPressedThisFrame && guiPos.y <= 170f)
            {
                CheckButtonPress(guiPos);
            }

            if (mouse.leftButton.wasPressedThisFrame && guiPos.y > 170f)
            {
                if (HitTestCircle(pitLimiterZone, mp) && ctrl != null)
                    ctrl.TogglePitLimiter();
                else if (HitTestCircle(drsZone, mp) && ctrl != null)
                    ctrl.ToggleAero();
            }

            if (mouse.leftButton.isPressed && guiPos.y > 170f)
            {
                if (!HitTestCircle(pitLimiterZone, mp) && !HitTestCircle(drsZone, mp))
                {
                    bool inThrottleMouse = throttleZone.Contains(mp);
                    bool inBrakeMouse    = brakeZone.Contains(mp);
                    if (inThrottleMouse) thrTarget = 1f;
                    if (inBrakeMouse)    brkTarget = 1f;

                    if (controlMode == ControlMode.Touch && !inThrottleMouse && !inBrakeMouse)
                    {
                        if (mp.x < W * 0.5f) isTouchSteeringLeft = true;
                        else isTouchSteeringRight = true;
                    }
                }
            }
        }

        // Speed scaling multiplier
        float speedKmh = ctrl != null ? ctrl.SpeedKmh : 0f;
        float speedMult = 1f;
        if (useSpeedSensitiveLock)
        {
            speedMult = Mathf.Clamp(1.0f - (1.0f - speedLockMinMultiplier) * (speedKmh / speedLockMaxKmh), speedLockMinMultiplier, 1.0f);
        }

        // ── Touch Steering Mode (Real Racing 3 Touch A/B) ────────────────
        if (controlMode == ControlMode.Touch)
        {
            float targetTouch = 0f;
            if (isTouchSteeringLeft && !isTouchSteeringRight) targetTouch = -1f;
            else if (isTouchSteeringRight && !isTouchSteeringLeft) targetTouch = 1f;

            if (targetTouch != 0f)
            {
                touchSteerCurrent = Mathf.MoveTowards(touchSteerCurrent, targetTouch, touchSteerAttackRate * dt);
            }
            else
            {
                touchSteerCurrent = Mathf.MoveTowards(touchSteerCurrent, 0f, touchSteerReleaseRate * dt);
            }

            float absTouch = Mathf.Abs(touchSteerCurrent);
            float sNorm = steerExpo != 1f ? Mathf.Pow(absTouch, steerExpo) : absTouch;
            steerTarget = Mathf.Sign(touchSteerCurrent) * sNorm * speedMult;
        }

        // ── Tilt Steering Mode (Real Racing 3 Tilt A/B) ──────────────────
        var acc = Accelerometer.current;
        if (acc != null)
        {
            Vector3 a = acc.acceleration.ReadValue();
            lastAcc = a;
            Vector2 g2 = new Vector2(a.x, a.y);

            // Low-pass filter the gravity vector
            if (!tiltSeeded)
            {
                gravXY = g2;
                tiltSeeded = true;
            }
            else
            {
                gravXY = Vector2.Lerp(gravXY, g2, 1f - Mathf.Exp(-tiltFilter * dt));
            }

            // Check if tilt reading is valid
            isTiltValid = gravXY.magnitude >= minPlaneG;

            if (isTiltValid)
            {
                rollDeg = Mathf.Atan2(gravXY.x, gravXY.y) * Mathf.Rad2Deg;
                validContinuousTimer += dt;

                // Auto-calibrate only once the reading has been valid continuously for 1 second
                if (!calibrated && Time.time >= calibrateAt && validContinuousTimer >= 1.0f)
                {
                    Calibrate();
                }

                relDeg = calibrated
                    ? -Mathf.DeltaAngle(offsetDeg, rollDeg) * (invertTilt ? -1f : 1f)
                    : 0f;

                // ── Auto-center: drift offsetDeg toward current roll ──────────
                if (autoCenter && calibrated)
                {
                    bool condSpeed = ctrl != null && ctrl.SpeedKmh > 50f;
                    bool condLatG  = ctrl != null && Mathf.Abs(ctrl.LatG) < 0.25f;
                    bool condTilt  = Mathf.Abs(relDeg) < autoCenterWindowDeg;

                    // Also allow auto-centering if the car is stationary (e.g. stopped on grid or in pits)
                    bool condStationary = ctrl != null && ctrl.SpeedKmh < 2f && Mathf.Abs(relDeg) < autoCenterWindowDeg;

                    if ((condSpeed && condLatG && condTilt) || condStationary)
                    {
                        autoCenterTimer += dt;
                        if (autoCenterTimer >= 1.0f)
                        {
                            offsetDeg = Mathf.MoveTowards(offsetDeg, rollDeg, autoCenterRate * dt);
                            relDeg = -Mathf.DeltaAngle(offsetDeg, rollDeg) * (invertTilt ? -1f : 1f);
                        }
                    }
                    else
                    {
                        autoCenterTimer = 0f;
                    }
                }
            }
            else
            {
                validContinuousTimer = 0f;
                autoCenterTimer = 0f;
                relDeg = Mathf.MoveTowards(relDeg, 0f, 120f * dt);
            }

            if (controlMode == ControlMode.Tilt && useTilt && calibrated)
            {
                float absRel = Mathf.Abs(relDeg);
                if (absRel <= deadzoneDeg)
                {
                    steerTarget = 0f;
                }
                else
                {
                    float span = Mathf.Max(0.001f, maxTiltDeg - deadzoneDeg);
                    float sLinear = Mathf.Clamp01((absRel - deadzoneDeg) / span);
                    float sNorm = steerExpo != 1f ? Mathf.Pow(sLinear, steerExpo) : sLinear;
                    float sDir = Mathf.Sign(relDeg);

                    steerTarget = sDir * sNorm * speedMult;
                }
            }
        }

        // Keyboard fallback (for editor testing)
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.dKey.isPressed) steerTarget =  1f;
            if (kb.aKey.isPressed) steerTarget = -1f;
            if (kb.wKey.isPressed) thrTarget = 1f;
            if (kb.sKey.isPressed) brkTarget = 1f;
        }

        // Smooth output to vehicle steering rack
        Steer    = Mathf.Lerp(Steer,    steerTarget, 1f - Mathf.Exp(-steerSmoothing * dt));
        Throttle = Mathf.MoveTowards(Throttle, thrTarget, (thrTarget > Throttle ? 4f : 8f) * dt);
        Brake    = Mathf.MoveTowards(Brake,    brkTarget, (brkTarget > Brake    ? 6f : 10f) * dt);
    }

    void OnGUI()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        UpdateButtonRects(sw, sh);

        // ── Top-Center Modern Toolbar (under Minimap) ─────────────────────
        // 1. Control Mode Toggle [Tilt] / [Touch]
        string modeLabel = controlMode == ControlMode.Tilt ? "MODE: TILT" : "MODE: TOUCH";
        DrawModernButton(rectMode, modeLabel, new Color(0.35f, 0.75f, 1f), false);
        if (GUI.Button(rectMode, GUIContent.none, GUIStyle.none))
        {
            ToggleControlMode();
        }

        // 2. Mode-specific buttons
        if (controlMode == ControlMode.Tilt)
        {
            // Calibrate button
            string calibLabel = _calibFeedbackTimer > 0f ? "✓ CALIBRATED" : "CALIBRATE";
            Color calibCol = _calibFeedbackTimer > 0f ? new Color(0.2f, 1f, 0.4f) : new Color(0.75f, 0.82f, 0.95f);
            DrawModernButton(rectCalibrate, calibLabel, calibCol, _calibFeedbackTimer > 0f);
            if (GUI.Button(rectCalibrate, GUIContent.none, GUIStyle.none))
            {
                OnCalibratePressed();
            }

            // Sensitivity [-] SENS X [+]
            DrawModernButton(rectSensMinus, "—", new Color(0.7f, 0.75f, 0.85f), false);
            if (GUI.Button(rectSensMinus, GUIContent.none, GUIStyle.none))
            {
                OnSensMinusPressed();
            }
            DrawModernButton(rectSensLabel, $"SENS {sensitivitySlider:0}", new Color(0.4f, 0.8f, 1f), false);
            DrawModernButton(rectSensPlus, "+", new Color(0.7f, 0.75f, 0.85f), false);
            if (GUI.Button(rectSensPlus, GUIContent.none, GUIStyle.none))
            {
                OnSensPlusPressed();
            }

            // Invert tilt toggle
            DrawModernButton(rectInvert, invertTilt ? "INV: ON" : "INV: OFF", invertTilt ? new Color(1f, 0.6f, 0.2f) : new Color(0.6f, 0.65f, 0.75f), invertTilt);
            if (GUI.Button(rectInvert, GUIContent.none, GUIStyle.none))
            {
                ToggleInvertTilt();
            }
        }
        else // Touch Mode
        {
            // Zones toggle button
            DrawModernButton(rectZones, showZones ? "ZONES: ON" : "ZONES: OFF", showZones ? new Color(0.2f, 1f, 0.4f) : new Color(0.6f, 0.65f, 0.75f), showZones);
            if (GUI.Button(rectZones, GUIContent.none, GUIStyle.none))
            {
                ToggleShowZones();
            }

            // Sensitivity [-] SENS X [+]
            DrawModernButton(rectSensMinus, "—", new Color(0.7f, 0.75f, 0.85f), false);
            if (GUI.Button(rectSensMinus, GUIContent.none, GUIStyle.none))
            {
                OnSensMinusPressed();
            }
            DrawModernButton(rectSensLabel, $"SENS {sensitivitySlider:0}", new Color(0.4f, 0.8f, 1f), false);
            DrawModernButton(rectSensPlus, "+", new Color(0.7f, 0.75f, 0.85f), false);
            if (GUI.Button(rectSensPlus, GUIContent.none, GUIStyle.none))
            {
                OnSensPlusPressed();
            }
        }

        // Steer HUD status readout (Cleanly shown only when Debug is ON)
        if (ctrl != null && ctrl.isDebugActive)
        {
            float hudY = 205f;
            if (controlMode == ControlMode.Tilt)
            {
                string stateText = isTiltValid ? "VALID" : "TILT INVALID";
                string calibText = calibrated ? "" : $"  HOLD LEVEL... {Mathf.Max(0f, calibrateAt - Time.time):0.0}s";
                float rawRoll = Mathf.Atan2(lastAcc.x, lastAcc.y) * Mathf.Rad2Deg;
                float steerWheelDeg = Steer * 90f; // F1 steering wheel angle (-90 to +90 deg max)
                GUI.Label(new Rect(8f, hudY, 620f, 65f),
                    $"[TILT DIAG] RawRoll: {rawRoll:+0.0;-0.0}° | FilteredRoll: {rollDeg:+0.0;-0.0}° | CalibRelTilt: {relDeg:+0.0;-0.0}° (Deadzone: {deadzoneDeg:0.0}°)\n" +
                    $"NormTarget: {steerTarget:+0.000;-0.000} | FinalSteer: {Steer:+0.000;-0.000} (Wheel: {steerWheelDeg:+0.0;-0.0}°) | MaxTilt: {maxTiltDeg:0.0}° [{stateText}]{calibText}\n" +
                    $"AccRaw: ({lastAcc.x:+0.00;-0.00}, {lastAcc.y:+0.00;-0.00}, {lastAcc.z:+0.00;-0.00}) | GravXY: {gravXY.magnitude:0.00}g | SensSlider: {sensitivitySlider:0}");
            }
            else
            {
                string touchState = isTouchSteeringLeft ? "◄ TURNING LEFT" : (isTouchSteeringRight ? "TURNING RIGHT ►" : "CENTERED");
                GUI.Label(new Rect(8f, hudY, 560f, 50f),
                    $"TOUCH STEER [RR3 Touch A] — State: {touchState}\n" +
                    $"SteerCmd: {Steer:0.00}  Target: {touchSteerCurrent:0.00}  Sens: {sensitivitySlider:0}");
            }
        }

        // ── Tactical Circular Buttons on Left Side (Pit Limiter & DRS) ───
        bool isPitActive = ctrl != null && ctrl.isPitLimiterActive;
        Color pitColor = new Color(1f, 0.78f, 0.1f);
        string pitSub = isPitActive ? "<color=#FFEE55>80 ON</color>" : "80";
        DrawModernCircleButton(pitLimiterGuiRect, "PIT", pitColor, isPitActive, pitSub);
        if (GUI.Button(pitLimiterGuiRect, GUIContent.none, GUIStyle.none))
        {
            // Fallback for mouse / desktop GUI button clicks if not already toggled this frame
            if (_lastButtonActionFrame != Time.frameCount && ctrl != null)
            {
                _lastButtonActionFrame = Time.frameCount;
                ctrl.TogglePitLimiter();
            }
        }

        bool isDrsActive = ctrl != null && ctrl.IsDrsActive;
        Color drsColor = new Color(0.0f, 0.95f, 0.45f);
        string drsSub = isDrsActive ? "<color=#55FF88>OPEN</color>" : "AERO";
        DrawModernCircleButton(drsGuiRect, "DRS", drsColor, isDrsActive, drsSub);
        if (GUI.Button(drsGuiRect, GUIContent.none, GUIStyle.none))
        {
            // Fallback for mouse / desktop GUI button clicks if not already toggled this frame
            if (_lastButtonActionFrame != Time.frameCount && ctrl != null)
            {
                _lastButtonActionFrame = Time.frameCount;
                ctrl.ToggleAero();
            }
        }

        if (showZones)
        {
            Color prevColor = GUI.color;
            if (_zonePixel == null)
            {
                _zonePixel = new Texture2D(1, 1);
                _zonePixel.SetPixel(0, 0, Color.white);
                _zonePixel.Apply();
            }

            DrawTouchZone(ToGui(brakeZone), Brake > 0.05f, "BRAKE", new Color(1f, 0.28f, 0.28f));
            DrawTouchZone(ToGui(throttleZone), Throttle > 0.05f, "THROTTLE", new Color(0.2f, 0.95f, 0.35f));

            if (controlMode == ControlMode.Touch)
            {
                Rect leftSteerRect = new Rect(10f, sh * 0.35f, 100f, 60f);
                Rect rightSteerRect = new Rect(sw - 110f, sh * 0.35f, 100f, 60f);
                DrawTouchZone(leftSteerRect, isTouchSteeringLeft, "◀ STEER L", new Color(0.4f, 0.7f, 1.0f));
                DrawTouchZone(rightSteerRect, isTouchSteeringRight, "STEER R ▶", new Color(0.4f, 0.7f, 1.0f));
            }

            GUI.color = prevColor;
        }
    }

    public static void DrawModernButton(Rect r, string label, Color accent, bool active, string subLabel = null)
    {
        if (r.width <= 0f || r.height <= 0f) return;

        if (_zonePixel == null)
        {
            _zonePixel = new Texture2D(1, 1);
            _zonePixel.SetPixel(0, 0, Color.white);
            _zonePixel.Apply();
        }

        // Translucent dark glass background
        Color bgColor = active
            ? new Color(accent.r * 0.38f, accent.g * 0.38f, accent.b * 0.38f, 0.88f)
            : new Color(0.06f, 0.08f, 0.12f, 0.78f);
        GUI.color = bgColor;
        GUI.DrawTexture(r, _zonePixel);

        // Border outline
        Color borderColor = active
            ? accent
            : new Color(accent.r * 0.5f + 0.2f, accent.g * 0.5f + 0.2f, accent.b * 0.5f + 0.2f, 0.65f);
        float bw = active ? 2.5f : 1.5f;
        GUI.color = borderColor;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, bw), _zonePixel);
        GUI.DrawTexture(new Rect(r.x, r.yMax - bw, r.width, bw), _zonePixel);
        GUI.DrawTexture(new Rect(r.x, r.y, bw, r.height), _zonePixel);
        GUI.DrawTexture(new Rect(r.xMax - bw, r.y, bw, r.height), _zonePixel);

        // Text
        GUI.color = Color.white;
        if (string.IsNullOrEmpty(subLabel))
        {
            GUIStyle bStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.38f), 11, 15),
                richText = true
            };
            bStyle.normal.textColor = active ? Color.white : new Color(0.9f, 0.93f, 0.98f, 0.95f);
            GUI.Label(r, label, bStyle);
        }
        else
        {
            GUIStyle topStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.32f), 11, 14),
                richText = true
            };
            topStyle.normal.textColor = active ? Color.white : new Color(0.9f, 0.93f, 0.98f, 0.95f);
            Rect topRect = new Rect(r.x, r.y + 3f, r.width, r.height * 0.50f);
            GUI.Label(topRect, label, topStyle);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.26f), 9, 12),
                richText = true
            };
            subStyle.normal.textColor = active ? accent : new Color(0.7f, 0.75f, 0.82f, 0.85f);
            Rect botRect = new Rect(r.x, r.y + r.height * 0.46f, r.width, r.height * 0.48f);
            GUI.Label(botRect, subLabel, subStyle);
        }
    }

    static Texture2D _zonePixel;
    void DrawTouchZone(Rect r, bool active, string label, Color accent)
    {
        float fillAlpha = active ? pedalActiveOpacity : pedalOpacity;
        // Semi-transparent solid background fill
        GUI.color = new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, fillAlpha + 0.15f);
        GUI.DrawTexture(r, _zonePixel);

        // Highlight tint fill when pressed / active
        if (active)
        {
            GUI.color = new Color(accent.r, accent.g, accent.b, fillAlpha);
            GUI.DrawTexture(r, _zonePixel);
        }

        // Clean border outline
        float borderAlpha = active ? 0.90f : 0.65f;
        GUI.color = new Color(accent.r, accent.g, accent.b, borderAlpha);
        float bw = 2.5f;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, bw), _zonePixel);
        GUI.DrawTexture(new Rect(r.x, r.yMax - bw, r.width, bw), _zonePixel);
        GUI.DrawTexture(new Rect(r.x, r.y, bw, r.height), _zonePixel);
        GUI.DrawTexture(new Rect(r.xMax - bw, r.y, bw, r.height), _zonePixel);

        // Clear high-contrast pedal badge label
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.18f), 12, 22),
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        labelStyle.normal.textColor = active ? Color.white : new Color(accent.r, accent.g, accent.b, 0.95f);
        GUI.color = Color.white;
        GUI.Label(r, label, labelStyle);
    }

    static Texture2D _circleTex;
    public static void DrawModernCircleButton(Rect r, string label, Color accent, bool active, string subLabel = null)
    {
        if (r.width <= 0f || r.height <= 0f) return;

        if (_circleTex == null)
        {
            _circleTex = CreateCircleTexture(128);
        }

        // 1. Outer Border Ring
        Color borderColor = active
            ? accent
            : new Color(accent.r * 0.55f + 0.2f, accent.g * 0.55f + 0.2f, accent.b * 0.55f + 0.2f, 0.80f);
        GUI.color = borderColor;
        GUI.DrawTexture(r, _circleTex);

        // 2. Inner Circular Fill (inset by border width)
        float bw = active ? 3.0f : 2.0f;
        Rect innerRect = new Rect(r.x + bw, r.y + bw, r.width - 2f * bw, r.height - 2f * bw);
        Color bgColor = active
            ? new Color(accent.r * 0.45f, accent.g * 0.45f, accent.b * 0.45f, 0.92f)
            : new Color(0.06f, 0.08f, 0.12f, 0.82f);
        GUI.color = bgColor;
        GUI.DrawTexture(innerRect, _circleTex);

        // 3. Central Typography
        GUI.color = Color.white;
        if (string.IsNullOrEmpty(subLabel))
        {
            GUIStyle bStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.32f), 11, 16),
                richText = true
            };
            bStyle.normal.textColor = active ? Color.white : new Color(0.92f, 0.95f, 0.98f, 0.95f);
            GUI.Label(r, label, bStyle);
        }
        else
        {
            GUIStyle topStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.26f), 11, 15),
                richText = true
            };
            topStyle.normal.textColor = active ? Color.white : new Color(0.92f, 0.95f, 0.98f, 0.95f);
            Rect topRect = new Rect(r.x, r.y + r.height * 0.16f, r.width, r.height * 0.36f);
            GUI.Label(topRect, label, topStyle);

            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(r.height * 0.20f), 9, 12),
                richText = true
            };
            subStyle.normal.textColor = active ? accent : new Color(0.70f, 0.75f, 0.82f, 0.85f);
            Rect botRect = new Rect(r.x, r.y + r.height * 0.48f, r.width, r.height * 0.34f);
            GUI.Label(botRect, subLabel, subStyle);
        }
    }

    static Texture2D CreateCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size * 0.5f;
        Vector2 center = new Vector2(radius - 0.5f, radius - 0.5f);
        Color[] cols = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                if (d < radius - 1.2f)
                {
                    cols[y * size + x] = Color.white;
                }
                else if (d <= radius)
                {
                    float a = Mathf.Clamp01(radius - d);
                    cols[y * size + x] = new Color(1f, 1f, 1f, a);
                }
                else
                {
                    cols[y * size + x] = Color.clear;
                }
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        return tex;
    }

    Rect ToGui(Rect z) =>
        new Rect(z.x, Screen.height - z.yMax, z.width, z.height);
}
