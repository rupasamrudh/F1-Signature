// VehicleDynamics.cs — Pure C#, no UnityEngine. 4-wheel dynamic vehicle model.
// Body frame: x forward, y left, yaw rate R positive = turning left.
// Input steer: +1 = right. World: Unity-style (X right, Z forward), Yaw clockwise from +Z.
using System;

namespace F1Sim.Simulation
{
    public class VehicleDynamics
    {
        public readonly CarParams P;

        // World position and orientation
        public float X, Z, Yaw;

        // Body-frame velocities and yaw rate
        public float Vx, Vy, R;

        // Filtered longitudinal acceleration (for load transfer on next frame)
        public float Ax;

        // Measured lateral acceleration this frame
        public float ayBody;
        public float fyBodyReal;

        // Steering state
        public float steerAngle;      // driver commanded after rate limit (rad, left-positive)
        public float finalSteerAngle; // after stability assist

        // Per-wheel states (0=FL,1=FR,2=RL,3=RR)
        public readonly WheelState[] Wheels = new WheelState[4]
        {
            new WheelState(), new WheelState(), new WheelState(), new WheelState()
        };

        // Powertrain and aero
        public readonly PowertrainState PT = new PowertrainState();
        public readonly AeroState       Aero = new AeroState();

        // Telemetry / HUD exposures
        public float LatG              => fyBodyReal / (P.mass * 9.81f);
        public float YawRate           => R;
        public float TotalSpeedKmh     => MathF.Sqrt(Vx * Vx + Vy * Vy) * 3.6f;
        public float SpeedKmh          => TotalSpeedKmh;
        public float VxKmh             => Vx * 3.6f;
        public float Beta              => MathF.Atan2(Vy, MathF.Max(Vx, 5f));
        public float BetaDeg           => Beta * (180f / MathF.PI);
        public float FinalSteerAngleDeg => finalSteerAngle * (180f / MathF.PI);
        public float DriverSteerAngleDeg => steerAngle * (180f / MathF.PI);
        public float DeltaAssist        { get; private set; }
        public float AssistDeg          => DeltaAssist * (180f / MathF.PI);
        public float RearTractionUse    { get; private set; }
        public float RearLateralGripScale { get; private set; }
        public int   Gear               => PT.Gear;
        public float EngineRpm          => PT.EngineRpm;
        public bool  IsShifting         => PT.IsShifting;

        // Per-wheel vertical loads (for telemetry)
        public float FzFL, FzFR, FzRL, FzRR;

        // Split-mu scaling factors (1.0 default)
        public float muScaleRL = 1.0f;
        public float muScaleRR = 1.0f;

        // Braking performance metrics
        public float currentBrakingDecelG;
        public float lastBrakingPeakDecelG;
        public float initialBrakeFrontN;
        public float initialBrakeRearN;
        bool isBrakingActive;

        public VehicleDynamics(CarParams p) { P = p; }

        public void Reset(float x = 0f, float z = 0f, float yaw = 0f)
        {
            X = x; Z = z; Yaw = yaw;
            Vx = 0f; Vy = 0f; R = 0f; Ax = 0f; ayBody = 0f; fyBodyReal = 0f;
            steerAngle = 0f; finalSteerAngle = 0f; DeltaAssist = 0f;
            RearTractionUse = 0f; RearLateralGripScale = 1f;
            FzFL = FzFR = FzRL = FzRR = 0f;
            muScaleRL = 1.0f; muScaleRR = 1.0f;
            currentBrakingDecelG = 0f;
            lastBrakingPeakDecelG = 0f;
            initialBrakeFrontN = 0f;
            initialBrakeRearN = 0f;
            isBrakingActive = false;
            foreach (var w in Wheels) w.Reset();
            PT.Reset();
            Aero.Reset();
        }

        public void Step(float dt, float steer, float throttle, float brake)
        {
            const float g    = 9.81f;
            float L  = P.wheelbase;
            float a  = L * (1f - P.frontWeightFrac);  // CG to front axle
            float b  = L * P.frontWeightFrac;          // CG to rear axle
            float twF = P.trackWidthF * 0.5f;
            float twR = P.trackWidthR * 0.5f;

            float speed = MathF.Max(MathF.Sqrt(Vx * Vx + Vy * Vy), 0.5f);

            // ── Aero ──────────────────────────────────────────────────────────
            var (down, drag) = AeroModel.Step(P, Aero, Vx, dt);

            // ── Per-wheel vertical loads ───────────────────────────────────────
            // Static weight per axle
            float fzFStatic = P.mass * g * b / L;
            float fzRStatic = P.mass * g * a / L;

            // Aero downforce per axle
            float fzFAero = down * P.aeroFrontFrac;
            float fzRAero = down * (1f - P.aeroFrontFrac);

            // Longitudinal load transfer (pitch)
            float fzLongTransfer = P.mass * Ax * P.cgHeight / L;

            // Lateral load transfer (roll), split front/rear by roll stiffness fraction
            float fzLatTotal     = P.mass * ayBody * P.cgHeight;
            float fzLatFront     = fzLatTotal * P.rollStiffnessFront / P.trackWidthF;
            float fzLatRear      = fzLatTotal * (1f - P.rollStiffnessFront) / P.trackWidthR;

            // Compose axle loads
            float fzFAxle = fzFStatic + fzFAero - fzLongTransfer;
            float fzRAxle = fzRStatic + fzRAero + fzLongTransfer;

            // Per-wheel: lateral transfer on top (left = FL/RL loses load on left turn)
            FzFL = MathF.Max(fzFAxle * 0.5f - fzLatFront, P.minWheelLoad);
            FzFR = MathF.Max(fzFAxle * 0.5f + fzLatFront, P.minWheelLoad);
            FzRL = MathF.Max(fzRAxle * 0.5f - fzLatRear,  P.minWheelLoad);
            FzRR = MathF.Max(fzRAxle * 0.5f + fzLatRear,  P.minWheelLoad);

            // ── Friction coefficients per wheel ───────────────────────────────
            float muFL = TireModel.Mu(P, FzFL);
            float muFR = TireModel.Mu(P, FzFR);
            float muRL = TireModel.Mu(P, FzRL) * muScaleRL;
            float muRR = TireModel.Mu(P, FzRR) * muScaleRR;

            // ── Grip-limited steering lock ─────────────────────────────────────
            float fzFAxleC = FzFL + FzFR;
            float fzRAxleC = FzRL + FzRR;
            float muFavg   = TireModel.Mu(P, fzFAxleC * 0.5f);
            float muRavg   = TireModel.Mu(P, fzRAxleC * 0.5f);
            float ayMax    = 0.85f * (muFavg * fzFAxleC + muRavg * fzRAxleC) / P.mass;
            float deltaLimit = MathF.Min(P.maxSteerAngle,
                (L * ayMax / MathF.Max(Vx * Vx, 25f)) * P.steerLimitScale);
            float deltaCmd = -steer * deltaLimit; // +1 steer = right → negative left-positive delta

            // ── Steering rate limit ───────────────────────────────────────────
            float u         = Math.Clamp(speed / P.steerRateSpeed, 0f, 1f);
            float steerRate = P.steerRateMax + (P.steerRateMin - P.steerRateMax) * u;
            float maxStep   = steerRate * dt;
            float diff      = deltaCmd - steerAngle;
            if (MathF.Abs(diff) <= maxStep)
                steerAngle = deltaCmd;
            else
                steerAngle += MathF.Sign(diff) * maxStep;

            // ── Ackermann steering angles ─────────────────────────────────────
            // Simple Ackermann: for a given centre steer angle δ,
            // inner wheel steers more, outer less. Fraction 0=parallel, 1=full Ackermann.
            float deltaFL, deltaFR;
            {
                float R_turn = MathF.Abs(steerAngle) > 0.001f
                    ? L / MathF.Tan(MathF.Abs(steerAngle))
                    : 1e6f;
                float innerExtra = MathF.Atan(L / (R_turn - twF)) - MathF.Abs(steerAngle);
                float outerLess  = MathF.Abs(steerAngle) - MathF.Atan(L / (R_turn + twF));
                float ackDelta   = P.ackermannFrac * 0.5f * (innerExtra + outerLess);

                if (steerAngle >= 0f)                 // turning left
                {
                    deltaFL = steerAngle + ackDelta;  // inner (left) steers more
                    deltaFR = steerAngle - ackDelta;  // outer (right) steers less
                }
                else                                  // turning right
                {
                    deltaFL = steerAngle + ackDelta;  // outer (left) steers less (ackDelta ≤0 since steerAngle<0)
                    deltaFR = steerAngle - ackDelta;  // inner (right) steers more
                }
            }

            // ── Stability assist ──────────────────────────────────────────────
            float beta    = MathF.Atan2(Vy, MathF.Max(Vx, 5f));
            float rRef    = Vx * MathF.Tan(steerAngle) / L;
            float assistFade = Math.Clamp(speed / 2f, 0f, 1f);
            float da = P.stabilityAssist * (P.kBeta * beta - P.kYaw * (R - rRef)) * assistFade;
            da = Math.Clamp(da, -0.08f, 0.08f);
            DeltaAssist = da;

            // Apply assist to both front wheels equally
            deltaFL += da;
            deltaFR += da;
            finalSteerAngle = (deltaFL + deltaFR) * 0.5f;

            // ── Brake-while-cornering coupling ────────────────────────────────
            // Use max of (measured lateral G fraction) and (current steer fraction) so the
            // cut kicks in immediately when the driver is steering, not just after a force lag.
            float steerFraction = MathF.Abs(steerAngle) / MathF.Max(P.maxSteerAngle, 0.01f);
            float latFraction   = MathF.Abs(ayBody) / MathF.Max(ayMax, 0.01f);
            float cornerFraction = MathF.Max(steerFraction, latFraction);
            float cornerFactor  = 1f - P.brakeCornerCut * Math.Clamp(cornerFraction, 0f, 1f);

            // ── Brakes ────────────────────────────────────────────────────────
            var (bFL, bFR, bRL, bRR) = BrakeModel.Compute(
                P, brake, cornerFactor,
                FzFL, FzFR, FzRL, FzRR,
                muFL, muFR, muRL, muRR);

            if (brake > 0.1f)
            {
                if (!isBrakingActive)
                {
                    isBrakingActive = true;
                    initialBrakeFrontN = bFL + bFR;
                    initialBrakeRearN  = bRL + bRR;
                    currentBrakingDecelG = 0f;
                }
            }
            else
            {
                if (isBrakingActive)
                {
                    isBrakingActive = false;
                    lastBrakingPeakDecelG = currentBrakingDecelG;
                }
            }

            // ── Powertrain ────────────────────────────────────────────────────
            var (rawWheelTorque, _, _) = Powertrain.Step(P, PT, throttle, Vx, dt, FzRL, FzRR);

            float vxFL = Vx - R * twF; float vyFL = Vy + R * a;
            float vxFR = Vx + R * twF; float vyFR = Vy + R * a;
            float vxRL = Vx - R * twR; float vyRL = Vy - R * b;
            float vxRR = Vx + R * twR; float vyRR = Vy - R * b;

            float rTire = P.tireRadius;

            // Front wheels: undriven, follow ground
            Wheels[0].omega = vxFL / rTire;
            Wheels[1].omega = vxFR / rTire;

            // Rear wheel slip ratios for TC
            float baseRL = MathF.Max(MathF.Abs(vxRL), 0.5f);
            float baseRR = MathF.Max(MathF.Abs(vxRR), 0.5f);
            float slipRL = (Wheels[2].omega * rTire - vxRL) / baseRL;
            float slipRR = (Wheels[3].omega * rTire - vxRR) / baseRR;
            float maxRearSlip = MathF.Max(slipRL, slipRR);

            // Slip-ratio based traction control:
            // Intervenes earlier starting at tcSlipTarget * 0.70f with smooth rolloff
            float tcFactor = 1.0f;
            float tcStartThreshold = P.tcSlipTarget * 0.70f;
            if (maxRearSlip > tcStartThreshold)
            {
                float excess = (maxRearSlip - tcStartThreshold) / MathF.Max(P.tcSlipRange, 0.01f);
                tcFactor = 1.0f / (1.0f + excess * excess);
            }

            // Corner exit protection: scale drive torque by current rear lateral grip scale when steering or lateral G is non-zero
            float cornerExitScale = 1.0f;
            if (MathF.Abs(steer) > 0.01f || MathF.Abs(ayBody) > 0.5f)
            {
                cornerExitScale = Math.Clamp(RearLateralGripScale, 0.20f, 1.0f);
            }

            float effectiveTorque = rawWheelTorque * tcFactor * cornerExitScale;

            float fxMaxRL = muRL * FzRL;
            float fxMaxRR = muRR * FzRR;
            if (P.tcForceLimitFallback)
            {
                float maxRearForce = P.tractionFrac * (fxMaxRL + fxMaxRR);
                effectiveTorque = MathF.Min(effectiveTorque, maxRearForce * rTire);
            }

            // Enforce rearLateralReserve: cap rear combined usage so lateral grip scale never drops below rearLateralReserve.
            // Under friction ellipse: scale = sqrt(1 - (fx/fxMax)^2) >= reserve => fx <= fxMax * sqrt(1 - reserve^2).
            float maxRearLongFrac = MathF.Sqrt(MathF.Max(0f, 1f - P.rearLateralReserve * P.rearLateralReserve));
            float maxAllowableRearForce = maxRearLongFrac * (fxMaxRL + fxMaxRR);
            effectiveTorque = MathF.Min(effectiveTorque, maxAllowableRearForce * rTire);

            // Limited slip differential (diffLockFrac)
            // Slower wheel gets extra drive torque in proportion to speed difference, capped by diffLockFrac
            float deltaOmega = Wheels[2].omega - Wheels[3].omega;
            float avgOmega   = MathF.Max((Wheels[2].omega + Wheels[3].omega) * 0.5f, 1.0f);
            float normDiff   = Math.Clamp(deltaOmega / avgOmega, -1f, 1f);
            float transferFrac = Math.Clamp(normDiff * 1.5f, -P.diffLockFrac, P.diffLockFrac);

            float torqueRL = effectiveTorque * (0.5f - 0.5f * transferFrac);
            float torqueRR = effectiveTorque * (0.5f + 0.5f * transferFrac);

            // Driving force demand from delivered torque
            float demandRL = torqueRL / rTire;
            float demandRR = torqueRR / rTire;

            // Longitudinal stiffness ~ pacBx * FzMax
            float cKappaRL = MathF.Max(P.pacBx * fxMaxRL, 1000f);
            float cKappaRR = MathF.Max(P.pacBx * fxMaxRR, 1000f);

            // Elastic slip from torque demand
            float baseSlipRL = Math.Clamp(demandRL / cKappaRL, -0.05f, 0.05f);
            float baseSlipRR = Math.Clamp(demandRR / cKappaRR, -0.05f, 0.05f);

            // When demand exceeds grip, excess torque spins up the wheel
            float excessRL = MathF.Max(demandRL - fxMaxRL, 0f);
            float excessRR = MathF.Max(demandRR - fxMaxRR, 0f);

            float spinOmegaRL = (excessRL * rTire / P.wheelInertia) * dt * 2.1f;
            float spinOmegaRR = (excessRR * rTire / P.wheelInertia) * dt * 2.1f;

            float targetOmegaRL = (vxRL + baseSlipRL * baseRL) / rTire + spinOmegaRL;
            float targetOmegaRR = (vxRR + baseSlipRR * baseRR) / rTire + spinOmegaRR;

            Wheels[2].omega += (targetOmegaRL - Wheels[2].omega) * MathF.Min(1f, 40f * dt);
            Wheels[3].omega += (targetOmegaRR - Wheels[3].omega) * MathF.Min(1f, 40f * dt);

            // LSD locking torque couples wheel speeds together
            float diffCoupling = P.diffLockFrac * 80f * dt;
            float lockCorrection = 0.5f * (Wheels[2].omega - Wheels[3].omega) * MathF.Min(0.9f, diffCoupling);
            Wheels[2].omega -= lockCorrection;
            Wheels[3].omega += lockCorrection;

            Wheels[2].kappaLag = (Wheels[2].omega * rTire - vxRL) / baseRL;
            Wheels[3].kappaLag = (Wheels[3].omega * rTire - vxRR) / baseRR;

            // Actual drive forces delivered to chassis (limited by Pacejka grip)
            float fxDriveRL = MathF.Min(demandRL, fxMaxRL);
            float fxDriveRR = MathF.Min(demandRR, fxMaxRR);

            // Cap rear drive force so lateral grip scale never drops below rearLateralReserve
            if (P.rearLateralReserve > 0f)
            {
                float capRL = fxMaxRL * maxRearLongFrac;
                float capRR = fxMaxRR * maxRearLongFrac;
                fxDriveRL = MathF.Min(fxDriveRL, capRL);
                fxDriveRR = MathF.Min(fxDriveRR, capRR);
            }

            // Per-wheel longitudinal forces (positive = forward)
            float fxFL = -bFL;
            float fxFR = -bFR;
            float fxRL = fxDriveRL - bRL;
            float fxRR = fxDriveRR - bRR;

            // Clamp to wheel grip limits
            float fxMaxFL = muFL * FzFL; float fxMaxFR = muFR * FzFR;
            fxFL = Math.Clamp(fxFL, -fxMaxFL, fxMaxFL);
            fxFR = Math.Clamp(fxFR, -fxMaxFR, fxMaxFR);
            fxRL = Math.Clamp(fxRL, -fxMaxRL, fxMaxRL);
            fxRR = Math.Clamp(fxRR, -fxMaxRR, fxMaxRR);

            // Rear traction telemetry (combined rear)
            float rearFxTotal = MathF.Abs(fxRL) + MathF.Abs(fxRR);
            float rearFxMax   = fxMaxRL + fxMaxRR;
            RearTractionUse = rearFxTotal / MathF.Max(rearFxMax, 1f);

            // ── Slip angles per wheel ─────────────────────────────────────────
            // Corner positions (body frame, x forward, y left):
            // FL: (a, +twF), FR: (a, -twF), RL: (-b, +twR), RR: (-b, -twR)
            float spFL = MathF.Max(MathF.Sqrt(vxFL*vxFL + vyFL*vyFL), 0.5f);
            float spFR = MathF.Max(MathF.Sqrt(vxFR*vxFR + vyFR*vyFR), 0.5f);
            float spRL = MathF.Max(MathF.Sqrt(vxRL*vxRL + vyRL*vyRL), 0.5f);
            float spRR = MathF.Max(MathF.Sqrt(vxRR*vxRR + vyRR*vyRR), 0.5f);

            // Slip angle = atan2(lateral vel, forward vel) - steer angle
            float alphaTargetFL = MathF.Atan2(vyFL, MathF.Abs(vxFL)) - deltaFL;
            float alphaTargetFR = MathF.Atan2(vyFR, MathF.Abs(vxFR)) - deltaFR;
            float alphaTargetRL = MathF.Atan2(vyRL, MathF.Abs(vxRL));
            float alphaTargetRR = MathF.Atan2(vyRR, MathF.Abs(vxRR));

            // Slip relaxation per wheel
            TireModel.RelaxAngle(ref Wheels[0].alphaLag, alphaTargetFL, spFL, dt, P.relaxLength);
            TireModel.RelaxAngle(ref Wheels[1].alphaLag, alphaTargetFR, spFR, dt, P.relaxLength);
            TireModel.RelaxAngle(ref Wheels[2].alphaLag, alphaTargetRL, spRL, dt, P.relaxLength);
            TireModel.RelaxAngle(ref Wheels[3].alphaLag, alphaTargetRR, spRR, dt, P.relaxLength);

            // ── Lateral tire forces ───────────────────────────────────────────
            float ellipFL = TireModel.EllipseScale(fxFL, fxMaxFL, P.lateralGripFloor);
            float ellipFR = TireModel.EllipseScale(fxFR, fxMaxFR, P.lateralGripFloor);
            float ellipRL = TireModel.EllipseScale(fxRL, fxMaxRL, P.lateralGripFloor);
            float ellipRR = TireModel.EllipseScale(fxRR, fxMaxRR, P.lateralGripFloor);

            RearLateralGripScale = (ellipRL + ellipRR) * 0.5f;

            float fyRawFL = -TireModel.Lateral(P, Wheels[0].alphaLag, muFL * FzFL);
            float fyRawFR = -TireModel.Lateral(P, Wheels[1].alphaLag, muFR * FzFR);
            float fyRawRL = -TireModel.Lateral(P, Wheels[2].alphaLag, muRL * FzRL);
            float fyRawRR = -TireModel.Lateral(P, Wheels[3].alphaLag, muRR * FzRR);

            float fyFL = fyRawFL * ellipFL;
            float fyFR = fyRawFR * ellipFR;
            float fyRL = fyRawRL * ellipRL;
            float fyRR = fyRawRR * ellipRR;

            // ── Resolve front wheel forces into body frame ────────────────────
            float cdFL = MathF.Cos(deltaFL), sdFL = MathF.Sin(deltaFL);
            float cdFR = MathF.Cos(deltaFR), sdFR = MathF.Sin(deltaFR);

            // Body-frame forces from each wheel
            float fxBodyFL = fxFL * cdFL - fyFL * sdFL;
            float fyBodyFL = fxFL * sdFL + fyFL * cdFL;
            float fxBodyFR = fxFR * cdFR - fyFR * sdFR;
            float fyBodyFR = fxFR * sdFR + fyFR * cdFR;

            float rolling = Vx > 0.1f ? P.rollingRes * P.mass * 9.81f : 0f;

            float fxBody = fxBodyFL + fxBodyFR + fxRL + fxRR - drag - rolling;
            float fyBody = fyBodyFL + fyBodyFR + fyRL + fyRR;

            // Yaw moment: front axle force × a, rear axle force × (-b)
            // Lateral moment arm: left wheels +twF/twR, right wheels -twF/twR
            // Using (x, y) x (Fx, Fy) = x*Fy - y*Fx: left wheel (+tw) fx gives -tw*fx yaw moment!
            float mzFront = a  * (fyBodyFL + fyBodyFR)
                           - twF * (fxBodyFL - fxBodyFR);  // asymmetric Fx contribution
            float mzRear  = -b * (fyRL + fyRR)
                           - twR * (fxRL - fxRR);           // diff torque yaw moment
            float mz = mzFront + mzRear;

            fyBodyReal = fyBody;
            ayBody     = fyBody / P.mass;

            // Spin recovery yaw damping
            if (MathF.Abs(beta) > P.spinBetaThreshold)
                mz -= P.spinYawDamping * R;

            // ── Euler integration ────────────────────────────────────────────
            Vx += (fxBody / P.mass + Vy * R) * dt;
            Vy += (fyBody / P.mass - Vx * R) * dt;
            R  += (mz / P.yawInertia) * dt;
            Ax += (fxBody / P.mass - Ax) * MathF.Min(1f, dt * 20f);

            if (Vx < 0f) Vx = 0f;

            if (isBrakingActive)
            {
                float decelG = -fxBody / (P.mass * g);
                if (decelG > currentBrakingDecelG)
                    currentBrakingDecelG = decelG;
                lastBrakingPeakDecelG = currentBrakingDecelG;
            }

            // ── Low-speed kinematic blend ─────────────────────────────────────
            if (Vx < P.vBlend)
            {
                float w    = Vx / P.vBlend;
                float rKin = Vx * MathF.Tan(finalSteerAngle) / L;
                R  = rKin + (R - rKin) * w;
                Vy *= w;
            }

            // ── World integration ─────────────────────────────────────────────
            Yaw -= R * dt;
            float sinY = MathF.Sin(Yaw), cosY = MathF.Cos(Yaw);
            X += (Vx * sinY - Vy * cosY) * dt;
            Z += (Vx * cosY + Vy * sinY) * dt;
        }
    }
}

