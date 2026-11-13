using System;

namespace F1Sim.Simulation
{
    [Serializable]
    public class CarParams
    {
        // ── Parameter versioning ───────────────────────────────────────────
        public const int CURRENT_VERSION = 12;
        public int paramsVersion;

        // ── Chassis (2026-style: 768 kg, 3.4 m wheelbase) ─────────────────
        public float mass            = 768f;
        public float wheelbase       = 3.4f;
        public float frontWeightFrac = 0.455f;
        public float cgHeight        = 0.28f;
        public float yawInertia      = 2800f;   // tuned M2 stabilisation: 0.10-0.25s yaw rise time, overshoot < 25%
        public float maxSteerAngle   = 0.15f;   // rad (~8.59 deg) 90-degree corner race lock (steer is at most 90 deg at wheel)
        public float steerFalloffSpeed = 30f;   // m/s, legacy speed falloff

        // ── 4-Wheel geometry (M2) ──────────────────────────────────────────
        public float trackWidthF        = 1.60f;  // m front track
        public float trackWidthR        = 1.55f;  // m rear track
        public float rollStiffnessFront = 0.55f;  // fraction of roll stiffness at front axle
        public float ackermannFrac      = 0.5f;   // 0 = parallel, 1 = full Ackermann
        public float minWheelLoad       = 150f;   // N minimum per-wheel load

        // ── Grip-limited steering lock ─────────────────────────────────────
        public float steerLimitScale = 1.65f; // Enhanced cornering authority to carry speed through medium & high-speed corners

        // ── Dynamic Cornering Steering (Decel/Accel modulation) ─────────
        public float decelTurnGain      = 0.35f; // Extra turn authority while decelerating (turns more and more to tuck into apex)
        public float accelTurnReduction = 0.38f; // Steering unwinding while accelerating (turns less and less to carry exit speed)

        // ── Steering rate limit ────────────────────────────────────────────
        public float steerRateMax   = 2.8f;   // rad/s at 0 m/s (~160 deg/s smooth progressive turn-in)
        public float steerRateMin   = 1.4f;   // rad/s at 80 m/s (~80 deg/s high-speed cornering stability)
        public float steerRateSpeed = 80f;    // m/s

        // ── Stability assist ───────────────────────────────────────────────
        public float stabilityAssist = 0.65f;  // Smooth, refined counter-steer assist (eliminates sharp snaps)
        public float kBeta           = 0.55f;  // Sideslip angle damping
        public float kYaw            = 0.035f; // Yaw rate error damping
        public float maxAssistAngle  = 0.055f; // rad (~3.15 deg max counter-steer angle)
        public float assistFilterRate = 12.0f; // s^-1: smooth exponential damping for buttery counter-steer

        // ── Traction and braking limits ────────────────────────────────────
        public float tractionFrac        = 0.68f;  // rear drive force cap (tuned M3: 0-100 in 2.78s, 0-200 in 5.04s)
        public bool  tcForceLimitFallback = true;   // force limit active by default
        public float tcSlipTarget        = 0.12f;  // target rear slip ratio before TC cut
        public float tcSlipRange         = 0.08f;  // smoothing range above target
        public float rearLateralReserve  = 0.80f;  // cap rear combined usage so lateral grip scale never drops below 80% (prevents snap oversteer)
        public float diffLockFrac        = 0.40f;  // LSD extra torque transfer to slower wheel
        public float wheelInertia        = 1.2f;   // kg*m^2 per wheel

        public float absFrac          = 0.90f;  // front axle brake force cap (tuned M2)
        public float rearAbsFrac      = 0.565f; // rear axle brake force cap (tuned M2)
        public float brakeBiasFront   = 0.68f;  // front brake bias fraction
        public float brakeCornerCut   = 0.40f;  // brake reduction during cornering
        public float lateralGripFloor = 0.55f;  // friction-ellipse lateral grip floor (allows strong steering authority while trail-braking)

        // ── Tire slip relaxation ───────────────────────────────────────────
        public float relaxLength = 0.4f;  // m

        // ── Low-speed kinematic blend ──────────────────────────────────────
        public float vBlend = 8.0f;  // m/s

        // ── Spin recovery damping ──────────────────────────────────────────
        public float spinBetaThreshold = 0.10f;   // rad (~5.7 deg, catches oversteer early to prevent spin-outs)
        public float spinYawDamping    = 4500f;   // N*m*s/rad (firm anti-spin damping to keep rear planted)

        // ── Powertrain M1 legacy (kept for BicycleModel compatibility) ─────
        public float powerW       = 650000f;
        public float maxDriveForce = 30000f;

        // ── Powertrain M2: ICE + gearbox ──────────────────────────────────
        public float drivetrainEff  = 0.93f;
        public float finalDrive     = 8.0f;
        public float tireRadius     = 0.330f;   // m (rolling radius)
        public float idleRpm        = 4000f;
        public float redlineRpm     = 15000f;
        public float shiftTime      = 0.025f;   // s (25 ms torque cut)
        public float downshiftFrac  = 0.42f;    // fraction of redline to trigger downshift
        public float electricDeployW = 350000f; // W flat electric deploy (2026 MGU-K limit)
        public float electricTaperStartKmh = 290f; // km/h start taper
        public float electricTaperEndKmh   = 345f; // km/h end taper

        // Gear ratios G1..G8 (finalDrive=8.0, 8th=0.64 -> power-limited top speed ~340 km/h with 6.7% redline margin)
        public float[] gearRatios = { 3.07f, 2.14f, 1.63f, 1.30f, 1.07f, 0.90f, 0.76f, 0.64f };

        // ── Brakes ─────────────────────────────────────────────────────────
        /// Total brake force demand across all four wheels at brake = 1.0 (N).
        /// The BrakeModel splits this force by brakeBiasFront and applies per-wheel ABS caps.
        public float totalBrakeForceN = 25200f;
        public float maxBrakeForce { get => totalBrakeForceN; set => totalBrakeForceN = value; }

        // ── Aero (M1 legacy, kept for BicycleModel) ───────────────────────
        public float clA          = 4.0f;
        public float cdA          = 1.1f;
        public float aeroFrontFrac = 0.45f;
        public float rho           = 1.225f;
        public float rollingRes    = 0.015f;

        // ── Aero M2: Corner / Straight modes ──────────────────────────────
        public float aeroCornerClA   = 4.0f;    // high downforce (corner)
        public float aeroCornerCdA   = 1.15f;
        public float aeroStraightClA = 2.3f;    // low drag (straight)
        public float aeroStraightCdA = 0.72f;
        public float aeroBlendTime   = 0.15f;   // s to fully switch modes

        // ── Tire (Pacejka lateral, per wheel) ─────────────────────────────
        public float mu0              = 2.30f;   // tuned M2 stabilisation: 2.5-3.5 g skidpad with yawInertia=2800
        public float rearTireGripMult = 1.10f;   // 405mm wide rear tire contact patch grip advantage (+10% rear traction)
        public float fz0              = 4000f;
        public float loadSens = 0.05f;
        public float pacB     = 9.0f;    // tuned M2 stabilisation: lateral stiffness for 0.10-0.25s rise time (0.100s)
        public float pacC     = 1.6f;
        public float pacE     = 0.95f;

        // ── Tire (Pacejka longitudinal) ────────────────────────────────────
        public float pacBx = 12f;
        public float pacCx = 1.65f;
        public float pacEx = 0.90f;
    }

    /// Dynamic bicycle model. Body frame: x forward, y left, yaw rate R positive = turning left.
    /// Input steer: +1 = right. World: Unity-style (X right, Z forward), Yaw clockwise from +Z.
    public class BicycleModel
    {
        public readonly CarParams P;
        public float X, Z, Yaw;
        public float Vx, Vy, R;
        public float Ax;              // filtered longitudinal accel (m/s^2), used for load transfer
        public float ayBody;          // measured body lateral acceleration (m/s^2)
        public float fyBodyReal;      // real total lateral force on body (N)
        public float steerAngle;      // driver steering angle after rate limiting (rad, left-positive)
        public float finalSteerAngle; // final front wheel angle INCLUDING assist (rad, left-positive)
        public float alphaLagF;       // lagged slip angle front (rad)
        public float alphaLagR;       // lagged slip angle rear (rad)

        // Real lateral acceleration = fyBody / (mass * 9.81)
        public float LatG => fyBodyReal / (P.mass * 9.81f);
        public float TotalSpeedKmh => MathF.Sqrt(Vx * Vx + Vy * Vy) * 3.6f;
        public float SpeedKmh => TotalSpeedKmh;
        public float VxKmh => Vx * 3.6f;
        public float Beta => MathF.Atan2(Vy, MathF.Max(Vx, 5f));
        public float FinalSteerAngleDeg => finalSteerAngle * (180f / MathF.PI);
        public float SteerAngleDeg => FinalSteerAngleDeg;
        public float DriverSteerAngleDeg => steerAngle * (180f / MathF.PI);
        public float BetaDeg => Beta * (180f / MathF.PI);
        public float DeltaAssist { get; private set; }
        public float AssistDeg => DeltaAssist * (180f / MathF.PI);
        public float RearTractionUse { get; private set; }
        public float RearLateralGripScale { get; private set; }

        public BicycleModel(CarParams p) { P = p; }

        public void Reset(float x = 0f, float z = 0f, float yaw = 0f)
        {
            X = x;
            Z = z;
            Yaw = yaw;
            Vx = 0f;
            Vy = 0f;
            R = 0f;
            Ax = 0f;
            ayBody = 0f;
            fyBodyReal = 0f;
            steerAngle = 0f;
            finalSteerAngle = 0f;
            alphaLagF = 0f;
            alphaLagR = 0f;
            DeltaAssist = 0f;
            RearTractionUse = 0f;
            RearLateralGripScale = 1f;
        }

        float Mu(float fz) =>
            MathF.Max(0.6f, P.mu0 * (1f - P.loadSens * (fz - P.fz0) / P.fz0));

        float Pacejka(float x, float d)
        {
            float bx = P.pacB * x;
            return d * MathF.Sin(P.pacC * MathF.Atan(bx - P.pacE * (bx - MathF.Atan(bx))));
        }

        float Ellipse(float fx, float fxMax)
        {
            float k = fx / fxMax;
            float scale = MathF.Sqrt(MathF.Max(0f, 1f - k * k));
            return MathF.Max(scale, P.lateralGripFloor);
        }

        public void Step(float dt, float steer, float throttle, float brake)
        {
            const float g = 9.81f;
            float L = P.wheelbase;
            float a = L * (1f - P.frontWeightFrac); // CG to front axle
            float b = L * P.frontWeightFrac;        // CG to rear axle

            float speed = MathF.Max(MathF.Sqrt(Vx * Vx + Vy * Vy), 0.5f);

            // Aero
            float q = 0.5f * P.rho * Vx * Vx;
            float down = q * P.clA;
            float drag = q * P.cdA;

            // Axle loads: static + aero + longitudinal weight transfer
            float fzF = P.mass * g * b / L + down * P.aeroFrontFrac - P.mass * Ax * P.cgHeight / L;
            float fzR = P.mass * g * a / L + down * (1f - P.aeroFrontFrac) + P.mass * Ax * P.cgHeight / L;
            fzF = MathF.Max(fzF, 200f);
            fzR = MathF.Max(fzR, 200f);

            float muF = Mu(fzF), muR = Mu(fzR);
            float fxMaxF = muF * fzF, fxMaxR = muR * fzR;

            // Grip-limited steering lock
            float ayMax = 0.85f * (muF * fzF + muR * fzR) / P.mass;
            float deltaLimit = MathF.Min(P.maxSteerAngle, (L * ayMax / MathF.Max(Vx * Vx, 25f)) * P.steerLimitScale);
            float deltaCmd = -steer * deltaLimit; // input steer: +1 = right, so -steer is left-positive

            // Steering rate limit: actual angle moves toward commanded angle at most steerRate rad/s
            float u = Math.Clamp(speed / P.steerRateSpeed, 0f, 1f);
            float steerRate = P.steerRateMax + (P.steerRateMin - P.steerRateMax) * u;
            float maxStep = steerRate * dt;
            float diff = deltaCmd - steerAngle;
            if (MathF.Abs(diff) <= maxStep)
                steerAngle = deltaCmd;
            else
                steerAngle += MathF.Sign(diff) * maxStep;

            // Stability assist: smooth counter-steer correction DeltaAssist
            float beta = MathF.Atan2(Vy, MathF.Max(Vx, 5f));
            float rRef = Vx * MathF.Tan(steerAngle) / L;
            float assistFade = Math.Clamp(speed / 3f, 0f, 1f);

            float betaEff = MathF.Abs(beta) > 0.02f ? beta : 0f;
            float yawErr  = (R - rRef);
            float yawEff  = MathF.Abs(yawErr) > 0.03f ? yawErr : 0f;

            float daTarget = P.stabilityAssist * (P.kBeta * betaEff - P.kYaw * yawEff) * assistFade;
            daTarget = Math.Clamp(daTarget, -P.maxAssistAngle, P.maxAssistAngle);
            DeltaAssist += (daTarget - DeltaAssist) * (1f - MathF.Exp(-P.assistFilterRate * dt));

            float delta = steerAngle + DeltaAssist;
            finalSteerAngle = delta;

            // Longitudinal demand: Brake-while-cornering coupling
            float cornerBrakeFactor = 1f - P.brakeCornerCut * Math.Clamp(MathF.Abs(ayBody) / MathF.Max(ayMax, 0.01f), 0f, 1f);
            float brakeTotal = brake * P.maxBrakeForce * cornerBrakeFactor;

            // Traction control force cap
            float driveF = MathF.Min(P.powerW * P.drivetrainEff / MathF.Max(Vx, 5f), P.maxDriveForce);
            float maxDriveGrip = P.tractionFrac * fxMaxR;
            float rearDrive = MathF.Min(throttle * driveF, maxDriveGrip);

            // Front and rear brake limits
            float brakeFrontDemand = brakeTotal * P.brakeBiasFront;
            float brakeRearDemand = brakeTotal * (1f - P.brakeBiasFront);
            float brakeFront = MathF.Min(brakeFrontDemand, P.absFrac * fxMaxF);
            float brakeRear = MathF.Min(brakeRearDemand, P.rearAbsFrac * fxMaxR);

            float fxF = -brakeFront;
            float fxR = rearDrive - brakeRear;
            fxF = Math.Clamp(fxF, -fxMaxF, fxMaxF);
            fxR = Math.Clamp(fxR, -fxMaxR, fxMaxR);

            RearTractionUse = MathF.Abs(fxR) / fxMaxR;
            RearLateralGripScale = Ellipse(fxR, fxMaxR);

            // Tire slip relaxation: lagged slip angle per axle
            float alphaTargetF = MathF.Atan2(Vy + a * R, speed) - delta;
            float alphaTargetR = MathF.Atan2(Vy - b * R, speed);

            float relaxFactor = 1f - MathF.Exp(-speed * dt / P.relaxLength);
            alphaLagF += (alphaTargetF - alphaLagF) * relaxFactor;
            alphaLagR += (alphaTargetR - alphaLagR) * relaxFactor;

            float fyF = -Pacejka(alphaLagF, muF * fzF) * Ellipse(fxF, fxMaxF);
            float fyR = -Pacejka(alphaLagR, muR * fzR) * RearLateralGripScale;

            // Resolve front tire forces into the body frame
            float cd = MathF.Cos(delta), sd = MathF.Sin(delta);
            float rolling = Vx > 0.1f ? P.rollingRes * P.mass * g : 0f;
            float fxBody = fxF * cd - fyF * sd + fxR - drag - rolling;
            float fyBody = fxF * sd + fyF * cd + fyR;
            float mz = a * (fxF * sd + fyF * cd) - b * fyR;

            // Real measured body lateral force and acceleration
            fyBodyReal = fyBody;
            ayBody = fyBody / P.mass;

            // Short yaw-rate damping term when |beta| > 0.5 rad (tunable)
            if (MathF.Abs(beta) > P.spinBetaThreshold)
            {
                mz -= P.spinYawDamping * R;
            }

            // Semi-implicit Euler: velocities first, then positions
            Vx += (fxBody / P.mass + Vy * R) * dt;
            Vy += (fyBody / P.mass - Vx * R) * dt;
            R += (mz / P.yawInertia) * dt;
            Ax += (fxBody / P.mass - Ax) * MathF.Min(1f, dt * 20f);

            if (Vx < 0f) Vx = 0f; // no reverse in M1

            // Low-speed stabilisation: blend toward kinematic steering (raised to 8 m/s)
            float vBlend = P.vBlend;
            if (Vx < vBlend)
            {
                float w = Vx / vBlend;
                float rKin = Vx * MathF.Tan(delta) / L;
                R = rKin + (R - rKin) * w;
                Vy *= w;
            }

            Yaw -= R * dt;
            float s = MathF.Sin(Yaw), c = MathF.Cos(Yaw);
            X += (Vx * s - Vy * c) * dt;
            Z += (Vx * c + Vy * s) * dt;
        }
    }
}
