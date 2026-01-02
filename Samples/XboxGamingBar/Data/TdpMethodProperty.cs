using System;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for TDP control method selection (ManufacturerWMI=0, PawnIO=1, WinRing0=2)
    /// </summary>
    internal class TdpMethodProperty : WidgetControlProperty<int, ComboBox>
    {
        public TdpMethodProperty(ComboBox inUI, Page inOwner) : base((int)TdpMethod.PawnIO, Function.Settings_TdpMethod, inUI, inOwner)
        {
            if (UI != null)
            {
                UI.SelectionChanged += ComboBox_SelectionChanged;
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
                                    UI.SelectedIndex = i;
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
                case "WinRing0": return (int)TdpMethod.WinRing0;
                default: return (int)TdpMethod.PawnIO;
            }
        }

        private static string ValueToTag(int value)
        {
            switch ((TdpMethod)value)
            {
                case TdpMethod.ManufacturerWMI: return "ManufacturerWMI";
                case TdpMethod.PawnIO: return "PawnIO";
                case TdpMethod.WinRing0: return "WinRing0";
                default: return "PawnIO";
            }
        }
    }
}
