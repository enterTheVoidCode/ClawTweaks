using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for TDP control method selection (ManufacturerWMI=0, PawnIO=1)
    /// </summary>
    internal class TdpMethodProperty : WidgetControlProperty<int, ComboBox>
    {
        // Goes true once the helper has pushed a value via BatchSync. Until then the
        // ComboBox's XAML-mount SelectionChanged is a default-render artifact, not a
        // user action, and must not push the widget's stale local default UP to helper.
        // Exposed so other code paths (e.g., LegionGo tab visibility callback) can
        // skip auto-defaulting when the helper has already supplied a value — the
        // helper-driven UI sync is deferred via Dispatcher.RunAsync, so a sibling
        // dispatcher entry that runs first must NOT race-set SelectedIndex=0.
        internal bool HasReceivedHelperSync { get; private set; }

        // Set true while NotifyPropertyChanged programmatically updates SelectedIndex,
        // so the resulting SelectionChanged is recognized as helper-driven.
        private bool isUpdatingUI;

        public TdpMethodProperty(ComboBox inUI, Page inOwner) : base((int)TdpMethod.ManufacturerWMI, Function.Settings_TdpMethod, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            // Any SetValue arriving with SuppressRemoteSync set is a helper-driven push
            // (BatchSync DOWN). Mark the property as hydrated so subsequent
            // SelectionChanged events can safely send user changes UP.
            if (SuppressRemoteSync)
            {
                HasReceivedHelperSync = true;
            }
            return base.SetValue(newValue, updatedTime);
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip XAML-mount default-fire: at first ComboBox realization the control fires
            // SelectionChanged with its default index (Manufacturer WMI = 0) before our
            // deferred dispatcher has applied helper's actual value. Pushing 0 UP at this
            // point overwrites helper's PawnIO selection on every widget cold-start.
            if (!HasReceivedHelperSync)
            {
                Logger.Info($"{Function} SelectionChanged before first helper sync - ignoring (XAML mount artifact, idx={UI?.SelectedIndex}).");
                return;
            }

            // Skip echoes triggered by our own programmatic SelectedIndex change in
            // NotifyPropertyChanged (helper-driven update).
            if (isUpdatingUI)
            {
                return;
            }

            var selectedItem = UI.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                var tagString = selectedItem.Tag as string;
                if (tagString != null)
                {
                    int newValue = TagToValue(tagString);
                    if (newValue != Value)
                    {
                        Logger.Info($"{Function} combo box updated to {tagString} (value={newValue}).");
                        SetValue(newValue);
                    }
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
                    // Find the item with matching tag value
                    string targetTag = ValueToTag(Value);
                    for (int i = 0; i < UI.Items.Count; i++)
                    {
                        var item = UI.Items[i] as ComboBoxItem;
                        if (item != null)
                        {
                            var tag = item.Tag as string;
                            if (tag == targetTag)
                            {
                                if (UI.SelectedIndex != i)
                                {
                                    Logger.Info($"{Function} combo box selected {targetTag} (index={i}).");
                                    isUpdatingUI = true;
                                    try
                                    {
                                        UI.SelectedIndex = i;
                                    }
                                    finally
                                    {
                                        isUpdatingUI = false;
                                    }
                                }
                                break;
                            }
                        }
                    }
                });
            }
        }

        private static int TagToValue(string tag)
        {
            switch (tag)
            {
                case "ManufacturerWMI": return (int)TdpMethod.ManufacturerWMI;
                case "PawnIO": return (int)TdpMethod.PawnIO;
                // WinRing0 removed - deprecated TDP method
                default: return (int)TdpMethod.ManufacturerWMI; // Default to ManufacturerWMI for Legion Go
            }
        }

        private static string ValueToTag(int value)
        {
            switch ((TdpMethod)value)
            {
                case TdpMethod.ManufacturerWMI: return "ManufacturerWMI";
                case TdpMethod.PawnIO: return "PawnIO";
                // WinRing0 removed - deprecated TDP method
                default: return "ManufacturerWMI"; // Default to ManufacturerWMI for Legion Go
            }
        }
    }
}
