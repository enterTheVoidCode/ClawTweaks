using Shared.Enums;
using System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion light color as hex string "#RRGGBB"
    /// Uses ColorPicker for selection
    /// </summary>
    internal class LegionLightColorProperty : WidgetControlProperty<string, Windows.UI.Xaml.Controls.ColorPicker>
    {
        public LegionLightColorProperty(Windows.UI.Xaml.Controls.ColorPicker inUI, Page inOwner) : base("#FFFFFF", Function.LegionLightColor, inUI, inOwner)
        {
            // Don't initialize color in constructor - let the sync from helper set it
        }

        /// <summary>
        /// Called from the ColorChanged event handler in code-behind
        /// </summary>
        public void OnColorChanged(Color color)
        {
            string hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (hexColor != Value)
            {
                Logger.Info($"{Function} color picker updated to {hexColor}.");
                SetValue(hexColor);
            }
        }

        private void UpdateUIColor()
        {
            try
            {
                if (UI != null && !string.IsNullOrEmpty(Value))
                {
                    Color color = ParseHexColor(Value);
                    UI.Color = color;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update color picker: {ex.Message}");
            }
        }

        private Color ParseHexColor(string hexColor)
        {
            if (string.IsNullOrEmpty(hexColor))
                return Colors.White;

            string hex = hexColor.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
            return Colors.White;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UpdateUIColor();
                });
            }
        }
    }
}
