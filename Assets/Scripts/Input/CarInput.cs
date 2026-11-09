using UnityEngine;
using UnityEngine.InputSystem;

public class CarInput : MonoBehaviour
{
    public float Steer, Throttle, Brake; // Steer: +1 = right

    [Header("Version")]
    public const int CURRENT_INPUT_VERSION = 1;
    public int inputParamsVersion;

    [Header("Tilt")]
    public bool useTilt = true;
    public float maxTiltDeg = 40f;
    public float deadzoneDeg = 3f;
    public float steerSmoothing = 10f;
    public float tiltFilter = 8f;
    public float minPlaneG = 0.12f;
    public float steerExpo = 1.4f;
    public bool invertTilt = false;
    public float calibrationDelay = 3f;

    [Header("Tilt Auto-Center")]
    public bool autoCenter = true;
    public float autoCenterRate = 2f;       // deg/s
    public float autoCenterWindowDeg = 10f; // |relDeg| must be below this

    [Header("Pedals")]
    public bool showZones = true;

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
        if (inputParamsVersion != CURRENT_INPUT_VERSION)
        {
            Debug.LogWarning($"[CarInput] Config version mismatch (found v{inputParamsVersion}, current v{CURRENT_INPUT_VERSION}). Resetting input tunables to defaults:\n" +
                             $"  maxTiltDeg: {maxTiltDeg} -> 40\n" +
                             $"  deadzoneDeg: {deadzoneDeg} -> 3\n" +
                             $"  steerExpo: {steerExpo} -> 1.4\n" +
                             $"  steerSmoothing: {steerSmoothing} -> 10\n" +
                             $"  tiltFilter: {tiltFilter} -> 8\n" +
                             $"  minPlaneG: {minPlaneG} -> 0.12\n" +
                             $"  autoCenter: {autoCenter} -> True\n" +
                             $"  autoCenterRate: {autoCenterRate} -> 2\n" +
                             $"  autoCenterWindowDeg: {autoCenterWindowDeg} -> 10");

            maxTiltDeg = 40f;
            deadzoneDeg = 3f;
            steerSmoothing = 10f;
            tiltFilter = 8f;
            minPlaneG = 0.12f;
            steerExpo = 1.4f;
            invertTilt = false;
            calibrationDelay = 3f;
            autoCenter = true;
            autoCenterRate = 2f;
            autoCenterWindowDeg = 10f;
            inputParamsVersion = CURRENT_INPUT_VERSION;

            var c = GetComponent<CarController>();
            if (c != null) c.ShowResetBanner();
        }
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
        relDeg = 0f;
        autoCenterTimer = 0f;
    }

    void Update()
    {
        float W = Screen.width, H = Screen.height;
        brakeZone    = new Rect(W * 0.04f, 0f, W * 0.36f, H * 0.65f);
        throttleZone = new Rect(W * 0.60f, 0f, W * 0.36f, H * 0.65f);

        float dt = Time.deltaTime;
        steerTarget = 0f;
        float thrTarget = 0f, brkTarget = 0f;

        var acc = Accelerometer.current;
        if (acc != null)
        {
            Vector3 a = acc.acceleration.ReadValue();
            lastAcc = a;
            Vector2 g2 = new Vector2(a.x, a.y);

            // Low-pass filter the gravity vector (exponential, tiltFilter ≈ 8/s)
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
                    ? Mathf.DeltaAngle(offsetDeg, rollDeg) * (invertTilt ? -1f : 1f)
                    : 0f;

                // ── Auto-center: drift offsetDeg toward current roll ──────────
                if (autoCenter && calibrated)
                {
                    bool condSpeed = ctrl != null && ctrl.SpeedKmh > 100f;
                    bool condLatG  = ctrl != null && Mathf.Abs(ctrl.LatG) < 0.3f;
                    bool condTilt  = Mathf.Abs(relDeg) < autoCenterWindowDeg;

                    if (condSpeed && condLatG && condTilt)
                    {
                        autoCenterTimer += dt;
                        if (autoCenterTimer >= 1.5f)
                        {
                            offsetDeg = Mathf.MoveTowards(offsetDeg, rollDeg, autoCenterRate * dt);
                            // Recompute relDeg after offset update
                            relDeg = Mathf.DeltaAngle(offsetDeg, rollDeg) * (invertTilt ? -1f : 1f);
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
                // Decay relative tilt toward 0 at 120 deg/s instead of freezing
                relDeg = Mathf.MoveTowards(relDeg, 0f, 120f * dt);
            }

            if (useTilt && calibrated)
            {
                float absRel = Mathf.Abs(relDeg);
                if (absRel <= deadzoneDeg)
                {
                    steerTarget = 0f;
                }
                else
                {
                    float span = Mathf.Max(0.001f, maxTiltDeg - deadzoneDeg);
                    float t = Mathf.Clamp(Mathf.Sign(relDeg) * (absRel - deadzoneDeg) / span, -1f, 1f);
                    steerTarget = Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), steerExpo);
                }
            }
        }

        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var touch in ts.touches)
            {
                if (!touch.press.isPressed) continue;
                Vector2 p = touch.position.ReadValue();
                if (throttleZone.Contains(p)) thrTarget = 1f;
                else if (brakeZone.Contains(p)) brkTarget = 1f;
            }
        }

        var kb = Keyboard.current; // editor fallback
        if (kb != null)
        {
            if (kb.dKey.isPressed) steerTarget =  1f;
            if (kb.aKey.isPressed) steerTarget = -1f;
            if (kb.wKey.isPressed) thrTarget = 1f;
            if (kb.sKey.isPressed) brkTarget = 1f;
        }

        Steer    = Mathf.Lerp(Steer,    steerTarget, 1f - Mathf.Exp(-steerSmoothing * dt));
        Throttle = Mathf.MoveTowards(Throttle, thrTarget, (thrTarget > Throttle ? 4f : 8f) * dt);
        Brake    = Mathf.MoveTowards(Brake,    brkTarget, (brkTarget > Brake    ? 6f : 10f) * dt);
    }

    void OnGUI()
    {
        float s = (Screen.dpi > 0 ? Screen.dpi : 320f) / 160f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        // Calibrate button: centred at top
        if (GUI.Button(new Rect(Screen.width / s * 0.5f - 60f, 10f, 120f, 40f), "Calibrate"))
            Calibrate();

        // Sensitivity buttons [Sens -] and [Sens +] next to Calibrate button
        float sensBtnY = 10f;
        float sensBtnH = 40f;
        float centerBtnX = Screen.width / s * 0.5f;
        if (GUI.Button(new Rect(centerBtnX - 145f, sensBtnY, 75f, sensBtnH), "Sens -"))
            maxTiltDeg = Mathf.Clamp(maxTiltDeg - 5f, 25f, 55f);
        if (GUI.Button(new Rect(centerBtnX + 70f, sensBtnY, 75f, sensBtnH), "Sens +"))
            maxTiltDeg = Mathf.Clamp(maxTiltDeg + 5f, 25f, 55f);

        // Tilt debug line: shown below the main HUD panel (CarController occupies top-left)
        // Positioned 170 dp from top to avoid overlap with the 5-row panel (~150 dp tall)
        string stateText = isTiltValid ? "VALID" : "TILT INVALID";
        string calibText = calibrated ? "" : $"  HOLD LEVEL... {Mathf.Max(0f, calibrateAt - Time.time):0.0}s";
        GUI.Label(new Rect(8f, 170f, 520f, 50f),
            $"acc x {lastAcc.x:0.00} y {lastAcc.y:0.00} z {lastAcc.z:0.00}  plane {gravXY.magnitude:0.00} [{stateText}]\n" +
            $"roll {rollDeg:0.0}°  tilt-steer {relDeg:0.0}°  Sens {maxTiltDeg:0}°{calibText}");

        if (showZones)
        {
            GUI.Box(ToGui(brakeZone, s),    "BRAKE");
            GUI.Box(ToGui(throttleZone, s), "THROTTLE");
        }
    }

    Rect ToGui(Rect z, float s) =>
        new Rect(z.x / s, (Screen.height - z.yMax) / s, z.width / s, z.height / s);
}
