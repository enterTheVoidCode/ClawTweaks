using NLog;
using Shared.Enums;
using System;
using System.Runtime.CompilerServices;
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

        protected override async void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

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
            if (response != null)
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
            if (response != null)
            {
                if (response.Message.TryGetValue(nameof(Content), out object responseValue))
                {
                    if (SetValue(responseValue))
                    {
                        Logger.Info($"Sync {function} value {responseValue} successfully.");
                    }
                    else
                    {
                        Logger.Info($"Got {function} value {responseValue} but can't sync.");
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

        protected abstract Task<AppServiceResponse> SendMessageAsync(ValueSet request);

        public abstract ValueSet AddValueSetContent(in ValueSet inValueSet);
    }
}
