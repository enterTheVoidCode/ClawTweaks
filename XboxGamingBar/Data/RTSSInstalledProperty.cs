using Shared.Enums;
using System;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class RTSSInstalledProperty : WidgetControlEnabledProperty<Slider>
    {
        private Action<bool> additionalCallback;

        public RTSSInstalledProperty(Slider inUI, Page inOwner) : base(Function.RTSSInstalled, inUI, inOwner)
        {
        }

        /// <summary>
        /// Sets an additional callback to be called when RTSS installed state changes.
        /// Used to update FPS Limit controls.
        /// </summary>
        public void SetAdditionalCallback(Action<bool> callback)
        {
            additionalCallback = callback;
        }

        protected override void SetControlEnabled(bool isEnabled)
        {
            base.SetControlEnabled(isEnabled);

            // Call additional callback if set (for FPS Limit controls)
            additionalCallback?.Invoke(isEnabled);
        }
    }
}
