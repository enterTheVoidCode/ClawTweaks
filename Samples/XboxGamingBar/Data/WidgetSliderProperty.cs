using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace XboxGamingBar.Data
{
    internal class WidgetSliderProperty : WidgetControlProperty<int, Slider>
    {
        public WidgetSliderProperty(int inValue, Function inFunction, Slider inControl, Page inOwner) : base(inValue, inFunction, inControl, inOwner)
        {
            if (UI != null)
            {
                UI.ValueChanged += Slider_ValueChanged;
                //UI.DragEnter += Slider_DragEnter;
                //UI.DragStarting += Slider_DragStarting;
                //UI.DragOver += Slider_DragOver;
                //UI.DragLeave += Slider_DragLeave;
                UI.Value = inValue;
            }
        }

        //private void Slider_DragLeave(object sender, Windows.UI.Xaml.DragEventArgs e)
        //{
        //    Logger.Info($"{Function} Slider drag leave {e.Data.ToString()}.");
        //}

        //private void Slider_DragOver(object sender, Windows.UI.Xaml.DragEventArgs e)
        //{
        //    Logger.Info($"{Function} Slider drag over {e.Data.ToString()}.");
        //}

        //private void Slider_DragStarting(Windows.UI.Xaml.UIElement sender, Windows.UI.Xaml.DragStartingEventArgs args)
        //{
        //    Logger.Info($"{Function} Slider drag starting {args.Data.ToString()}.");
        //}

        //private void Slider_DragEnter(object sender, Windows.UI.Xaml.DragEventArgs e)
        //{
        //    Logger.Info($"{Function} Slider drag enter {e.Data.ToString()}.");
        //}

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var newValue = (int)e.NewValue;
            if (newValue != Value)
            {
                Logger.Info($"{Function} Slider value changed from {e.OldValue} to {e.NewValue}, update property.");
                SetValue(newValue);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
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
