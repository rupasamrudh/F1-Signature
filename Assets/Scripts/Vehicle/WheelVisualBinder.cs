using UnityEngine;

namespace F1Sim.Vehicle
{
    public class WheelVisualBinder : MonoBehaviour
    {
        [Header("Wheel Visual Transforms")]
        public Transform wheelFL;
        public Transform wheelFR;
        public Transform wheelRL;
        public Transform wheelRR;

        [Header("Car Controller Reference")]
        public CarController controller;

        // Bounded spin angles in [0, 360)
        float flSpinAngle = 0f;
        float frSpinAngle = 0f;
        float rlSpinAngle = 0f;
        float rrSpinAngle = 0f;

        // Smooth steering angles for visual presentation (eliminates 50Hz physics stair-stepping & assist micro-jitter)
        float smoothSteerFL = 0f;
        float smoothSteerFR = 0f;

        // Smoothed speed for stable wheel rotation rate (eliminates render-physics dt aliasing)
        float smoothForwardSpeed = 0f;

        // Cached rest local positions to eliminate any hub displacement or drift
        Vector3 restPosFL;
        Vector3 restPosFR;
        Vector3 restPosRL;
        Vector3 restPosRR;
        bool hasRestPositions = false;

        void Start()
        {
            if (controller == null)
                controller = GetComponentInParent<CarController>();

            CacheRestPositions();
        }

        void CacheRestPositions()
        {
            if (wheelFL != null) restPosFL = wheelFL.localPosition;
            if (wheelFR != null) restPosFR = wheelFR.localPosition;
            if (wheelRL != null) restPosRL = wheelRL.localPosition;
            if (wheelRR != null) restPosRR = wheelRR.localPosition;
            hasRestPositions = true;
        }

        void LateUpdate()
        {
            if (controller == null) return;
            if (!hasRestPositions) CacheRestPositions();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float rawSpeedMs = controller.ForwardSpeedMs;
            float tireRadius = (controller.config != null && controller.config.tireRadius > 0.05f)
                ? controller.config.tireRadius
                : 0.358f;

            // Critically smooth forward speed to prevent 50Hz physics discrete velocity steps from jerking spin rate
            smoothForwardSpeed = Mathf.Lerp(smoothForwardSpeed, rawSpeedMs, 1f - Mathf.Exp(-25f * dt));

            // Angular spin rate: v / r (rad/s) -> deg/s
            float rawSpinStepDeg = (smoothForwardSpeed / tireRadius) * Mathf.Rad2Deg * dt;

            // Anti-aliasing cap for high speeds:
            // At high speeds (>45 km/h), raw spin step can exceed the spoke symmetry Nyquist limit (~24°/frame at 60fps),
            // which causes stroboscopic wagon-wheel backwards jumping and visual vibration.
            // Capping the frame spin step guarantees silky, continuous forward blur rotation at any speed.
            const float kMaxFrameSpinDeg = 24.0f;
            float spinStepDeg = Mathf.Clamp(rawSpinStepDeg, -kMaxFrameSpinDeg, kMaxFrameSpinDeg);

            // Spin accumulation kept strictly in [0, 360) to eliminate 32-bit float truncation jitter
            flSpinAngle = (flSpinAngle + spinStepDeg) % 360f;
            if (flSpinAngle < 0f) flSpinAngle += 360f;
            frSpinAngle = flSpinAngle;

            rlSpinAngle = (rlSpinAngle + spinStepDeg) % 360f;
            if (rlSpinAngle < 0f) rlSpinAngle += 360f;
            rrSpinAngle = rlSpinAngle;

            // Target Ackermann steering angles (negative for Unity coordinate system: +Y up, turning right is positive yaw)
            float targetSteerFL = -controller.SteerAngleDegFL;
            float targetSteerFR = -controller.SteerAngleDegFR;

            // Apply fine deadband for straight-line running to eliminate stability assist micro-chatter
            if (Mathf.Abs(targetSteerFL) < 0.08f) targetSteerFL = 0f;
            if (Mathf.Abs(targetSteerFR) < 0.08f) targetSteerFR = 0f;

            // Critically damped steering filter (18 rad/s ~ 55ms settling):
            // Smooths 50Hz physics discrete updates into silky render-frame motion without perceived input lag
            float steerFilterT = 1f - Mathf.Exp(-18f * dt);
            smoothSteerFL = Mathf.Lerp(smoothSteerFL, targetSteerFL, steerFilterT);
            smoothSteerFR = Mathf.Lerp(smoothSteerFR, targetSteerFR, steerFilterT);

            // Front wheels: steer around local Y axis, then roll forward around steered local X axis
            if (wheelFL != null)
            {
                wheelFL.localPosition = restPosFL;
                wheelFL.localRotation = Quaternion.AngleAxis(smoothSteerFL, Vector3.up) *
                                        Quaternion.AngleAxis(flSpinAngle, Vector3.right);
            }

            if (wheelFR != null)
            {
                wheelFR.localPosition = restPosFR;
                wheelFR.localRotation = Quaternion.AngleAxis(smoothSteerFR, Vector3.up) *
                                        Quaternion.AngleAxis(frSpinAngle, Vector3.right);
            }

            // Rear wheels: rolling spin only (fixed heading) anchored at hub center
            if (wheelRL != null)
            {
                wheelRL.localPosition = restPosRL;
                wheelRL.localRotation = Quaternion.AngleAxis(rlSpinAngle, Vector3.right);
            }

            if (wheelRR != null)
            {
                wheelRR.localPosition = restPosRR;
                wheelRR.localRotation = Quaternion.AngleAxis(rrSpinAngle, Vector3.right);
            }
        }
    }
}
