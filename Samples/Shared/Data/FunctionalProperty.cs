using NLog;
using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
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
                    Logger.Error($"Can't send {function} value changed message.");
                    return;
                }

                var response = await sentMessage;
                if (response != null && response.Message != null)
                {
                    if (response.Message.TryGetValue(nameof(Content), out object responseValue))
                    {
                        Logger.Debug($"Notify property {function} changed {responseValue}.");
                    }
                    else
                    {
                        if (function != Function.None)
                        {
                            Logger.Warn($"Got empty response when notifying property {function}.");
                        }
                    }
                }
                else
                {
                    Logger.Warn($"Got no response when notifying property {function}.");
                }
            }
            catch (Exception ex)
            {
                // Catch exceptions to prevent async void from crashing the process
                // This can happen when the AppService connection is broken
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
                Logger.Error($"Can't sync {function} value.");
                return;
            }

            var response = await sentMessage;
            if (response != null && response.Message != null)
            {
                if (response.Message.TryGetValue(nameof(Content), out object responseValue))
                {
                    if (response.Message.TryGetValue(nameof(UpdatedTime), out object updatedTimeValue))
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
            else if (response != null && response.Message == null)
            {
                Logger.Warn($"Got null Message when trying to sync property {function}.");
            }
            else
            {
                Logger.Warn($"Got no response when trying to sync property {function}.");
            }
        }

        protected abstract Task<AppServiceResponse> SendMessageAsync(ValueSet request);

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
                    Logger.Debug($"Can't send {function} sync to remote message.");
                    return;
                }

                var response = await sentMessage;
                if (response != null && response.Message != null)
                {
                    if (response.Message.TryGetValue(nameof(Content), out object responseValue))
                    {
                        Logger.Debug($"Synced property {function} to remote: {responseValue}.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Catch exceptions to prevent async void from crashing the process
                // This can happen when the AppService connection is broken (e.g., widget closed)
                Logger.Debug($"SyncToRemote failed for {function}: {ex.Message}");
            }
        }
    }
}
