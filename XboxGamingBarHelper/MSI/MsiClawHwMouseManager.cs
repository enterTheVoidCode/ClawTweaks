using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.MSI
{
    /// <summary>
    /// Minimal manager for the MSI Claw HW-mouse killswitch (Quick Settings tile / action).
    ///
    /// Bridges property-change notifications to Program.MSIClaw.cs via a static callback (OnChanged),
    /// registered during StartMSIClawControllerEmulation().
    ///
    /// true  = force firmware Desktop mouse mode (EnterHwMouseKillswitch)
    /// false = restore controller mode (ExitHwMouseKillswitch)
    ///
    /// Not persisted — always starts OFF after a helper start (helper always boots in controller mode).
    /// The helper is authoritative: <see cref="SetStateFromHelper"/> pushes state to the widget tile
    /// (e.g. auto-recovery or a no-op correction) WITHOUT re-triggering Apply.
    /// </summary>
    internal class MsiClawHwMouseManager : IManager
    {
        /// <summary>Callback registered by Program.MSIClaw.cs; true = ON (killswitch), false = OFF.</summary>
        internal static Action<bool> OnChanged;

        /// <summary>The helper-side property exposed for registration in Program.cs property list.</summary>
        public MsiClawHwMouseProperty MsiClawHwMouse { get; }

        // Set while a helper-authoritative push is in flight, so the re-entrant NotifyPropertyChanged
        // does not bounce back into Apply() (which would re-run Enter/Exit and loop).
        private bool _applying;

        public MsiClawHwMouseManager()
        {
            MsiClawHwMouse = new MsiClawHwMouseProperty(this);
        }

        /// <summary>Called by MsiClawHwMouseProperty.NotifyPropertyChanged() when the widget writes a new value.</summary>
        internal void Apply(bool on)
        {
            if (_applying) return; // ignore the echo from SetStateFromHelper()
            OnChanged?.Invoke(on);
        }

        /// <summary>
        /// Helper-authoritative state push: update the widget tile without re-invoking Apply.
        /// Used when the helper changes the killswitch state itself (auto-recovery switched an
        /// accidental HW mouse back, or an ON request no-op'd because the controller wasn't running).
        /// </summary>
        internal void SetStateFromHelper(bool on)
        {
            if (MsiClawHwMouse.Value == on) return;
            _applying = true;
            try { MsiClawHwMouse.SetValue(on); }
            finally { _applying = false; }
        }

        // IManager: no polling or disposable resources.
        public void Update() { }
        public void Dispose() { }
    }
}
