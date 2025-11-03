using Shared.Enums;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class WidgetPropertyWithAdditionalUI<ValueType, UIType, AdditionalUIType> : WidgetUIProperty<ValueType, UIType> where UIType : UIElement where AdditionalUIType : UIElement
    {
        private AdditionalUIType additionalUI;

        public AdditionalUIType AdditionalUI
        {
            get { return additionalUI; }
        }

        public WidgetPropertyWithAdditionalUI(ValueType inValue, Function inFunction, UIType inUI, AdditionalUIType inAdditionalUI, Page inOwner) : base(inValue, inFunction, inUI, inOwner)
        {
            additionalUI = inAdditionalUI;
        }
    }
}
