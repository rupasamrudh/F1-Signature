// DemoDriver.cs — Pure pursuit steering and speed profile auto-driving controller for F1 Signature.
using System;

namespace F1Sim.Simulation.Track
{
    public class DemoDriver
    {
        public readonly TrackSpline Spline;
        public float LookaheadDistance = 15.0f; // meters ahead along centreline
        public float SpeedSafetyFactor  = 0.92f; // scale down speed profile for safe cornering
        public float KpSteer            = 2.2f;  // steering proportional gain

        float[] _speedProfile;
        int _lastHintIndex = 0;

        public DemoDriver(TrackSpline spline, float maxLatG = 2.8f, float maxAccG = 1.2f, float maxDecelG = 3.5f)
        {
            Spline = spline ?? throw new ArgumentNullException(nameof(spline));
            _speedProfile = Spline.ComputeSpeedProfile(maxLatG, maxAccG, maxDecelG);
        }

        /// Computes (steer, throttle, brake) commands given vehicle state.
        public (float steer, float throttle, float brake) Step(
            float carX, float carZ, float carYawRad, float carVx, float carVy)
        {
            // 1. Query track at car position with cached hint index
            var q = Spline.Query(carX, carZ, _lastHintIndex);
            _lastHintIndex = q.nearestIndex;

            // 2. Pure pursuit target point at s + lookahead
            float targetS = (q.s + LookaheadDistance) % Spline.TotalLength;
            var targetQ = Spline.QueryAtS(targetS);

            // Target vector in world coordinates
            float dx = targetQ.x - carX;
            float dz = targetQ.z - carZ;

            // Transform into vehicle local frame:
            // carYawRad is clockwise from +Z (Z forward, X right)
            // Forward unit vector: (sin(yaw), cos(yaw))
            // Right unit vector:   (cos(yaw), -sin(yaw))
            float forwardX = MathF.Sin(carYawRad);
            float forwardZ = MathF.Cos(carYawRad);
            float rightX   = MathF.Cos(carYawRad);
            float rightZ   = -MathF.Sin(carYawRad);

            float localX = dx * rightX + dz * rightZ;     // positive = target is to the right
            float localZ = dx * forwardX + dz * forwardZ; // positive = target is in front

            float targetDist = MathF.Max(MathF.Sqrt(localX * localX + localZ * localZ), 1.0f);

            // Pure pursuit curvature: 2 * x / L^2
            // Steer input is -1..+1 where positive steer turns left in BicycleModel/VehicleDynamics
            // If localX > 0 (target to the right), car needs to turn right (negative steer)
            float curvatureDemand = 2.0f * localX / (targetDist * targetDist);
            float steer = Math.Clamp(-curvatureDemand * KpSteer * 10.0f, -1.0f, 1.0f);

            // 3. Longitudinal speed control
            // Target speed from profile at targetS
            int sIdx = targetQ.nearestIndex;
            float targetSpeed = _speedProfile[sIdx] * SpeedSafetyFactor;

            float currentSpeed = MathF.Max(carVx, 0f);
            float speedError = targetSpeed - currentSpeed;

            float throttle = 0f;
            float brake = 0f;

            if (speedError > 0f)
            {
                // Accelerate
                throttle = Math.Clamp(speedError / 5.0f, 0.1f, 1.0f);
                brake = 0f;
            }
            else
            {
                // Brake
                throttle = 0f;
                brake = Math.Clamp(-speedError / 8.0f, 0.2f, 1.0f);
            }

            return (steer, throttle, brake);
        }
    }
}

