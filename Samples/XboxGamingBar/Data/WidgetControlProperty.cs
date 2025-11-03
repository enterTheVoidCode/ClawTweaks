using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetControlProperty<ValueType, UIType> : WidgetUIProperty<ValueType, UIType> where UIType : Control
    {
        public WidgetControlProperty(ValueType inValue, Function inFunction, UIType inUI, Page inOwner) : base(inValue, inFunction, inUI, inOwner)
        {
        }

        public override async Task Sync()
        {

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => SetControlEnabled(false));
            }

            await base.Sync();

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => SetControlEnabled(true));
            }
        }

        protected virtual void SetControlEnabled(bool isEnabled)
        {
            Logger.Debug($"{GetType().Name} Set {UI.Name} {(isEnabled ? "enabled" : "disabled")}.");
            if (UI != null)
            {
                UI.IsEnabled = isEnabled;
            }
        }
    }
}
