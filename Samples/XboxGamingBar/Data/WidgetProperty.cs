using Shared.Data;
using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetProperty<ValueType, UIType> : GenericProperty<ValueType> where UIType : UIElement
    {
        private UIType ui;
        public UIType UI
        {
            get { return ui; }
        }

        private readonly Page owner;
        public Page Owner { get { return owner; } }

        public WidgetProperty(ValueType inValue, Function inFunction, UIType inUI, Page inOwner) : base(inValue, null, inFunction)
        {
            ui = inUI;
            owner = inOwner;
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
