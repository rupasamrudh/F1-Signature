// TestRunnerComponent.cs — In-scene test runner. Scripted maneuvers with HUD buttons.
// Attached to the same GameObject as CarController.
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using F1Sim.Simulation;

[RequireComponent(typeof(CarController))]
public class TestRunnerComponent : MonoBehaviour
{
    [Header("Test targets")]
    public float skidpadRadius       = 50f;
    public float skidpadTargetLatGMin = 2.5f;
    public float skidpadTargetLatGMax = 3.5f;
    public float accel100TargetMin   = 2.5f;
    public float accel100TargetMax   = 3.5f;
    public float accel200TargetMin   = 5.0f;
    public float accel200TargetMax   = 7.0f;
    public float brake300DistMin     = 100f;
    public float brake300DistMax     = 150f;
    public float brake300TimeMin     = 3.5f;
    public float brake300TimeMax     = 5.5f;
    public float brake200TimeMin     = 1.5f;
    public float brake200TimeMax     = 2.6f;
    public float stepSteerMaxOvershoot = 0.25f;

    CarController ctrl;
    CarInput      inp;
    bool          isRunning;
    string        resultLog = "Press a test button to run.";

    void Awake()
    {
        ctrl = GetComponent<CarController>();
        inp  = GetComponent<CarInput>();
    }

    // ── Coroutine helpers ─────────────────────────────────────────────────

    IEnumerator RunSkidpad()
    {
        isRunning = true;
        resultLog = "Skidpad: running...";

        // Approach: command a constant steer input that produces 50 m radius,
        // ramp speed until car can't hold radius.
        // For safety we scripted-override the input using a simplified approach:
        // We override input.Steer and input.Throttle directly.

        // Reset first
        ctrl.ResetCar();
        yield return new WaitForSeconds(0.5f);

        float peakLatG = 0f;
        float speed    = 0f;

        // Ramp throttle while holding constant steer
        for (float t = 0f; t < 25f; t += Time.fixedDeltaTime)
        {
            inp.Steer    = 0.4f;  // right turn
            inp.Throttle = Mathf.Clamp01(t / 10f);
            inp.Brake    = 0f;
            float latG = Mathf.Abs(ctrl.LatG);
            if (latG > peakLatG) peakLatG = latG;
            speed = ctrl.SpeedKmh;
            if (Mathf.Abs(ctrl.LatG) > 4.5f || ctrl.SpeedKmh > 200f) break;
            yield return new WaitForFixedUpdate();
        }

        inp.Steer = 0f; inp.Throttle = 0f;

        string pass = (peakLatG >= skidpadTargetLatGMin && peakLatG <= skidpadTargetLatGMax)
            ? "PASS" : "FAIL";
        resultLog = $"Skidpad: peak LatG = {peakLatG:0.00} g  [{pass}] " +
                    $"(target {skidpadTargetLatGMin}-{skidpadTargetLatGMax} g)";
        isRunning = false;
        ctrl.ResetCar();
    }

    IEnumerator RunAccel()
    {
        isRunning = true;
        resultLog = "Accel: running...";
        ctrl.ResetCar();
        yield return new WaitForSeconds(0.5f);

        float t100 = -1f, t200 = -1f, elapsed = 0f;
        bool  hit100 = false, hit200 = false;

        while (elapsed < 30f)
        {
            inp.Steer    = 0f;
            inp.Throttle = 1f;
            inp.Brake    = 0f;
            elapsed += Time.fixedDeltaTime;

            float spd = ctrl.SpeedKmh;
            if (!hit100 && spd >= 100f) { t100 = elapsed; hit100 = true; }
            if (!hit200 && spd >= 200f) { t200 = elapsed; hit200 = true; break; }
            yield return new WaitForFixedUpdate();
        }

        inp.Throttle = 0f;
        string p100 = (t100 >= accel100TargetMin && t100 <= accel100TargetMax) ? "PASS" : "FAIL";
        string p200 = (t200 >= accel200TargetMin && t200 <= accel200TargetMax) ? "PASS" : "FAIL";
        resultLog = $"Accel: 0-100={t100:0.00}s [{p100}]  0-200={t200:0.00}s [{p200}]";
        isRunning = false;
        ctrl.ResetCar();
    }

    IEnumerator RunBraking(float startSpeedKmh)
    {
        isRunning = true;
        resultLog = $"Braking from {startSpeedKmh:0} km/h: running...";
        ctrl.ResetCar();
        yield return new WaitForSeconds(0.5f);

        // Accelerate to target speed
        while (ctrl.SpeedKmh < startSpeedKmh - 2f)
        {
            inp.Throttle = 1f; inp.Brake = 0f; inp.Steer = 0f;
            yield return new WaitForFixedUpdate();
        }
        inp.Throttle = 0f;

        // Record start position for distance
        Vector3 startPos = transform.position;
        float   elapsed  = 0f;

        while (ctrl.SpeedKmh > 2f && elapsed < 15f)
        {
            inp.Brake = 1f; inp.Steer = 0f; inp.Throttle = 0f;
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        inp.Brake = 0f;
        float dist = Vector3.Distance(transform.position, startPos);

        string result;
        if (startSpeedKmh >= 290f)
        {
            string pd = (dist >= brake300DistMin && dist <= brake300DistMax) ? "PASS" : "FAIL";
            string pt = (elapsed >= brake300TimeMin && elapsed <= brake300TimeMax) ? "PASS" : "FAIL";
            result = $"Brake 300→0: dist={dist:0}m [{pd}]  time={elapsed:0.0}s [{pt}]";
        }
        else
        {
            string pt = (elapsed >= brake200TimeMin && elapsed <= brake200TimeMax) ? "PASS" : "FAIL";
            result = $"Brake 200→0: time={elapsed:0.0}s [{pt}]  dist={dist:0}m";
        }
        resultLog = result;
        isRunning = false;
        ctrl.ResetCar();
    }

    IEnumerator RunStepSteer()
    {
        isRunning = true;
        resultLog = "Step steer: running...";
        ctrl.ResetCar();
        yield return new WaitForSeconds(0.5f);

        // Accelerate to 150 km/h
        while (ctrl.SpeedKmh < 148f)
        {
            inp.Throttle = 1f; inp.Brake = 0f; inp.Steer = 0f;
            yield return new WaitForFixedUpdate();
        }
        inp.Throttle = 0.3f; // maintain speed

        // Apply step steer: 0.5 lock right
        float peakYr = 0f;
        float finalYr = 0f;
        float t10 = -1f, t90 = -1f;

        var yrList = new System.Collections.Generic.List<float>();
        for (float t = 0f; t < 3.0f; t += Time.fixedDeltaTime)
        {
            inp.Steer = 0.5f;
            float yr = Mathf.Abs(ctrl.YawRate);
            yrList.Add(yr);
            if (yr > peakYr) peakYr = yr;
            if (t > 2.0f) finalYr = yr; // steady state
            yield return new WaitForFixedUpdate();
        }

        // Calculate 10-90% rise time
        float yr10 = finalYr * 0.10f;
        float yr90 = finalYr * 0.90f;
        for (int i = 0; i < yrList.Count; i++)
        {
            float t = i * Time.fixedDeltaTime;
            if (t10 < 0f && yrList[i] >= yr10) t10 = t;
            if (t90 < 0f && yrList[i] >= yr90) { t90 = t; break; }
        }
        float riseTime = (t10 >= 0f && t90 >= 0f) ? (t90 - t10) : 0f;

        // Evaluate overshoot
        float overshootPct = finalYr > 0.05f ? ((peakYr - finalYr) / finalYr) * 100f : 0f;
        string pass = (overshootPct <= stepSteerMaxOvershoot * 100f) ? "PASS" : "FAIL";

        inp.Steer = 0f; inp.Throttle = 0f;
        resultLog = $"Step steer: Rise={riseTime:0.000}s, Peak={peakYr:0.2} rad/s, SS={finalYr:0.2} rad/s, Overshoot={overshootPct:0.1}% [{pass}] (target < {stepSteerMaxOvershoot*100:0}%)";
        isRunning = false;
        ctrl.ResetCar();
    }

    IEnumerator RunAllTests()
    {
        isRunning = true;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Running All Tests ===");

        // Test 1: Skidpad
        resultLog = "Running 1/5: Skidpad...";
        yield return StartCoroutine(RunSkidpad());
        sb.AppendLine(resultLog);

        // Test 2: Accel
        resultLog = "Running 2/5: Accel...";
        yield return StartCoroutine(RunAccel());
        sb.AppendLine(resultLog);

        // Test 3: Brake 300
        resultLog = "Running 3/5: Brake 300...";
        yield return StartCoroutine(RunBraking(300f));
        sb.AppendLine(resultLog);

        // Test 4: Brake 200
        resultLog = "Running 4/5: Brake 200...";
        yield return StartCoroutine(RunBraking(200f));
        sb.AppendLine(resultLog);

        // Test 5: Step steer
        resultLog = "Running 5/5: Step Steer...";
        yield return StartCoroutine(RunStepSteer());
        sb.AppendLine(resultLog);

        resultLog = sb.ToString();
        isRunning = false;
    }

    // ── HUD ───────────────────────────────────────────────────────────────
    void OnGUI()
    {
        float s   = (Screen.dpi > 0 ? Screen.dpi : 320f) / 160f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        float sw = Screen.width  / s;
        float sh = Screen.height / s;

        // Result panel at bottom
        float panelW = sw - 20f;
        GUI.Box(new Rect(10f, sh - 120f, panelW, 110f), "Test Results");
        GUI.Label(new Rect(15f, sh - 105f, panelW - 10f, 100f), resultLog);

        // Test buttons across the bottom
        float bw = 110f, bh = 38f, by = sh - 165f, bx = 10f, gap = 6f;
        if (!isRunning)
        {
            if (GUI.Button(new Rect(bx, by, bw, bh), "Run All Tests"))
                StartCoroutine(RunAllTests());
            bx += bw + gap;
            if (GUI.Button(new Rect(bx, by, bw, bh), "Skidpad"))
                StartCoroutine(RunSkidpad());
            bx += bw + gap;
            if (GUI.Button(new Rect(bx, by, bw, bh), "0-200 Accel"))
                StartCoroutine(RunAccel());
            bx += bw + gap;
            if (GUI.Button(new Rect(bx, by, bw, bh), "Brake 300→0"))
                StartCoroutine(RunBraking(300f));
            bx += bw + gap;
            if (GUI.Button(new Rect(bx, by, bw, bh), "Brake 200→0"))
                StartCoroutine(RunBraking(200f));
            bx += bw + gap;
            if (GUI.Button(new Rect(bx, by, bw, bh), "Step Steer"))
                StartCoroutine(RunStepSteer());
        }
        else
        {
            GUI.Label(new Rect(10f, by, 300f, bh), "<color=yellow>⏳ Test running...</color>");
        }
    }
}
