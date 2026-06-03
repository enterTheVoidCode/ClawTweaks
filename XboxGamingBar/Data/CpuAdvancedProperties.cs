using Shared.Enums;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Generic int-valued ComboBox property for the CPU advanced section (ToothNClaw port).
    /// Each ComboBoxItem carries an integer string Tag; the Tag is the value sent to the helper.
    /// Used for CPU Boost Mode, Scheduling Policy and P/E core max frequency (MHz).
    /// Mirrors TdpMethodProperty's "ignore XAML-mount default fire until first helper sync" guard.
    /// </summary>
    internal class CpuIntComboProperty : WidgetControlProperty<int, ComboBox>
    {
        internal bool HasReceivedHelperSync { get; private set; }
        private bool isUpdatingUI;

        public CpuIntComboProperty(int initialValue, Function function, ComboBox inUI, Page inOwner)
            : base(initialValue, function, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            if (SuppressRemoteSync)
            {
                HasReceivedHelperSync = true;
            }
            return base.SetValue(newValue, updatedTime);
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignore the default fire before the helper has pushed the real value.
            if (!HasReceivedHelperSync)
            {
                Logger.Info($"{Function} SelectionChanged before first helper sync - ignoring (idx={UI?.SelectedIndex}).");
                return;
            }
            if (isUpdatingUI) return;

            if (UI?.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int newValue))
            {
                if (newValue != Value)
                {
                    Logger.Info($"{Function} combo updated to {newValue}.");
                    SetValue(newValue);
                }
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    for (int i = 0; i < UI.Items.Count; i++)
                    {
                        if (UI.Items[i] is ComboBoxItem item && item.Tag is string tag
                            && int.TryParse(tag, out int v) && v == Value)
                        {
                            if (UI.SelectedIndex != i)
                            {
                                isUpdatingUI = true;
                                try { UI.SelectedIndex = i; }
                                finally { isUpdatingUI = false; }
                            }
                            break;
                        }
                    }
                });
            }
        }
    }
}
