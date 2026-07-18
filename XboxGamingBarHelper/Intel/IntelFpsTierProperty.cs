using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Intel
{
    /// <summary>
    /// Receives the Intel FPS cap from the widget and applies it via IGCL FRAME_LIMIT.
    /// Value is now an ARBITRARY FPS (0 = off, else the fps target) — the old 3-tier meaning
    /// (1/2/3) is migrated to 60/40/30 at the profile-read points (see IntelGpuManager.MigrateTierToFps).
    /// The name is kept for pipe/profile back-compat; the exclusion machinery only checks &gt; 0.
    /// </summary>
    internal class IntelFpsTierProperty : HelperProperty<int, IntelGpuManager>
    {
        public IntelFpsTierProperty(IntelGpuManager manager)
            : base(0, null, Function.IntelFpsTier, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Manager.ApplyFrameLimit(Value);
        }
    }
}
