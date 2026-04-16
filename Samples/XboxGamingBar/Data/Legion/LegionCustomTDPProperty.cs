using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Custom TDP Slow (SPL) in watts (5-45W)
    /// </summary>
    internal class LegionCustomTDPSlowProperty : WidgetSliderProperty
    {
        public LegionCustomTDPSlowProperty(Slider inControl, Page inOwner) : base(15, Function.LegionCustomTDPSlow, inControl, inOwner)
        {
        }
    }

    /// <summary>
    /// Property for Legion Custom TDP Fast (SPPL) in watts (5-55W)
    /// </summary>
    internal class LegionCustomTDPFastProperty : WidgetSliderProperty
    {
        public LegionCustomTDPFastProperty(Slider inControl, Page inOwner) : base(25, Function.LegionCustomTDPFast, inControl, inOwner)
        {
        }
    }

    /// <summary>
    /// Property for Legion Custom TDP Peak (FPPT) in watts (5-65W)
    /// </summary>
    internal class LegionCustomTDPPeakProperty : WidgetSliderProperty
    {
        public LegionCustomTDPPeakProperty(Slider inControl, Page inOwner) : base(35, Function.LegionCustomTDPPeak, inControl, inOwner)
        {
        }
    }
}
