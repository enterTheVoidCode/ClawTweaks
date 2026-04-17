using Shared.Enums;
using System.Threading.Tasks;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Windows 11 OS Power Mode (power slider).
    /// 0 = Best Power Efficiency, 1 = Balanced, 2 = Best Performance
    /// </summary>
    internal class OSPowerModeProperty : WidgetProperty<int>
    {
        /// <summary>
        /// When true, Sync() will be skipped. Used to prevent OS Power Mode sync from
        /// overwriting profile-loaded values during app startup or resume.
        /// </summary>
        public bool SkipSync { get; set; } = false;

        public OSPowerModeProperty() : base(1, null, Function.OSPowerMode)
        {
        }

        public override async Task Sync()
        {
            if (SkipSync)
            {
                Logger.Info($"{Function} sync skipped - profile has OS Power Mode set");
                return;
            }
            await base.Sync();
        }
    }
}
