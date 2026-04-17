using Shared.Data;
using Shared.Enums;
using System;
using System.Threading.Tasks;
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

        protected override Task<object> SendMessageAsync(ValueSet request)
        {
            // Send via Named Pipe
            if (Program.IsPipeConnected)
            {
                try
                {
                    var pipeMsg = Shared.IPC.PipeMessage.FromValueSet(request);
                    if (Program.SendPipeMessage(pipeMsg))
                    {
                        Logger.Debug($"Property {Function} sent update via Named Pipe.");
                        return Task.FromResult<object>(null);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Property {Function} failed to send via pipe: {ex.Message}");
                }
            }

            Logger.Debug($"Property {Function} not sent - pipe not connected.");
            return null;
        }
    }
}
