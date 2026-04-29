using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Single property that gates all four AFMF 2.x extended-control ComboBoxes
    /// (Algorithm, SearchMode, PerformanceMode, FastMotionResponse). One pipe Function
    /// fans out to multiple controls because the helper only sends one V1Supported
    /// signal — the standard WidgetControlEnabledProperty maps 1:1 to a control, and
    /// the pipe dispatcher is keyed by Function, so we can't register four of those
    /// against the same Function.
    /// </summary>
    internal class AMDFluidMotionFrameV1SupportedProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private readonly Control[] controls;

        public AMDFluidMotionFrameV1SupportedProperty(Page inOwner, params Control[] inControls)
            : base(false, null, Function.AMDFluidMotionFrameV1Supported)
        {
            owner = inOwner;
            controls = inControls;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner == null || controls == null) return;
            await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (var c in controls)
                {
                    if (c != null) c.IsEnabled = Value;
                }
            });
        }
    }
}
