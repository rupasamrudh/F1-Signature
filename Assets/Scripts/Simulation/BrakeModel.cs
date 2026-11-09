// BrakeModel.cs — Pure C#, no UnityEngine. Per-wheel brake torque with bias and ABS limits.
using System;

namespace F1Sim.Simulation
{
    public static class BrakeModel
    {
        /// Compute per-wheel brake force (N, positive = retarding).
        /// fzFL/FR/RL/RR: vertical loads; muF/muR: friction coefficients per axle.
        /// Returns (fxFL, fxFR, fxRL, fxRR) as brake forces (positive retard values).
        public static (float fxFL, float fxFR, float fxRL, float fxRR) Compute(
            CarParams p,
            float brake,
            float cornerFactor,       // 1 - brakeCornerCut * |ayBody|/ayMax
            float fzFL, float fzFR, float fzRL, float fzRR,
            float muFL, float muFR, float muRL, float muRR)
        {
            float brakeTotal = brake * p.totalBrakeForceN * Math.Clamp(cornerFactor, 0f, 1f);

            float frontShare = brakeTotal * p.brakeBiasFront;
            float rearShare  = brakeTotal * (1f - p.brakeBiasFront);

            // Split front equally, rear equally
            float rawFL = frontShare * 0.5f;
            float rawFR = frontShare * 0.5f;
            float rawRL = rearShare  * 0.5f;
            float rawRR = rearShare  * 0.5f;

            // ABS limits per wheel
            float limFL = p.absFrac     * muFL * fzFL;
            float limFR = p.absFrac     * muFR * fzFR;
            float limRL = p.rearAbsFrac * muRL * fzRL;
            float limRR = p.rearAbsFrac * muRR * fzRR;

            float fxFL = MathF.Min(rawFL, limFL);
            float fxFR = MathF.Min(rawFR, limFR);
            float fxRL = MathF.Min(rawRL, limRL);
            float fxRR = MathF.Min(rawRR, limRR);

            return (fxFL, fxFR, fxRL, fxRR);
        }
    }
}

