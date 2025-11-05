using Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// This kind of property decides whether a widget control is enabled or disabled.
    /// </summary>
    internal class WidgetControlEnabledProperty<UIType> : WidgetControlProperty<bool, UIType> where UIType : Control
    {
        public WidgetControlEnabledProperty(Function inFunction, UIType inUI, Page inOwner) : base(false, inFunction, inUI, inOwner)
        {
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Logger.Debug($"{(Value ? "Enabled" : "Disabled")} {UI.Name} property changed.");
                    SetControlEnabled(Value);
                });
            }
        }

        public override async Task Sync()
        {
            await base.Sync();

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Logger.Debug($"{(Value ? "Enabled" : "Disabled")} {UI.Name} at sync.");
                    SetControlEnabled(Value);
                });
            }
        }
    }
}
