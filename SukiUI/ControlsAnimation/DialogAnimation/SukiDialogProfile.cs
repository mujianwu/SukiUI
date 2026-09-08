namespace SukiUI.ControlsAnimation
{
    /// <summary>
    /// XAML-selectable dialog feels.
    /// </summary>
    public enum SukiDialogPreset
    {
        Default
    }

    /// <summary>
    /// Calibrated constants of the dialog's open / close / pinned-shake chain, one profile
    /// per <see cref="SukiDialogPreset"/>. The open motion is a size-calibrated spring
    /// (small dialogs bounce, big dialogs do not), driven by Avalonia Transitions +
    /// <c>SukiSpringEaseOut</c>; the pinned-background shake is a real damped spring
    /// integrated frame by frame on <see cref="SukiTicker"/> (see <see cref="SukiDialogPhysics"/>).
    /// </summary>
    /// <remarks>
    /// Size-based spring calibration: dialog "mass" grows with its area, so bigger dialogs
    /// get a slower, more damped spring and a smaller scale travel, while small ones are
    /// allowed to be toy-like. The damping ramp is curved (<see cref="DampingCurveExponent"/>)
    /// so mid-size dialogs keep noticeably more bounce than a linear ramp would leave them,
    /// and only truly large dialogs go overdamped — no rebound at all, never longer than small.
    /// </remarks>
    public sealed record SukiDialogProfile(
        // Size-based spring calibration breakpoints.
        double SmallDialogArea,
        double LargeDialogArea,
        double DampingCurveExponent,
        // Emergence pose (open) — always rises from below by EmergenceVertical, pointer steers horizontal only.
        double EmergenceVertical,
        double EmergenceHorizontalMax,
        // Open spring: lerp between small and large, linearly for omega, curved (sizeT^exp) for the rest.
        double OpenTransformDurationSmallMs,
        double OpenTransformDurationLargeMs,
        double OpenOmegaSmall,
        double OpenOmegaLarge,
        double OpenZetaSmall,
        double OpenZetaLarge,
        double OpenFromScaleSmall,
        double OpenFromScaleLarge,
        int OpenOpacityDurationMs,
        // Close pose: sinks straight back down by EmergenceVertical.
        double CloseScale,
        // Depth-of-field blur: same radius at both blurred ends of life, 0 at rest, animated on the surface.
        double BlurredRadius,
        int SurfaceTransitionDurationMs,
        // Glass overlay fade, decoupled from the content's choreography.
        int GlassFadeMilliseconds,
        // Pinned-dialog shake: a real spring given an initial velocity (impulse, not keyframes).
        double ShakeOmega,
        double ShakeDecay,
        double ShakeImpulse,
        double ShakeSettleDelta,
        double ShakeSettleVelocity)
    {
        /// <summary>
        /// Default feel: the historical calibration of SukiDialogHost. Small dialogs
        /// (~280x170) replay the button's release spring (omega 16 rad/s / duration 650ms)
        /// with a bit more damping (zeta 0.53, ~14% rebound); large ones (~700x495)
        /// go overdamped (zeta &gt; 1) with less travel. Shake: zeta 0.30, ~4 visible swings.
        /// </summary>
        public static readonly SukiDialogProfile Default = new(
            SmallDialogArea: 48_000.0,
            LargeDialogArea: 346_000.0,
            DampingCurveExponent: 1.6,
            EmergenceVertical: 100.0,
            EmergenceHorizontalMax: 50.0,
            OpenTransformDurationSmallMs: 650.0,
            OpenTransformDurationLargeMs: 400.0,
            OpenOmegaSmall: 10.4,
            OpenOmegaLarge: 5.8,
            OpenZetaSmall: 0.53,
            OpenZetaLarge: 1.05,
            OpenFromScaleSmall: 0.72,
            OpenFromScaleLarge: 0.86,
            OpenOpacityDurationMs: 300,
            CloseScale: 0.8,
            BlurredRadius: 40.0,
            SurfaceTransitionDurationMs: 250,
            GlassFadeMilliseconds: 220,
            ShakeOmega: 15.0,
            ShakeDecay: 9.0,
            ShakeImpulse: 320.0,
            ShakeSettleDelta: 0.5,
            ShakeSettleVelocity: 10.0);

        /// <summary>
        /// Resolves the XAML-selectable preset to its calibrated profile. Every enum
        /// member is listed explicitly — when adding a member, add its case here and
        /// keep names and calibrations in sync; invalid enum values throw.
        /// </summary>
#pragma warning disable CS8524 // the exhaustive member list is intentional; unnamed enum values throw
        public static SukiDialogProfile For(SukiDialogPreset preset) => preset switch
        {
            SukiDialogPreset.Default => Default,
        };
#pragma warning restore CS8524
    }
}
