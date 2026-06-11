using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Minimal manager for the "External Gamepad Mode" Quick Settings tile.
    ///
    /// Bridges property-change notifications to Program.MSIClaw.cs via a static callback
    /// (OnChanged), registered during StartMSIClawControllerEmulation().
    ///
    /// true  = hide ALL handheld controllers (native MSI + virtual ViGEm) so only an external
    ///         gamepad remains visible.
    /// false = restore the prior state (virtual-controller suppression, or fully unhidden in HW mode).
    ///
    /// Not persisted — the tile always starts OFF after a helper start.
    /// </summary>
    internal class ExternalGamepadModeManager : IManager
    {
        /// <summary>Callback registered by Program.MSIClaw.cs to apply the hide/restore logic.</summary>
        internal static Action<bool> OnChanged;

        /// <summary>The helper-side property exposed for registration in Program.cs property list.</summary>
        public ExternalGamepadModeProperty ExternalGamepadMode { get; }

        public ExternalGamepadModeManager()
        {
            ExternalGamepadMode = new ExternalGamepadModeProperty(this);
        }

        /// <summary>Called by ExternalGamepadModeProperty.NotifyPropertyChanged() when the widget writes a new value.</summary>
        internal void Apply(bool on)
        {
            OnChanged?.Invoke(on);
        }

        // IManager: no polling or disposable resources.
        public void Update() { }
        public void Dispose() { }
    }
}
