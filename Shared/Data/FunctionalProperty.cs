using NLog;
using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.Foundation.Collections;

namespace Shared.Data
{
    /// <summary>
    /// A value that should be shared and synced between the helper and the widget for a specific purpose, like TDP or OSD.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    public abstract class FunctionalProperty : Property
    {
        protected Function function;
        public Function Function
        {
            get { return function; }
        }

        /// <summary>
        /// The last time this property was updated (for sync ordering).
        /// </summary>
        public abstract long UpdatedTime { get; }

        /// <summary>
        /// When true, NotifyPropertyChanged will update UI but won't send value to remote.
        /// Used during batch sync to prevent echoing values back to sender.
        /// </summary>
        public bool SuppressRemoteSync { get; set; }

        public FunctionalProperty() : base()
        {
            function = Function.OSD;
        }

        public FunctionalProperty(IProperty inParentProperty) : base(inParentProperty)
        {
            function = Function.OSD;
        }

        public FunctionalProperty(IProperty inParentProperty, Function inFunction) : base(inParentProperty)
        {
            function = inFunction;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Skip sending to remote if suppressed (e.g., during batch sync)
            if (SuppressRemoteSync)
            {
                return;
            }

            try
            {
                var request = new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function),(int)function },
                };
                request = AddValueSetContent(request);

                var sentMessage = SendMessageAsync(request);
                if (sentMessage == null)
                {
                    Logger.Debug($"Can't send {function} value changed message - not connected.");
                    return;
                }

                var response = await sentMessage;
                if (response is ValueSet responseValueSet)
                {
                    if (responseValueSet.TryGetValue(nameof(Content), out object responseValue))
                    {
                        Logger.Debug($"Notify property {function} changed {responseValue}.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Catch exceptions to prevent async void from crashing the process
                Logger.Debug($"NotifyPropertyChanged failed for {function}: {ex.Message}");
            }
        }

        public override async Task Sync()
        {
            var request = new ValueSet
            {
                { nameof(Command), (int)Command.Get },
                { nameof(Function),(int)function },
            };

            var sentMessage = SendMessageAsync(request);
            if (sentMessage == null)
            {
                Logger.Debug($"Can't sync {function} value - not connected.");
                return;
            }

            var response = await sentMessage;
            if (response is ValueSet responseValueSet)
            {
                if (responseValueSet.TryGetValue(nameof(Content), out object responseValue))
                {
                    if (responseValueSet.TryGetValue(nameof(UpdatedTime), out object updatedTimeValue))
                    {
                        var updatedTime = (long)updatedTimeValue;
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
                Logger.Debug($"Got no response when trying to sync property {function}.");
            }
        }

        protected abstract Task<object> SendMessageAsync(ValueSet request);

        public abstract ValueSet AddValueSetContent(in ValueSet inValueSet);

        /// <summary>
        /// Called after batch sync completes to allow derived classes to perform post-sync actions.
        /// Override this in WidgetControlProperty to enable controls after batch sync.
        /// </summary>
        public virtual Task OnBatchSyncCompleted()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends the current value to the other side (widget/helper) without triggering local property change handlers.
        /// Use this when syncing values from hardware to the UI without causing a feedback loop.
        /// </summary>
        public async void SyncToRemote()
        {
            try
            {
                var request = new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function),(int)function },
                };
                request = AddValueSetContent(request);

                var sentMessage = SendMessageAsync(request);
                if (sentMessage == null)
                {
                    Logger.Debug($"Can't send {function} sync to remote message - not connected.");
                    return;
                }

                var response = await sentMessage;
                if (response is ValueSet responseValueSet)
                {
                    if (responseValueSet.TryGetValue(nameof(Content), out object responseValue))
                    {
                        Logger.Debug($"Synced property {function} to remote: {responseValue}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"SyncToRemote failed for {function}: {ex.Message}");
            }
        }
    }
}
