using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SukiUI.Animations;
using SukiUI.Controls.GlassMorphism;

namespace SukiUI.ControlsAnimation
{
    /// <summary>
    /// The complete dialog open / close / pinned-shake choreography, over the same
    /// <see cref="SukiTicker"/> loop that drives the press and popup engines. The dialog
    /// is a hybrid: the open / close is declarative (Avalonia Transitions armed per
    /// opening with a size-calibrated <see cref="SukiSpringEaseOut"/>), only the
    /// pinned-background shake is a real spring integrated per frame — the same damped
    /// oscillator as press / popup, shared through <see cref="SukiSpring"/>.
    /// Owned by one SukiDialogHost; the host handles pointer tracking, template lookup
    /// and manager events, and calls this engine on every state change.
    /// </summary>
    public sealed class SukiDialogPhysics : IDisposable
    {
        private readonly Func<SukiDialogProfile> _getProfile;
        private SukiDialogProfile _profile;             // snapshot per open/close/shake, re-read at entry

        private Border? _surface;

        // Open/close transitions built per opening, kept so the shake can detach and
        // restore them around its direct writes (single writer on the RenderTransform).
        private Transitions? _openTransitions;

        // Shake spring state (semi-implicit Euler around rest, driven by SukiTicker).
        private IDisposable? _shakeTicker;
        private double _shakeY, _shakeV, _shakeScale;
        private long _shakeLastTick;

        public SukiDialogPhysics(Func<SukiDialogProfile> getProfile)
        {
            _getProfile = getProfile;
            _profile = getProfile();
        }

        /// <summary>
        /// Opens the dialog with a fixed rise from below, steered horizontally toward the
        /// click that summoned it. The spring is calibrated against the dialog's measured
        /// area: small ones replay the button's release spring exactly, bigger ones get
        /// more mass — slower, more damped, no rebound.
        /// </summary>
        public void PlayOpen(ContentControl content, double width, double height, (double Dx, double Dy) emergence)
        {
            _profile = _getProfile();   // re-resolve at choreography start: a live switch never touches a running one
            EnsureDialogSurface(content);

            double area = width * height;
            double sizeT = Math.Clamp(
                (area - _profile.SmallDialogArea) / (_profile.LargeDialogArea - _profile.SmallDialogArea), 0.0, 1.0);
            double sizeCurve = Math.Pow(sizeT, _profile.DampingCurveExponent);

            double transformDurationMs = SukiSpring.Lerp(
                _profile.OpenTransformDurationSmallMs, _profile.OpenTransformDurationLargeMs, sizeCurve);
            double omega = SukiSpring.Lerp(_profile.OpenOmegaSmall, _profile.OpenOmegaLarge, sizeT);
            double zeta = SukiSpring.Lerp(_profile.OpenZetaSmall, _profile.OpenZetaLarge, sizeCurve);
            var spring = new SukiSpringEaseOut { Omega = omega, Decay = 2.0 * zeta * omega };
            var transformDuration = TimeSpan.FromMilliseconds(transformDurationMs);
            double fromScale = SukiSpring.Lerp(_profile.OpenFromScaleSmall, _profile.OpenFromScaleLarge, sizeT);

            // Initial pose with the transitions detached, then re-arm and head for the
            // target: the transitions animate from whatever pose is current, which is the
            // emerged-from-the-click one.
            content.Transitions = null;
            SetPose(content, emergence, fromScale, 0.0, _profile.BlurredRadius);
            FadeGlass(content, 1.0);
            _openTransitions = BuildTransitions(
                spring, transformDuration, TimeSpan.FromMilliseconds(_profile.OpenOpacityDurationMs));
            content.Transitions = _openTransitions;
            SetPose(content, (0.0, 0.0), 1.0, 1.0, 0.0);
        }

        /// <summary>
        /// Closes downward: the dialog sinks below its resting place, regardless of where
        /// the dismissal interaction happened.
        /// </summary>
        public void PlayClose(ContentControl content)
        {
            _profile = _getProfile();   // re-resolve at choreography start
            // A shake leaves the transitions detached (it owns the transform while it
            // runs): restore them so the close actually animates instead of snapping.
            StopShake();
            content.Transitions ??= _openTransitions;
            FadeGlass(content, 0.0);
            SetPose(content, (0.0, _profile.EmergenceVertical), _profile.CloseScale, 0.0, _profile.BlurredRadius);
        }

        /// <summary>
        /// Struck-spring shake for a pinned dialog pressed on the backdrop: the vertical
        /// offset starts from the pose actually on screen with an initial velocity, and
        /// integrates back to rest. The transitions are detached while the shake writes
        /// the transform directly, then re-armed.
        /// </summary>
        public void StartShake(ContentControl content, double initialVelocity)
        {
            _profile = _getProfile();   // re-resolve the active profile at shake start
            StopShake();
            var (ty, scale) = ReadCurrentTransform(content);
            _shakeY = ty;
            _shakeV = initialVelocity;
            _shakeScale = scale;
            _shakeLastTick = SukiTicker.Timestamp;
            content.Transitions = null;

            // One synchronous tick primes the very first transform write, which schedules
            // the frame the rest of the shake rides on.
            _shakeTicker = SukiTicker.Subscribe(content, _ => ShakeTick(content));
            ShakeTick(content);
        }

        private void ShakeTick(ContentControl content)
        {
            double dt = Math.Min(SukiTicker.ElapsedSeconds(_shakeLastTick), 0.05);
            _shakeLastTick = SukiTicker.Timestamp;

            SukiSpring.Step(ref _shakeY, ref _shakeV, 0.0, dt, _profile.ShakeOmega, _profile.ShakeDecay);

            if (Math.Abs(_shakeY) < _profile.ShakeSettleDelta && Math.Abs(_shakeV) < _profile.ShakeSettleVelocity)
            {
                // Settled: hand the transform back to the transitions and restate the open
                // rest pose — if the shake interrupted an opening mid-flight, the remaining
                // travel resumes as a proper transition instead of snapping.
                StopShake();
                content.Transitions = _openTransitions;
                SetPose(content, (0.0, 0.0), 1.0, 1.0, 0.0);
                return;
            }

            WriteTransform(content, 0.0, _shakeY, _shakeScale);
        }

        public void Stop() => StopShake();

        public void Dispose()
        {
            StopShake();
            _surface = null;
            _openTransitions = null;
        }

        private void StopShake()
        {
            _shakeTicker?.Dispose();
            _shakeTicker = null;
        }

        // ---- Visual plumbing -------------------------------------------------------

        /// <summary>
        /// The glass overlay fades on its own fast clock, decoupled from the content's
        /// choreography (melting the frost with the content's fade reads as the dialog
        /// blackening, and the frost must be fully in before the content's opening blur
        /// has finished collapsing). The transition is attached ONCE per glass lifetime —
        /// replacing a live Transitions collection and writing the target in the same
        /// frame makes the write land before the transition is armed and the value snaps.
        /// </summary>
        private void FadeGlass(ContentControl content, double to)
        {
            if (FindGlassOverlay(content) is not { } glass)
                return;
            if (glass.Transitions is null)
            {
                glass.Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = BlurBackground.OverlayOpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(_profile.GlassFadeMilliseconds)
                    }
                };
                // Same-frame attach + write snaps; give the transition one frame to arm.
                Dispatcher.UIThread.Post(() => glass.OverlayOpacity = to, DispatcherPriority.Loaded);
            }
            else
            {
                glass.OverlayOpacity = to;
            }
        }

        /// <summary>
        /// The dialog's depth-of-field surface (PART_DialogSurface, in the SukiDialog's
        /// ControlTheme — NOT the host's template, hence the visual-tree walk). The DoF
        /// blur lives there, never on the content control: the glass overlay renders
        /// through a custom draw op, and under an ancestor Effect it lands in the effect
        /// buffer, whose opaque restore pass then smears into black. Re-checked on every
        /// open: a re-applied template brings a fresh, uninitialized surface.
        /// </summary>
        private void EnsureDialogSurface(ContentControl content)
        {
            var surface = content.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Name == "PART_DialogSurface");
            if (surface is null || ReferenceEquals(surface, _surface))
                return;
            _surface = surface;
            surface.Effect = new BlurEffect { Radius = _profile.BlurredRadius };
            surface.Transitions = new Transitions
            {
                new EffectTransition
                {
                    Property = Visual.EffectProperty,
                    Duration = TimeSpan.FromMilliseconds(_profile.SurfaceTransitionDurationMs)
                }
            };
        }

        private void SetPose(
            ContentControl content, (double Dx, double Dy) offset, double scale, double opacity, double blur)
        {
            WriteTransform(content, offset.Dx, offset.Dy, scale);
            content.Opacity = opacity;
            if (_surface is { } surface)
                surface.Effect = new BlurEffect { Radius = blur };
        }

        private static void WriteTransform(ContentControl content, double dx, double dy, double scale)
        {
            content.RenderTransform = TransformOperations.Parse(FormattableString.Invariant(
                $"translate({dx:0.##}px, {dy:0.##}px) scale({scale:0.###})"));
        }

        private static (double Ty, double Scale) ReadCurrentTransform(ContentControl content)
        {
            var matrix = content.RenderTransform is { } transform ? transform.Value : Matrix.Identity;
            return (matrix.M32, matrix.M11);
        }

        // Visual-tree walk, not logical: the glass lives inside the SukiDialog's
        // ControlTemplate, and template children are not logical descendants — the
        // logical lookup silently returned null here (glass stuck at its template
        // opacity, never driven).
        private static BlurBackground? FindGlassOverlay(ContentControl content) =>
            content.GetVisualDescendants().OfType<BlurBackground>().FirstOrDefault();

        private static Transitions BuildTransitions(
            Easing spring, TimeSpan transformDuration, TimeSpan opacityDuration) => new()
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = opacityDuration },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = transformDuration,
                Easing = spring
            }
        };
    }
}
