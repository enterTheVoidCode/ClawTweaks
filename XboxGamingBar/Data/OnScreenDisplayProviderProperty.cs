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

        private bool isUpdatingUI;

        private void RadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingUI) return;

            Logger.Info($"On-Screen Display changed to index {UI.SelectedIndex}");
            if (Value != UI.SelectedIndex)
            {
                SetValue(UI.SelectedIndex);
            }
        }

        /// <summary>
        /// Paints the selection when the helper reports one. Without this the page showed whichever
        /// radio button the XAML marked checked, so a saved choice of the native OSD came back looking
        /// like RTSS on the next visit - and picking it again would have been a no-op, because the
        /// stored value already matched.
        /// </summary>
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI == null || Owner == null) return;

            _ = Owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (Value < 0 || Value >= UI.Items.Count) return;
                if (UI.SelectedIndex == Value) return;

                isUpdatingUI = true;
                try { UI.SelectedIndex = Value; }
                finally { isUpdatingUI = false; }
            });
        }
    }
}
