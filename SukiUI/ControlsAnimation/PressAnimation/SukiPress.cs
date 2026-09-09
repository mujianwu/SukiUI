using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SukiUI.ControlsAnimation
{
    /// <summary>
    /// Unified hover + press -&gt; release scale behavior facade over the shared
    /// <see cref="SukiPressPhysics"/> engine, driven by the single <see cref="SukiTicker"/>
    /// loop (see <see cref="SukiPressProfile"/> for the calibrated feels). This class is
    /// only the XAML-facing surface: it owns the attached properties and the pointer
    /// wiring, all motion lives in the engine.
    /// Enable it with <c>SukiPress.Enable="True"</c> (from a style setter), pick a feel
    /// with <c>SukiPress.Preset</c> (Button by default, ComboBox softer) and optionally
    /// override the depth with <c>SukiPress.PressDepth</c> (unset = the profile's default).
    /// The active profile and preset are resolved per gesture through
    /// <see cref="SukiAnimationTheme.Current"/>, so a live profile switch applies to the next
    /// press and an Enable/Preset reorder in XAML stays order-independent.
    /// </summary>
    public class SukiPress
    {
        public static readonly AttachedProperty<bool> EnableProperty =
            AvaloniaProperty.RegisterAttached<SukiPress, InputElement, bool>("Enable");

        public static readonly AttachedProperty<SukiPressPreset> PresetProperty =
            AvaloniaProperty.RegisterAttached<SukiPress, InputElement, SukiPressPreset>("Preset", SukiPressPreset.Button);

        // NaN sentinel = "follow the preset's DefaultPressDepth" (Button 0.96, ComboBox 0.982).
        public static readonly AttachedProperty<double> PressDepthProperty =
            AvaloniaProperty.RegisterAttached<SukiPress, InputElement, double>("PressDepth", double.NaN);

        private static readonly AttachedProperty<SukiPressPhysics?> PhysicsProperty =
            AvaloniaProperty.RegisterAttached<SukiPress, InputElement, SukiPressPhysics?>("Physics");

        static SukiPress()
        {
            EnableProperty.Changed.AddClassHandler<InputElement>(OnEnableChanged);
        }

        public static bool GetEnable(InputElement element) => element.GetValue(EnableProperty);
        public static void SetEnable(InputElement element, bool value) => element.SetValue(EnableProperty, value);

        public static SukiPressPreset GetPreset(InputElement element) => element.GetValue(PresetProperty);
        public static void SetPreset(InputElement element, SukiPressPreset value) => element.SetValue(PresetProperty, value);

        public static double GetPressDepth(InputElement element) => element.GetValue(PressDepthProperty);
        public static void SetPressDepth(InputElement element, double value) => element.SetValue(PressDepthProperty, value);

        private static void OnEnableChanged(InputElement element, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                // Buttons, combo boxes, etc. mark pointer events as handled in their class
                // handlers, so plain CLR subscriptions would never fire: handledEventsToo is required.
                element.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
                element.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
                // PointerEntered/Exited are direct routed events: subscribe through the CLR
                // wrappers (AddHandler with a routing strategy would never match them).
                element.PointerEntered += OnPointerEntered;
                element.PointerExited += OnPointerExited;
                element.PointerCaptureLost += OnPointerCaptureLost;
                element.DetachedFromVisualTree += OnDetachedFromVisualTree;
            }
            else
            {
                element.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
                element.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
                element.PointerEntered -= OnPointerEntered;
                element.PointerExited -= OnPointerExited;
                element.PointerCaptureLost -= OnPointerCaptureLost;
                element.DetachedFromVisualTree -= OnDetachedFromVisualTree;
                element.GetValue(PhysicsProperty)?.Dispose();
                element.SetValue(PhysicsProperty, null);
            }
        }

        /// <summary>
        /// Gets (or lazily creates) the press engine attached to a control. Exposed so
        /// hosts like the demo's benchmark page can drive the REAL press state machine
        /// programmatically — no pointer input can be synthesized in Avalonia.
        /// </summary>
        public static SukiPressPhysics EnsurePhysics(InputElement element)
        {
            var physics = element.GetValue(PhysicsProperty);
            if (physics is null)
            {
                // Profile and press depth are both resolved per gesture through the theme, so a
                // live switch or a Preset/PressDepth change applies to the next gesture, not mid-flight.
                physics = new SukiPressPhysics(element,
                    () => SukiAnimationTheme.Current.Press[GetPreset(element)],
                    () =>
                    {
                        double depth = GetPressDepth(element);
                        return double.IsNaN(depth)
                            ? SukiAnimationTheme.Current.Press[GetPreset(element)].DefaultPressDepth
                            : depth;
                    });
                element.SetValue(PhysicsProperty, physics);
            }
            return physics;
        }

        private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is InputElement element)
                element.GetValue(PhysicsProperty)?.Cancel();
        }

        private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is InputElement element)
                EnsurePhysics(element).Press();
        }

        private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is InputElement element)
                element.GetValue(PhysicsProperty)?.Release();
        }

        private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (sender is InputElement element)
                element.GetValue(PhysicsProperty)?.Release();
        }

        private static void OnPointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is InputElement element)
                EnsurePhysics(element).PointerEnter();
        }

        private static void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (sender is InputElement element)
                EnsurePhysics(element).PointerExit();
        }
    }
}
