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

        float flSpinAngle = 0f;
        float frSpinAngle = 0f;
        float rlSpinAngle = 0f;
        float rrSpinAngle = 0f;

        void Start()
        {
            if (controller == null)
                controller = GetComponentInParent<CarController>();
        }

        void LateUpdate()
        {
            if (controller == null) return;

            float dt = Time.deltaTime;
            float speedMs = controller.ForwardSpeedMs;
            float tireRadius = controller.config.tireRadius > 0.05f ? controller.config.tireRadius : 0.35f;

            // Angular spin rate: v / r (rad/s) -> deg/s
            float spinStepDeg = (speedMs / tireRadius) * Mathf.Rad2Deg * dt;

            // Wheels spin forward around local X axis
            flSpinAngle += spinStepDeg;
            frSpinAngle += spinStepDeg;
            rlSpinAngle += spinStepDeg;
            rrSpinAngle += spinStepDeg;

            // Front wheels: steer angle around local Y axis + rolling spin around local X axis
            // Note: In CarController/VehicleDynamics, finalSteerAngle is in radians (+ = left, - = right)
            // In Unity coordinates (+Y up, +Z forward, +X right): turning right is positive yaw (clockwise)
            float steerDeg = -controller.SteerAngleDeg;

            if (wheelFL != null)
            {
                wheelFL.localRotation = Quaternion.Euler(0f, steerDeg, 0f) * Quaternion.Euler(flSpinAngle, 0f, 0f);
            }

            if (wheelFR != null)
            {
                wheelFR.localRotation = Quaternion.Euler(0f, steerDeg, 0f) * Quaternion.Euler(frSpinAngle, 0f, 0f);
            }

            // Rear wheels: spin only (fixed heading)
            if (wheelRL != null)
            {
                wheelRL.localRotation = Quaternion.Euler(rlSpinAngle, 0f, 0f);
            }

            if (wheelRR != null)
            {
                wheelRR.localRotation = Quaternion.Euler(rrSpinAngle, 0f, 0f);
            }
        }
    }
}

