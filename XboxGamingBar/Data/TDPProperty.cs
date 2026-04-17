using Shared.Enums;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class TDPProperty : WidgetSliderProperty
    {
        /// <summary>
        /// When true, Sync() will be skipped. Used to prevent TDP sync from triggering
        /// Custom mode on Legion devices when the profile uses a preset mode (Quiet/Balanced/Performance).
        /// </summary>
        public bool SkipSync { get; set; } = false;

        public TDPProperty(int inValue, Slider inControl, Page inOwner) : base(inValue, Function.TDP, inControl, inOwner)
        {
        }

        public override async Task Sync()
        {
            if (SkipSync)
            {
                Logger.Info($"{Function} sync skipped - profile uses preset TDP mode");
                return;
            }
            await base.Sync();
        }
    }
}
