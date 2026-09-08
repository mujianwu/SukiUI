using System;

namespace SukiUI.ControlsAnimation
{
    /// <summary>
    /// The library's damped-spring integrator (<c>x'' = -omega^2 (x - target) - decay x'</c>),
    /// semi-implicit Euler with fixed substeps. Shared by the press, popup and dialog
    /// engines so every module settles to the same numeric behavior at the same substep
    /// cadence (8ms ceiling). Time base is caller-provided <c>dt</c> — always derived
    /// from <see cref="SukiTicker"/> so all springs share the same monotonic clock.
    /// </summary>
    internal static class SukiSpring
    {
        public static void Step(ref double x, ref double v, double target, double dt, double omega, double decay)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.008));
            double h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                double accel = -omega * omega * (x - target) - decay * v;
                v += accel * h;
                x += v * h;
            }
        }

        public static double Lerp(double from, double to, double t) => from + (to - from) * t;
    }
}
