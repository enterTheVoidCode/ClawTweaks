using System;
using Shared.Data;
using Shared.Enums;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace XboxGamingBar.Data
{
    internal class WidgetProperty<ValueType> : GenericProperty<ValueType>
    {
        public WidgetProperty(ValueType inValue, IProperty inParentProperty, Function inFunction) : base(inValue, inParentProperty, inFunction)
        {
        }

        protected override Task<AppServiceResponse> SendMessageAsync(ValueSet request)
        {
            if (App.Connection == null)
            {
                Logger.Warn($"Widget property {function} doesn't have connection.");
                return null;
            }

            return App.Connection.SendMessageAsync(request).AsTask();
        }
    }
}
