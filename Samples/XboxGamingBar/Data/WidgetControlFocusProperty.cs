using Shared.Enums;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    // Detects whether a Control is focused or not.
    internal class WidgetControlFocusProperty<UIType> : WidgetControlProperty<bool, UIType> where UIType : Control
    {
        public WidgetControlFocusProperty(bool inValue, Function inFunction, UIType inUI, Page inOwner) : base(inValue, inFunction, inUI, inOwner)
        {
            if (UI != null)
            {
                value = UI.FocusState != FocusState.Unfocused;
                UI.FocusEngaged += UI_FocusEngaged;
                UI.FocusDisengaged += UI_FocusDisengaged;
                UI.LostFocus += UI_LostFocus;
            }
        }

        private void UI_FocusDisengaged(Control sender, FocusDisengagedEventArgs args)
        {
            if (Value)
            {
                Logger.Info($"{UI.Name} of function {function} FocusDisengaged, set to false.");
                SetValue(false);
            }
            else
            {
                Logger.Info($"{UI.Name} of function {function} FocusDisengaged, already false.");
            }
        }

        private void UI_FocusEngaged(Control sender, FocusEngagedEventArgs args)
        {
            Logger.Info($"{UI.Name} of function {function} FocusEngaged");
            SetValue(true);
        }

        private void UI_LostFocus(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (Value)
            {
                Logger.Info($"{UI.Name} of function {function} LostFocus, set to false.");
                SetValue(false);
            }
            else
            {
                Logger.Info($"{UI.Name} of function {function} LostFocus, already false.");
            }
        }
    }
}
