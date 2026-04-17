using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using muxc = Microsoft.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion light color as hex string "#RRGGBB"
    /// Uses ColorPicker for selection
    /// </summary>
    internal class LegionLightColorProperty : WidgetControlProperty<string, muxc.ColorPicker>
    {
        /// <summary>
        /// Flag to indicate when the UI is being updated programmatically (from helper sync).
        /// When true, ColorChanged events should not trigger profile saves.
        /// </summary>
        public bool IsUpdatingUI { get; private set; }

        /// <summary>
        /// Tracks whether a color has been explicitly set from a saved profile.
        /// When true, sync from helper will be ignored to preserve the user's saved color.
        /// </summary>
        public bool HasSavedProfileColor { get; private set; }

        public LegionLightColorProperty(muxc.ColorPicker inUI, Page inOwner) : base("#FFFFFF", Function.LegionLightColor, inUI, inOwner)
        {
            // Don't initialize color in constructor - let the sync from helper set it
        }

        /// <summary>
        /// Called when a color is loaded from a saved profile.
        /// Marks the color as user-saved to prevent it from being overwritten by helper sync.
        /// </summary>
        public void SetFromProfile(string colorHex)
        {
            if (!string.IsNullOrEmpty(colorHex) && !IsDefaultWhite(colorHex))
            {
                HasSavedProfileColor = true;
                Logger.Info($"{Function} color loaded from profile: {colorHex} (will be preserved during sync)");
            }
            SetValue(colorHex);
        }

        /// <summary>
        /// Override Sync to preserve user's saved color.
        /// The widget is the source of truth for lighting settings, not the helper.
        /// The helper has a hardcoded default of #FFFFFF which would overwrite the user's saved color.
        /// </summary>
        public override async Task Sync()
        {
            // If we have a saved profile color, skip sync from helper to preserve it
            // The helper doesn't persist lighting settings and would return its default #FFFFFF
            if (HasSavedProfileColor)
            {
                Logger.Info($"{Function} skipping sync - preserving saved profile color: {Value}");

                // Still need to enable the control after "sync"
                if (UI != null && Owner != null)
                {
                    await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        UI.IsEnabled = true;
                        UpdateUIColor();
                    });
                }
                return;
            }

            // No saved color, proceed with normal sync from helper
            await base.Sync();
        }

        /// <summary>
        /// Checks if a color hex string represents the default white color.
        /// </summary>
        private bool IsDefaultWhite(string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex))
                return true;

            string normalized = colorHex.TrimStart('#').ToUpperInvariant();
            return normalized == "FFFFFF";
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
                    // Set flag to prevent ColorChanged from triggering profile saves
                    IsUpdatingUI = true;
                    try
                    {
                        UI.Color = color;
                    }
                    finally
                    {
                        IsUpdatingUI = false;
                    }
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
