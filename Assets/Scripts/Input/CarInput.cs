using UnityEngine;
using UnityEngine.InputSystem;

public class CarInput : MonoBehaviour
{
    public float Steer, Throttle, Brake; // Steer: +1 = right

    [Header("Version")]
    public const int CURRENT_INPUT_VERSION = 14;
    public int inputParamsVersion;

    public enum ControlMode { Tilt, Touch }

    [Header("Control Mode")]
    public ControlMode controlMode = ControlMode.Tilt;

    [Header("Tilt & Steering Configuration")]
    public bool useTilt = true;
    public float maxTiltDeg = 90.0f;       // Physical device roll for full lock (90 deg full phone tilt)
    public float deadzoneDeg = 4.5f;       // Stable deadzone preventing accidental drift (4.5 deg)
    public float steerSmoothing = 14.0f;   // Responsive yet smooth interpolation rate (s^-1)
    public float tiltFilter = 20.0f;       // Accelerometer low-pass filter rate (s^-1)
    public float minPlaneG = 0.08f;
    public float steerExpo = 1.40f;        // Progressive curve: steer is at most 90 deg even at full tilt
    public bool invertTilt = false;
    public float calibrationDelay = 3f;

    [Header("Sensitivity Slider (0 to 10)")]
    [Range(0, 10)]
    public float sensitivitySlider = 2.0f; // Level 2 default (90 deg full tilt = at most 90 deg steer)

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
    public bool showZones = false;
    [Range(0.08f, 0.40f)] public float pedalWidthRatio = 0.18f;
    [Range(0.15f, 0.60f)] public float pedalHeightRatio = 0.35f;
    [Range(0.01f, 0.08f)] public float pedalOpacity = 0.04f;
    [Range(0.02f, 0.15f)] public float pedalActiveOpacity = 0.08f;

    // State
    Vector3 lastAcc;
    Vector2 gravXY;
    bool tiltSeeded, calibrated;
    bool isTiltValid;
    float validContinuousTimer;
    float rollDeg, offsetDeg, relDeg, calibrateAt, steerTarget;
    float autoCenterTimer;
    Rect brakeZone, throttleZone;

    // Reference to CarController for auto-center conditions
    CarController ctrl;

    void Awake()
    {
        // Load persisted preferences
        controlMode = (ControlMode)PlayerPrefs.GetInt("F1Sig_ControlMode", (int)ControlMode.Tilt);
        useTilt = (controlMode == ControlMode.Tilt);
        invertTilt = PlayerPrefs.GetInt("F1Sig_InvertTilt", 0) == 1;
        showZones = PlayerPrefs.GetInt("F1Sig_ShowZones", 0) == 1;
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
            Debug.LogWarning($"[CarInput] Config version mismatch (found v{inputParamsVersion}, current v{CURRENT_INPUT_VERSION}). Applying at-most-90-deg steer configuration:\n" +
                             $"  controlMode: TILT\n" +
                             $"  maxTiltDeg: 90.0 (full phone tilt = at most 90 deg steer)\n" +
                             $"  deadzoneDeg: 4.5\n" +
                             $"  steerExpo: 1.40\n" +
                             $"  invertTilt: False");

            sensitivitySlider = 2.0f;
            controlMode = ControlMode.Tilt;
            useTilt = true;
            invertTilt = false;
            PlayerPrefs.SetInt("F1Sig_ControlMode", (int)ControlMode.Tilt);
            PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", 2.0f);
            PlayerPrefs.SetInt("F1Sig_InvertTilt", 0);
            PlayerPrefs.Save();

            UpdateMaxTiltFromSlider();
            steerSmoothing = 14.0f;
            tiltFilter = 20.0f;
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
    /// Remaps 0-10 slider:
    /// Level 0 -> 95 deg (5.0 deg deadzone, calmest)
    /// Level 2 -> 90 deg (4.5 deg deadzone, full phone tilt = at most 90 deg steer)
    /// Level 5 -> 80 deg (4.0 deg deadzone)
    /// Level 10 -> 65 deg (3.0 deg deadzone)
    /// </summary>
    public void UpdateMaxTiltFromSlider()
    {
        float t = Mathf.Clamp01(sensitivitySlider / 10f);
        maxTiltDeg = Mathf.Lerp(95f, 65f, t);
        deadzoneDeg = Mathf.Lerp(5.0f, 3.0f, t);
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
        if (isTiltValid)
        {
            offsetDeg = rollDeg;
            calibrated = true;
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
        float pW = W * pedalWidthRatio;
        float pH = H * pedalHeightRatio;
        brakeZone    = new Rect(W * 0.03f, 0f, pW, pH);
        throttleZone = new Rect(W * (1.0f - 0.03f - pedalWidthRatio), 0f, pW, pH);

        float dt = Time.deltaTime;
        steerTarget = 0f;
        float thrTarget = 0f, brkTarget = 0f;
        isTouchSteeringLeft = false;
        isTouchSteeringRight = false;

        // Process Touchscreen touches
        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var touch in ts.touches)
            {
                if (!touch.press.isPressed) continue;
                Vector2 p = touch.position.ReadValue();
                if (throttleZone.Contains(p))
                {
                    thrTarget = 1f;
                }
                else if (brakeZone.Contains(p))
                {
                    brkTarget = 1f;
                }
                else if (controlMode == ControlMode.Touch)
                {
                    if (p.x < W * 0.5f) isTouchSteeringLeft = true;
                    else isTouchSteeringRight = true;
                }
            }
        }

        // Process Mouse input (allows testing Touch mode inside Unity Editor)
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            Vector2 mp = mouse.position.ReadValue();
            if (throttleZone.Contains(mp))
            {
                thrTarget = 1f;
            }
            else if (brakeZone.Contains(mp))
            {
                brkTarget = 1f;
            }
            else if (controlMode == ControlMode.Touch)
            {
                if (mp.x < W * 0.5f) isTouchSteeringLeft = true;
                else isTouchSteeringRight = true;
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
        Matrix4x4 oldMat = GUI.matrix;
        float s = (Screen.dpi > 0 ? Screen.dpi : 320f) / 160f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        float sw = Screen.width / s;
        float centerBtnX = sw * 0.5f;
        float topBtnY = 10f;
        float topBtnH = 36f;

        // 1. Control Mode Toggle [Tilt] / [Touch]
        string modeLabel = controlMode == ControlMode.Tilt ? "Mode: TILT" : "Mode: TOUCH";
        if (GUI.Button(new Rect(centerBtnX - 165f, topBtnY, 95f, topBtnH), modeLabel))
        {
            controlMode = controlMode == ControlMode.Tilt ? ControlMode.Touch : ControlMode.Tilt;
            useTilt = (controlMode == ControlMode.Tilt);
            PlayerPrefs.SetInt("F1Sig_ControlMode", (int)controlMode);
            PlayerPrefs.Save();
        }

        // 2. Mode-specific top buttons
        if (controlMode == ControlMode.Tilt)
        {
            // Calibrate button
            if (GUI.Button(new Rect(centerBtnX - 65f, topBtnY, 75f, topBtnH), "Calibrate"))
                Calibrate();

            // Sensitivity buttons [Sens -] and [Sens +]
            if (GUI.Button(new Rect(centerBtnX + 15f, topBtnY, 70f, topBtnH), $"Sens - ({sensitivitySlider:0})"))
            {
                sensitivitySlider = Mathf.Clamp(sensitivitySlider - 1f, 0f, 10f);
                UpdateMaxTiltFromSlider();
                PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", sensitivitySlider);
                PlayerPrefs.Save();
            }
            if (GUI.Button(new Rect(centerBtnX + 90f, topBtnY, 70f, topBtnH), $"Sens + ({sensitivitySlider:0})"))
            {
                sensitivitySlider = Mathf.Clamp(sensitivitySlider + 1f, 0f, 10f);
                UpdateMaxTiltFromSlider();
                PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", sensitivitySlider);
                PlayerPrefs.Save();
            }
            // Invert tilt toggle
            if (GUI.Button(new Rect(centerBtnX + 165f, topBtnY, 65f, topBtnH), invertTilt ? "Inv: ON" : "Inv: OFF"))
            {
                invertTilt = !invertTilt;
                PlayerPrefs.SetInt("F1Sig_InvertTilt", invertTilt ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
        else // Touch Mode
        {
            // Zones toggle button
            if (GUI.Button(new Rect(centerBtnX - 65f, topBtnY, 90f, topBtnH), "Zones " + (showZones ? "ON" : "OFF")))
            {
                showZones = !showZones;
                PlayerPrefs.SetInt("F1Sig_ShowZones", showZones ? 1 : 0);
                PlayerPrefs.Save();
            }
            // Sensitivity for touch
            if (GUI.Button(new Rect(centerBtnX + 30f, topBtnY, 70f, topBtnH), $"Sens - ({sensitivitySlider:0})"))
            {
                sensitivitySlider = Mathf.Clamp(sensitivitySlider - 1f, 0f, 10f);
                touchSteerAttackRate = Mathf.Lerp(2.5f, 5.0f, sensitivitySlider / 10f);
                PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", sensitivitySlider);
                PlayerPrefs.Save();
            }
            if (GUI.Button(new Rect(centerBtnX + 105f, topBtnY, 70f, topBtnH), $"Sens + ({sensitivitySlider:0})"))
            {
                sensitivitySlider = Mathf.Clamp(sensitivitySlider + 1f, 0f, 10f);
                touchSteerAttackRate = Mathf.Lerp(2.5f, 5.0f, sensitivitySlider / 10f);
                PlayerPrefs.SetFloat("F1Sig_SteerSensSlider", sensitivitySlider);
                PlayerPrefs.Save();
            }
        }

        // Steer HUD status readout (below telemetry box)
        if (controlMode == ControlMode.Tilt)
        {
            string stateText = isTiltValid ? "VALID" : "TILT INVALID";
            string calibText = calibrated ? "" : $"  HOLD LEVEL... {Mathf.Max(0f, calibrateAt - Time.time):0.0}s";
            float steerWheelDeg = Steer * 90f; // F1 steering wheel angle (-90 to +90 deg max)
            GUI.Label(new Rect(8f, 170f, 520f, 50f),
                $"acc x {lastAcc.x:0.00} y {lastAcc.y:0.00} z {lastAcc.z:0.00}  plane {gravXY.magnitude:0.00} [{stateText}]\n" +
                $"roll {rollDeg:0.0}°  relDeg {relDeg:0.0}°  MaxTilt {maxTiltDeg:0.0}°  SteerWheel {steerWheelDeg:0.0}° (Cmd {Steer:0.00}){calibText}");
        }
        else
        {
            string touchState = isTouchSteeringLeft ? "◄ TURNING LEFT" : (isTouchSteeringRight ? "TURNING RIGHT ►" : "CENTERED");
            GUI.Label(new Rect(8f, 170f, 520f, 50f),
                $"TOUCH STEER [RR3 Touch A] — State: {touchState}\n" +
                $"SteerCmd: {Steer:0.00}  Target: {touchSteerCurrent:0.00}  Sens: {sensitivitySlider:0}");
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

            DrawTouchZone(ToGui(brakeZone, s), Brake > 0.05f, "BRAKE", new Color(1f, 0.35f, 0.35f));
            DrawTouchZone(ToGui(throttleZone, s), Throttle > 0.05f, "THROTTLE", new Color(0.35f, 1f, 0.45f));

            if (controlMode == ControlMode.Touch)
            {
                float sh = Screen.height / s;
                Rect leftSteerRect = new Rect(10f, sh * 0.35f, 100f, 60f);
                Rect rightSteerRect = new Rect(sw - 110f, sh * 0.35f, 100f, 60f);
                DrawTouchZone(leftSteerRect, isTouchSteeringLeft, "◀ STEER L", new Color(0.4f, 0.7f, 1.0f));
                DrawTouchZone(rightSteerRect, isTouchSteeringRight, "STEER R ▶", new Color(0.4f, 0.7f, 1.0f));
            }

            GUI.color = prevColor;
        }

        GUI.matrix = oldMat;
    }

    static Texture2D _zonePixel;
    void DrawTouchZone(Rect r, bool active, string label, Color accent)
    {
        float fillAlpha = active ? pedalActiveOpacity : pedalOpacity;
        // Faint fill
        GUI.color = new Color(accent.r, accent.g, accent.b, fillAlpha);
        GUI.DrawTexture(r, _zonePixel);

        // Thin outline only
        float borderAlpha = active ? 0.45f : 0.20f;
        GUI.color = new Color(accent.r, accent.g, accent.b, borderAlpha);
        float bw = 1.5f;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, bw), _zonePixel);
        GUI.DrawTexture(new Rect(r.x, r.yMax - bw, r.width, bw), _zonePixel);
        GUI.DrawTexture(new Rect(r.x, r.y, bw, r.height), _zonePixel);
        GUI.DrawTexture(new Rect(r.xMax - bw, r.y, bw, r.height), _zonePixel);

        // Small subtle label
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = active ? FontStyle.Bold : FontStyle.Normal
        };
        labelStyle.normal.textColor = new Color(1f, 1f, 1f, active ? 0.7f : 0.35f);
        GUI.color = Color.white;
        GUI.Label(r, label, labelStyle);
    }

    Rect ToGui(Rect z, float s) =>
        new Rect(z.x / s, (Screen.height - z.yMax) / s, z.width / s, z.height / s);
}
