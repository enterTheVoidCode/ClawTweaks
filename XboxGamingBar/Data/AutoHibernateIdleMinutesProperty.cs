using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class AutoHibernateIdleMinutesProperty : WidgetSliderProperty
    {
        public AutoHibernateIdleMinutesProperty(int inValue, Slider inControl, Page inOwner)
            : base(inValue, Function.AutoHibernateIdleMinutes, inControl, inOwner)
        {
        }
    }
}
