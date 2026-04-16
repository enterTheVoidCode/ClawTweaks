using System;
using Shared.Data;
using Shared.Enums;
using System.Threading.Tasks;
using Windows.Foundation.Collections;

namespace XboxGamingBar.Data
{
    internal class WidgetProperty<ValueType> : GenericProperty<ValueType>
    {
        public WidgetProperty(ValueType inValue, IProperty inParentProperty, Function inFunction) : base(inValue, inParentProperty, inFunction)
        {
        }

        /// <summary>
        /// Override Sync to use the unified App.SendMessageAsync for Named Pipe communication.
        /// </summary>
        public override async Task Sync()
        {
            if (!App.IsConnected)
            {
                Logger.Warn($"Can't sync {function} - no connection.");
                return;
            }

            var request = new ValueSet
            {
                { nameof(Command), (int)Command.Get },
                { nameof(Function), (int)function },
            };

            var response = await App.SendMessageAsync(request);
            if (response != null)
            {
                if (response.TryGetValue(nameof(Content), out object responseValue))
                {
                    if (response.TryGetValue(nameof(UpdatedTime), out object updatedTimeValue))
                    {
                        var updatedTime = Convert.ToInt64(updatedTimeValue);
                        if (SetValue(responseValue, updatedTime))
                        {
                            Logger.Info($"Sync {function} value {responseValue} successfully.");
                        }
                        else
                        {
                            Logger.Warn($"Got {function} value {responseValue} but can't sync.");
                        }
                    }
                    else
                    {
                        Logger.Warn($"Can't get updated time when trying to sync property {function}.");
                    }
                }
                else
                {
                    Logger.Warn($"Got empty response when trying to sync property {function}.");
                }
            }
            else
            {
                Logger.Warn($"Got no response when trying to sync property {function}.");
            }
        }

        protected override async Task<object> SendMessageAsync(ValueSet request)
        {
            if (!App.IsConnected)
            {
                Logger.Debug($"Widget property {function} - not connected.");
                return null;
            }

            return await App.SendMessageAsync(request);
        }

        /// <summary>
        /// Override NotifyPropertyChanged to use App.SendMessageAsync for Named Pipe communication.
        /// We call InvokePropertyChanged directly to fire the INotifyPropertyChanged event.
        /// </summary>
        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            // Call InvokePropertyChanged directly to trigger INotifyPropertyChanged events
            InvokePropertyChanged(propertyName);

            // Skip sending to remote if suppressed (e.g., during batch sync)
            if (SuppressRemoteSync)
            {
                return;
            }

            if (!App.IsConnected)
            {
                Logger.Debug($"Widget property {function} - skipping remote sync (not connected).");
                return;
            }

            try
            {
                var request = new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function), (int)function },
                };
                request = AddValueSetContent(request);

                var response = await App.SendMessageAsync(request);
                if (response != null)
                {
                    if (response.TryGetValue(nameof(Content), out object responseValue))
                    {
                        Logger.Debug($"Notify property {function} changed {responseValue}.");
                    }
                    else if (response.TryGetValue("Error", out object errorValue))
                    {
                        Logger.Warn($"Error notifying property {function}: {errorValue}");
                    }
                    else
                    {
                        if (function != Function.None)
                        {
                            Logger.Debug($"Got response for property {function} (no Content field).");
                        }
                    }
                }
                else
                {
                    Logger.Debug($"Got no response when notifying property {function}.");
                }
            }
            catch (Exception ex)
            {
                // Catch exceptions to prevent async void from crashing the process
                Logger.Debug($"NotifyPropertyChanged failed for {function}: {ex.Message}");
            }
        }
    }
}
