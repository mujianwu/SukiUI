using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace SukiUI.ControlsAnimation
{
    /// <summary>
    /// Unified open/close animation facade for template popups, over the shared
    /// <see cref="SukiPopupPhysics"/> engine driven by the single <see cref="SukiTicker"/>
    /// loop (see <see cref="SukiPopupProfile"/> for the calibrated feels). Enable it with
    /// <c>SukiPopupAnimation.Enable="True"</c> from a style setter and optionally pick a
    /// feel with <c>SukiPopupAnimation.Preset</c> (ComboBox by default).
    /// The active profile is resolved per open/close through <see cref="SukiAnimationTheme.Current"/>,
    /// so a live profile switch (or a Preset change) applies to the next transition without
    /// tearing down the engine's popup wiring.
    /// Template contract: the host's template must contain a <c>Popup</c> named
    /// <c>PART_SukiPopup</c> whose content root is a control named
    /// <c>PART_LayoutTransform</c> (the animated root), with an optional
    /// <c>PART_ItemsPresenter</c> for the item cascade. Host support is resolved through
    /// <c>SukiPopupHosts</c> (ComboBox today); Enable on an unsupported control type is a
    /// logged no-op.
    /// </summary>
    public class SukiPopupAnimation
    {
        public static readonly AttachedProperty<bool> EnableProperty =
            AvaloniaProperty.RegisterAttached<SukiPopupAnimation, TemplatedControl, bool>("Enable");

        public static readonly AttachedProperty<SukiPopupPreset> PresetProperty =
            AvaloniaProperty.RegisterAttached<SukiPopupAnimation, TemplatedControl, SukiPopupPreset>("Preset", SukiPopupPreset.ComboBox);

        private static readonly AttachedProperty<SukiPopupPhysics?> PhysicsProperty =
            AvaloniaProperty.RegisterAttached<SukiPopupAnimation, TemplatedControl, SukiPopupPhysics?>("Physics");

        static SukiPopupAnimation()
        {
            EnableProperty.Changed.AddClassHandler<TemplatedControl>(OnEnableChanged);
        }

        public static bool GetEnable(TemplatedControl element) => element.GetValue(EnableProperty);
        public static void SetEnable(TemplatedControl element, bool value) => element.SetValue(EnableProperty, value);

        public static SukiPopupPreset GetPreset(TemplatedControl element) => element.GetValue(PresetProperty);
        public static void SetPreset(TemplatedControl element, SukiPopupPreset value) => element.SetValue(PresetProperty, value);

        // Unlike SukiPress (engine created lazily on the first gesture), the popup engine
        // must exist BEFORE any gesture: it owns the popup lifecycle wiring from the start.
        private static void OnEnableChanged(TemplatedControl element, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                if (element.GetValue(PhysicsProperty) is { })
                    return; // already wired (style re-application)
                if (SukiPopupHosts.Resolve(element) is not { } hostAdapter)
                {
                    Debug.WriteLine($"SukiPopupAnimation: no host adapter for '{element.GetType().Name}' — Enable ignored.");
                    return;
                }
                // Profile resolved per open/close through the theme (live switches and Preset
                // changes apply to the next transition, keeping the popup wiring alive).
                var physics = new SukiPopupPhysics(element,
                    () => SukiAnimationTheme.Current.Popup[GetPreset(element)], hostAdapter);
                element.SetValue(PhysicsProperty, physics);
            }
            else
            {
                element.GetValue(PhysicsProperty)?.Dispose();
                element.SetValue(PhysicsProperty, null);
            }
        }
    }
}
