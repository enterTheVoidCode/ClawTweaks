using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Read-only property indicating if PawnIO driver is installed.
    /// Controls the Install/Installed button state in TDP Method card.
    /// </summary>
    internal class PawnIOInstalledProperty : WidgetProperty<bool>
    {
        private readonly Page owner;
        private Action<bool> installedCallback;

        public PawnIOInstalledProperty(Page inOwner) : base(false, null, Function.TdpMethod_PawnIOInstalled)
        {
            owner = inOwner;
        }

        public void SetInstalledCallback(Action<bool> callback)
        {
            installedCallback = callback;
            // Don't invoke with the default value here. The widget-side default is false,
            // and UpdatePawnIOInstalledUI(false) doesn't just paint the UI — it auto-switches
            // the TDP method dropdown from PawnIO to ManufacturerWMI when PawnIO was the
            // active selection (GamingWidget.LegionGo.cs:1648-1660). Firing immediately at
            // widget startup, before any helper push has arrived, silently moves users off
            // PawnIO every restart in the window before the BatchGet response — which an end
            // user perceives as "PawnIO got uninstalled by the upgrade." The callback still
            // fires normally via NotifyPropertyChanged once the helper pushes the real state.
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && installedCallback != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    installedCallback(Value);
                });
            }
        }
    }
}
