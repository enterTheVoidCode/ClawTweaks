using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace XboxGamingBar.Data
{
    internal class WidgetSliderProperty : WidgetControlProperty<int, Slider>
    {
        private Windows.UI.Xaml.DispatcherTimer debounceTimer;
        private int pendingValue;
        private bool hasPendingValue;
        private const int DEBOUNCE_DELAY_MS = 500; // Wait 500ms after last change before sending

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

                // Initialize debounce timer
                debounceTimer = new Windows.UI.Xaml.DispatcherTimer();
                debounceTimer.Interval = TimeSpan.FromMilliseconds(DEBOUNCE_DELAY_MS);
                debounceTimer.Tick += DebounceTimer_Tick;
            }
        }

        public void StopDebounceTimer()
        {
            if (debounceTimer != null && debounceTimer.IsEnabled)
            {
                Logger.Info($"{Function} Stopping debounce timer.");
                debounceTimer.Stop();
                hasPendingValue = false;
            }
        }

        public void Cleanup()
        {
            if (debounceTimer != null)
            {
                debounceTimer.Stop();
                debounceTimer.Tick -= DebounceTimer_Tick;
                debounceTimer = null;
            }

            if (UI != null)
            {
                UI.ValueChanged -= Slider_ValueChanged;
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

        private void DebounceTimer_Tick(object sender, object e)
        {
            try
            {
                if (debounceTimer != null)
                {
                    debounceTimer.Stop();
                }

                // Check if connection is available before sending
                if (App.Connection == null)
                {
                    Logger.Debug($"{Function} Debounce timer tick - no connection yet, skipping send.");
                    hasPendingValue = false;
                    return;
                }

                if (hasPendingValue && pendingValue != Value)
                {
                    Logger.Info($"{Function} Debounce timer elapsed, applying pending value {pendingValue}.");
                    hasPendingValue = false;
                    SetValue(pendingValue);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{Function} Error in debounce timer tick: {ex.Message}");
            }
        }

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var newValue = (int)e.NewValue;
            if (newValue != Value)
            {
                Logger.Info($"{Function} Slider value changed from {e.OldValue} to {e.NewValue}, debouncing update.");

                // Store the pending value - do NOT update internal value yet
                // The timer will call SetValue() which updates the value and sends to helper
                pendingValue = newValue;
                hasPendingValue = true;

                // Restart the debounce timer - this delays sending to helper
                debounceTimer.Stop();
                debounceTimer.Start();
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
