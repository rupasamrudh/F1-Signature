// AeroModel.cs — Pure C#, no UnityEngine. Corner / Straight aero modes with blend.
using System;

namespace F1Sim.Simulation
{
    public enum AeroMode { Corner, Straight }

    public class AeroState
    {
        public AeroMode CurrentMode = AeroMode.Corner;
        public float    BlendFactor = 0f; // 0 = Corner, 1 = Straight
        public float    ClA;              // effective ClA after blend
        public float    CdA;              // effective CdA after blend

        public void Reset()
        {
            CurrentMode = AeroMode.Corner;
            BlendFactor = 0f;
        }
    }

    public static class AeroModel
    {
        /// Advance aero blend, compute dynamic pressure and forces.
        /// Returns (downforce N, drag N).
        public static (float down, float drag) Step(
            CarParams p, AeroState s, float vx, float dt)
        {
            // Blend toward target mode
            float target = s.CurrentMode == AeroMode.Straight ? 1f : 0f;
            float blendRate = 1f / MathF.Max(p.aeroBlendTime, 0.001f);
            s.BlendFactor += (target - s.BlendFactor) * MathF.Min(1f, blendRate * dt);
            s.BlendFactor  = Math.Clamp(s.BlendFactor, 0f, 1f);

            // Interpolate aero coefficients
            s.ClA = p.aeroCornerClA + (p.aeroStraightClA - p.aeroCornerClA) * s.BlendFactor;
            s.CdA = p.aeroCornerCdA + (p.aeroStraightCdA - p.aeroCornerCdA) * s.BlendFactor;

            // TODO: Ride-height sensitivity — scale ClA between 0.85 and 1.05 based on
            // speed-squared compression. Currently a placeholder (scale = 1.0).
            // Replace with AnimationCurve lookup when suspension model is added in M3.
            float rideHeightScale = 1.0f;
            s.ClA *= rideHeightScale;

            float q    = 0.5f * p.rho * vx * vx;
            float down = q * s.ClA;
            float drag = q * s.CdA;
            return (down, drag);
        }
    }
}

