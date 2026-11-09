// TireModel.cs — Pure C#, no UnityEngine. Per-wheel Pacejka + slip relaxation + friction ellipse.
using System;

namespace F1Sim.Simulation
{
    /// Per-wheel tire state (slip relaxation, angular velocity).
    public class WheelState
    {
        public float alphaLag;   // lagged slip angle (rad)
        public float kappaLag;   // lagged longitudinal slip ratio
        public float omega;      // wheel angular velocity (rad/s)

        public void Reset()
        {
            alphaLag = 0f;
            kappaLag = 0f;
            omega    = 0f;
        }
    }

    /// Pacejka magic-formula tire model, per wheel.
    public static class TireModel
    {
        /// Load-sensitive friction coefficient.
        public static float Mu(CarParams p, float fz)
            => MathF.Max(0.6f, p.mu0 * (1f - p.loadSens * (fz - p.fz0) / p.fz0));

        /// Pacejka lateral force (N). Input alpha in rad, D = mu * Fz.
        public static float Lateral(CarParams p, float alpha, float d)
        {
            float bx = p.pacB * alpha;
            return d * MathF.Sin(p.pacC * MathF.Atan(bx - p.pacE * (bx - MathF.Atan(bx))));
        }

        /// Pacejka longitudinal force (N). Input kappa (slip ratio), D = mu * Fz.
        /// Uses separate longitudinal Pacejka shape params.
        public static float Longitudinal(CarParams p, float kappa, float d)
        {
            float bx = p.pacBx * kappa;
            return d * MathF.Sin(p.pacCx * MathF.Atan(bx - p.pacEx * (bx - MathF.Atan(bx))));
        }

        /// Friction ellipse: returns lateral scale factor given longitudinal use fraction.
        public static float EllipseScale(float fx, float fxMax, float floor)
        {
            float k = fx / MathF.Max(fxMax, 1f);
            float scale = MathF.Sqrt(MathF.Max(0f, 1f - k * k));
            return MathF.Max(scale, floor);
        }

        /// Update slip relaxation for one wheel. Returns lagged value.
        public static float RelaxAngle(ref float lag, float target, float speed, float dt, float relaxLength)
        {
            float factor = 1f - MathF.Exp(-speed * dt / MathF.Max(relaxLength, 0.001f));
            lag += (target - lag) * factor;
            return lag;
        }

        /// Update kappa (longitudinal slip) relaxation.
        public static float RelaxKappa(ref float lag, float target, float speed, float dt, float relaxLength)
        {
            float factor = 1f - MathF.Exp(-speed * dt / MathF.Max(relaxLength, 0.001f));
            lag += (target - lag) * factor;
            return lag;
        }
    }
}

