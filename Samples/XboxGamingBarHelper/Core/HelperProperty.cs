using Shared.Data;
using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace XboxGamingBarHelper.Core
{
    internal class HelperProperty<T, TManager> : GenericProperty<T> where TManager : IManager
    {
        protected TManager manager;

        protected HelperProperty(T inValue) : base(inValue)
        {
            manager = default;
        }

        protected HelperProperty(T inValue, IProperty inParentProperty) : base(inValue, inParentProperty)
        {
            manager = default;
        }

        protected HelperProperty(T inValue, IProperty inParentProperty, Function inFunction) : base(inValue, inParentProperty, inFunction)
        {
            manager = default;
        }

        public HelperProperty(T inValue, IProperty inParentProperty, Function inFunction, TManager inManager) : base(inValue, inParentProperty, inFunction)
        {
            manager = inManager;
        }

        public TManager Manager
        {
            get { return manager; }
        }

        protected override Task<AppServiceResponse> SendMessageAsync(ValueSet request)
        {
            // First try Named Pipes (works when running via scheduled task)
            if (Program.IsPipeConnected)
            {
                try
                {
                    var pipeMsg = Shared.IPC.PipeMessage.FromValueSet(request);
                    if (Program.SendPipeMessage(pipeMsg))
                    {
                        Logger.Debug($"Property {Function} sent update via Named Pipe.");
                        // Return a completed task - pipe doesn't return AppServiceResponse
                        return Task.FromResult<AppServiceResponse>(null);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Property {Function} failed to send via pipe: {ex.Message}");
                }
            }

            // Fall back to AppService (works when running in package context)
            if (Manager == null)
            {
                Logger.Debug($"Property {Function}'s manager is null, skipping AppService send.");
                return null;
            }

            if (Manager.Connection == null)
            {
                Logger.Debug($"Property {Function}'s manager doesn't have connection, skipping AppService send.");
                return null;
            }

            return Manager.Connection.SendMessageAsync(request).AsTask();
        }
    }
}
