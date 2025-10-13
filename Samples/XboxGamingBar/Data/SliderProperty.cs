using System;
using Shared.Enums;
using System.Runtime.CompilerServices;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace XboxGamingBar.Data
{
    internal class SliderProperty : WidgetControlProperty<int, Slider>
    {
        public SliderProperty(int inValue, Function inFunction, Slider inControl, Page inOwner) : base(inValue, inFunction, inControl, inOwner)
        {
            if (UI != null)
            {
                UI.ValueChanged += Slider_ValueChanged;
            }
        }

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            Logger.Info($"TDP Slider value changed from {e.OldValue} to {e.NewValue}, update property.");
            Value = (int)e.NewValue;
        }

        protected override async void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                Logger.Info($"Update {Function} slider value {Value}.");
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { UI.Value = Value; });
            }
        }
    }
}
