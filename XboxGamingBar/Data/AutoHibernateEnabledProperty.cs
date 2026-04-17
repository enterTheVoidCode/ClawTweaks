using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AutoHibernateEnabledProperty : WidgetToggleProperty
    {
        public AutoHibernateEnabledProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.AutoHibernateEnabled, inUI, inOwner)
        {
        }
    }
}
