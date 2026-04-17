using Shared.Enums;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetUIProperty<ValueType, UIType> : WidgetProperty<ValueType> where UIType : UIElement
    {
        private UIType ui;
        public UIType UI
        {
            get { return ui; }
        }

        private readonly Page owner;
        public Page Owner { get { return owner; } }

        public WidgetUIProperty(ValueType inValue, Function inFunction, UIType inUI, Page inOwner) : base(inValue, null, inFunction)
        {
            ui = inUI;
            owner = inOwner;
        }
    }
}
