using Microsoft.UI.Xaml.Controls;
using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class OnScreenDisplayProviderProperty : WidgetControlProperty<int, RadioButtons>
    {
        public OnScreenDisplayProviderProperty(RadioButtons inUI, Page inOwner) : base(0, Function.Settings_OnScreenDisplayProvider, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += RadioButtons_SelectionChanged;
            }
        }

        private void RadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //Logger.Info($"On-Screen Display changed to {(e.AddedItems.Count > 0 ? e.AddedItems[0].ToString() : "NO_ITEM")} index {UI.SelectedIndex}");
            Logger.Info($"On-Screen Display changed to index {UI.SelectedIndex}");
            if (UI.SelectedIndex >= 0 && Value != UI.SelectedIndex)
            {
                SetValue(UI.SelectedIndex);
            }
        }
    }
}
