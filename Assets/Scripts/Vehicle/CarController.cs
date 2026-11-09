using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using F1Sim.Simulation;

[RequireComponent(typeof(CarInput))]
public class CarController : MonoBehaviour
{
    public CarParams config = new CarParams();

    // ── Properties exposed to CarInput for auto-center / aero toggle ──────
    public float SpeedKmh => model.TotalSpeedKmh;
    public float LatG     => model.LatG;
    public float YawRate  => model.YawRate;

    // ── HUD brake-bias adjustment (degrees per click) ─────────────────────
    [Header("HUD")]
    public float brakeBiasStep = 0.01f;  // per button press

    CarInput        input;
    VehicleDynamics model;
    Telemetry       telemetry;
    Vector3 prevPos, currPos;
    float   prevYaw, currYaw;
    float   groundY;
    Vector3    startPos;
    Quaternion startRot;
    float   startYaw;
    float   autoResetTimer;

    // ── Version reset banner timer ───────────────────────────────────────
    float resetBannerTimer;

    // ── Profiler ──────────────────────────────────────────────────────────
    Stopwatch stepTimer = new Stopwatch();
    float     avgStepMs;
    const float kAlpha = 0.05f; // EMA smoothing

    // ── HUD styles ────────────────────────────────────────────────────────
    GUIStyle labelStyle;
    GUIStyle boxStyle;
    GUIStyle shiftStyle;
    GUIStyle bannerStyle;
    bool     stylesReady;

    // ── Telemetry save path ───────────────────────────────────────────────
    string lastSavePath = "";

    public void ShowResetBanner(float duration = 5.0f)
    {
        resetBannerTimer = duration;
    }

    void Awake()
    {
        input = GetComponent<CarInput>();

        // Parameter version check: reset config to code defaults if version mismatch
        if (config.paramsVersion != CarParams.CURRENT_VERSION)
        {
            var def = new CarParams { paramsVersion = CarParams.CURRENT_VERSION };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[CarController] Config version mismatch (found v{config.paramsVersion}, current v{CarParams.CURRENT_VERSION}). Resetting to defaults:");

            var fields = typeof(CarParams).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (f.Name == "paramsVersion") continue;
                object oldVal = f.GetValue(config);
                object newVal = f.GetValue(def);
                if (oldVal is Array oldArr && newVal is Array newArr)
                {
                    // Array comparison
                    bool match = oldArr.Length == newArr.Length;
                    if (match)
                    {
                        for (int i = 0; i < oldArr.Length; i++)
                            if (!Equals(oldArr.GetValue(i), newArr.GetValue(i))) { match = false; break; }
                    }
                    if (!match)
                        sb.AppendLine($"  {f.Name}: [{string.Join(", ", (float[])oldArr)}] -> [{string.Join(", ", (float[])newArr)}]");
                }
                else if (!Equals(oldVal, newVal))
                {
                    sb.AppendLine($"  {f.Name}: {oldVal} -> {newVal}");
                }
            }

            UnityEngine.Debug.LogWarning(sb.ToString());
            config = def;
            ShowResetBanner();
        }

        startPos = transform.position;
        startRot = transform.rotation;
        startYaw = transform.eulerAngles.y * Mathf.Deg2Rad;

        model = new VehicleDynamics(config)
        {
            X   = startPos.x,
            Z   = startPos.z,
            Yaw = startYaw
        };
        groundY = startPos.y;
        currPos = prevPos = startPos;
        currYaw = prevYaw = startYaw;

        telemetry = new Telemetry(120f, 300);
        telemetry.StartRecording();
    }

    public void ResetCar()
    {
        SaveTelemetry();
        model.Reset(startPos.x, startPos.z, startYaw);
        currPos = prevPos = startPos;
        currYaw = prevYaw = startYaw;
        transform.position = startPos;
        transform.rotation = startRot;
        autoResetTimer = 0f;
        avgStepMs = 0f;
        telemetry.StartRecording();
        if (input != null) input.ResetInput();
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
        prevPos = currPos;
        prevYaw = currYaw;

        stepTimer.Restart();
        model.Step(Time.fixedDeltaTime, input.Steer, input.Throttle, input.Brake);
        stepTimer.Stop();

        float ms = (float)stepTimer.Elapsed.TotalMilliseconds;
        avgStepMs = avgStepMs + kAlpha * (ms - avgStepMs);

        telemetry.Record(Time.time, model, input.Throttle, input.Brake);

        currPos = new Vector3(model.X, groundY, model.Z);
        currYaw = model.Yaw;

        // Auto-reset: speed < 3 km/h and |beta| > 1.2 rad for 3 s
        float totalSpeedKmh = model.TotalSpeedKmh;
        if (totalSpeedKmh < 3f && MathF.Abs(model.Beta) > 1.2f)
        {
            autoResetTimer += Time.fixedDeltaTime;
            if (autoResetTimer >= 3f) ResetCar();
        }
        else
        {
            autoResetTimer = 0f;
        }
    }

    void Update()
    {
        // R key → reset
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.rKey.wasPressedThisFrame)
                ResetCar();

            // X key → aero toggle
            if (kb.xKey.wasPressedThisFrame)
                ToggleAero();

            // [ ] keys → brake bias
            if (kb.leftBracketKey.wasPressedThisFrame)
                config.brakeBiasFront = Mathf.Clamp(config.brakeBiasFront - brakeBiasStep, 0.4f, 0.9f);
            if (kb.rightBracketKey.wasPressedThisFrame)
                config.brakeBiasFront = Mathf.Clamp(config.brakeBiasFront + brakeBiasStep, 0.4f, 0.9f);
        }

        // Render-time interpolation
        float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        transform.position = Vector3.Lerp(prevPos, currPos, t);
        float yawDeg = Mathf.LerpAngle(prevYaw * Mathf.Rad2Deg, currYaw * Mathf.Rad2Deg, t);
        transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
    }

    void ToggleAero()
    {
        model.Aero.CurrentMode = model.Aero.CurrentMode == AeroMode.Corner
            ? AeroMode.Straight
            : AeroMode.Corner;
    }

    // ── HUD ───────────────────────────────────────────────────────────────

    void EnsureStyles()
    {
        if (stylesReady) return;

        var bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
        bgTex.Apply();

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            padding   = new RectOffset(8, 8, 6, 6),
            wordWrap  = false,
            alignment = TextAnchor.UpperLeft,
            normal    = { background = bgTex }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            wordWrap = false,
            richText = true,
            normal   = { textColor = Color.white }
        };

        shiftStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.red }
        };

        var bannerTex = new Texture2D(1, 1);
        bannerTex.SetPixel(0, 0, new Color(0.8f, 0.1f, 0.1f, 0.85f));
        bannerTex.Apply();

        bannerStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize  = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { background = bannerTex, textColor = Color.yellow }
        };

        stylesReady = true;
    }

    void OnGUI()
    {
        float dpi = Screen.dpi > 0f ? Screen.dpi : 320f;
        float s   = dpi / 160f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        float sw = Screen.width  / s;
        float sh = Screen.height / s;

        EnsureStyles();

        // ── Top-right buttons (Calibrate removed here to keep single top-center button in CarInput) ────
        const float btnW = 100f, btnH = 40f, btnGap = 8f;
        float btnTop = 10f;

        if (GUI.Button(new Rect(sw - btnW - btnGap, btnTop, btnW, btnH), "Reset"))
            ResetCar();
        if (GUI.Button(new Rect(sw - 2f * (btnW + btnGap), btnTop, btnW, btnH), "Save Log"))
            SaveTelemetry();

        // Parameter version badge in top-right below buttons
        GUI.Label(new Rect(sw - 90f, btnTop + btnH + 4f, 85f, 20f),
            $"<color=#aaaaaa>v{config.paramsVersion}</color>", labelStyle);

        // ── Reset banner notification (5s display) ─────────────────────────
        if (resetBannerTimer > 0f)
        {
            resetBannerTimer -= Time.deltaTime;
            GUI.Box(new Rect(sw * 0.5f - 160f, 60f, 320f, 40f),
                $"PARAMS RESET v{config.paramsVersion}", bannerStyle);
        }

        // ── Right-center aero toggle ───────────────────────────────────────
        string aeroLabel = model.Aero.CurrentMode == AeroMode.Corner ? "CORNER\nAERO" : "STRAIGHT\nAERO";
        if (GUI.Button(new Rect(sw - 110f, sh * 0.5f - 50f, 100f, 70f), aeroLabel))
            ToggleAero();

        // ── Brake bias buttons ─────────────────────────────────────────────
        if (GUI.Button(new Rect(sw - 110f, sh * 0.5f + 30f, 45f, 35f), "B-"))
            config.brakeBiasFront = Mathf.Clamp(config.brakeBiasFront - brakeBiasStep, 0.4f, 0.9f);
        if (GUI.Button(new Rect(sw - 60f,  sh * 0.5f + 30f, 50f, 35f), "B+"))
            config.brakeBiasFront = Mathf.Clamp(config.brakeBiasFront + brakeBiasStep, 0.4f, 0.9f);

        // ── Main telemetry panel (top-left) ───────────────────────────────
        const float panelX = 8f;
        const float panelY = 8f;
        const float panelW = 340f;
        const float rowH   = 24f;
        const float padV   = 6f;

        string aeroStr  = model.Aero.CurrentMode == AeroMode.Corner ? "CRN" : "STR";
        string shiftStr = model.IsShifting ? " ⚡" : "";
        string assistStr = config.stabilityAssist > 0f ? "ON" : "OFF";

        string[] rows =
        {
            $"<b>Speed</b> {model.TotalSpeedKmh:0} km/h   Vx {model.VxKmh:0} km/h",
            $"Lat G {model.LatG:0.00}   beta {model.BetaDeg:0.0}°",
            $"Steer {input.Steer:0.00}  Thr {input.Throttle:0.00}  Brk {input.Brake:0.00}",
            $"steerAngle {model.FinalSteerAngleDeg:0.0}°  assist {model.AssistDeg:0.0}° ({assistStr})",
            $"rear trac {model.RearTractionUse:0.00}  lat scale {model.RearLateralGripScale:0.00}",
            $"<b>Gear {model.Gear}</b>  RPM {model.EngineRpm:0}{shiftStr}  Aero {aeroStr}",
            $"Brake bias {config.brakeBiasFront:0.00}  PkDecel {model.lastBrakingPeakDecelG:0.00}g  step {avgStepMs:0.00}ms",
        };

        float panelH = rows.Length * rowH + 2f * padV;
        GUI.Box(new Rect(panelX, panelY, panelW, panelH), GUIContent.none, boxStyle);
        for (int i = 0; i < rows.Length; i++)
        {
            GUI.Label(new Rect(panelX + 8f, panelY + padV + i * rowH, panelW - 16f, rowH),
                rows[i], labelStyle);
        }

        // ── Gear / shift-light bar ─────────────────────────────────────────
        DrawGearBar(sw, sh, s);

        // ── Shift flash ───────────────────────────────────────────────────
        if (model.EngineRpm > config.redlineRpm * 0.93f)
        {
            GUI.Label(new Rect(sw * 0.5f - 60f, sh - 80f, 120f, 50f), "SHIFT!", shiftStyle);
        }
    }

    void DrawGearBar(float sw, float sh, float s)
    {
        // Small RPM bar centred at top
        float barW   = 200f;
        float barH   = 10f;
        float barX   = sw * 0.5f - barW * 0.5f;
        float barY   = sh - 55f;
        float fill   = Mathf.Clamp01(model.EngineRpm / config.redlineRpm);

        // Background
        GUI.Box(new Rect(barX - 2f, barY - 2f, barW + 4f, barH + 4f), GUIContent.none);

        // Fill color: green → yellow → red
        Color barColor;
        if (fill < 0.7f)       barColor = Color.green;
        else if (fill < 0.9f)  barColor = Color.yellow;
        else                   barColor = Color.red;

        var oldColor = GUI.color;
        GUI.color = barColor;
        GUI.DrawTexture(new Rect(barX, barY, barW * fill, barH), Texture2D.whiteTexture);
        GUI.color = oldColor;

        // Gear label
        GUI.Label(new Rect(barX + barW + 8f, barY - 8f, 40f, 30f),
            $"<b>G{model.Gear}</b>", labelStyle);
    }
}
