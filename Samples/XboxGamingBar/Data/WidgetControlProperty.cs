using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetControlProperty<ValueType, UIType> : WidgetProperty<ValueType, UIType> where UIType : Control
    {
        public WidgetControlProperty(ValueType inValue, Function inFunction, UIType inUI, Page inOwner) : base(inValue, inFunction, inUI, inOwner)
        {
        }

        public override async Task Sync()
        {

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { UI.IsEnabled = false; });
            }

            await base.Sync();

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { UI.IsEnabled = true; });
            }
        }
    }
}
