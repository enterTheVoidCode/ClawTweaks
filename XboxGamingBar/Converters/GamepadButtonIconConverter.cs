using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
    /// <summary>
    /// Maps a gamepad-action name (the ComboBox string item, e.g. "LS Click", "A", "Xbox Button") to its
    /// Xbox glyph in Assets/ButtonIcons. Returns null for names without an icon (e.g. "Disabled") so the
    /// bound Image simply shows nothing. Used by the button-remap action dropdowns' ItemTemplate.
    /// </summary>
    public sealed class GamepadButtonIconConverter : IValueConverter
    {
        internal static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "LS",          "xbox_stick_click_l" },  // tile-hotkey dropdown uses the short "LS"/"RS" labels
            { "RS",          "xbox_stick_click_r" },
            { "LS Click",    "xbox_stick_click_l" },
            { "LS Up",       "xbox_stick_l_up" },
            { "LS Down",     "xbox_stick_l_down" },
            { "LS Left",     "xbox_stick_l_left" },
            { "LS Right",    "xbox_stick_l_right" },
            { "RS Click",    "xbox_stick_click_r" },
            { "RS Up",       "xbox_stick_r_up" },
            { "RS Down",     "xbox_stick_r_down" },
            { "RS Left",     "xbox_stick_r_left" },
            { "RS Right",    "xbox_stick_r_right" },
            { "D-Pad Up",    "xbox_dpad_up_outline" },
            { "D-Pad Down",  "xbox_dpad_down_outline" },
            { "D-Pad Left",  "xbox_dpad_left_outline" },
            { "D-Pad Right", "xbox_dpad_right_outline" },
            { "A",           "xbox_button_color_a" },
            { "B",           "xbox_button_color_b" },
            { "X",           "xbox_button_color_x" },
            { "Y",           "xbox_button_color_y" },
            { "LB",          "xbox_lb" },
            { "LT",          "xbox_lt" },
            { "RB",          "xbox_rb" },
            { "RT",          "xbox_rt" },
            { "Select",      "xbox_button_menu" },   // Select == Xbox "View/Menu" glyph (per user)
            { "Start",       "xbox_button_start" },
            { "Xbox Button", "xbox_guide" },         // Guide glyph (per user)

            // Long-label aliases used by the controller-emulation / gyro-activation dropdowns.
            { "Left Bumper",       "xbox_lb" },
            { "Right Bumper",      "xbox_rb" },
            { "Left Trigger",      "xbox_lt" },
            { "Right Trigger",     "xbox_rt" },
            { "Left Stick Click",  "xbox_stick_click_l" },
            { "Right Stick Click", "xbox_stick_click_r" },
            { "Back",              "xbox_button_view" },
            { "Left Stick",        "xbox_stick_l" },   // cursor/scroll stick pickers
            { "Right Stick",       "xbox_stick_r" },
        };

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string s && Map.TryGetValue(s, out string file))
                return new BitmapImage(new Uri($"ms-appx:///Assets/ButtonIcons/{file}.png"));
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Collapses the icon Image for ComboBox items with no entry in
    /// <see cref="GamepadButtonIconConverter"/>'s map (e.g. "Disabled", "None", "+ Button") — without
    /// this, an Image bound to a null Source still reserves its Width+Margin, visually indenting
    /// those placeholder items relative to ones that do have an icon. Shared by the same
    /// ItemTemplate the icon Image itself uses.
    /// </summary>
    public sealed class GamepadButtonIconVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is string s && GamepadButtonIconConverter.Map.ContainsKey(s))
                ? Visibility.Visible
                : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
