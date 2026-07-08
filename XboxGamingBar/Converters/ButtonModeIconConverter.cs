using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Data;

namespace XboxGamingBar
{
    /// <summary>
    /// Maps a Back-Button mode name (the TypeComboBox string item -- "Gamepad"/"Keyboard"/"Mouse"/
    /// "Macro") to a Segoe Fluent Icons glyph. Glyph codepoints reused from existing FontIcon usages
    /// elsewhere in GamingWidget.xaml (Game=U+E7FC, Keyboard=U+E765, Mouse=U+E962 -- proven to render
    /// in this app already) so no unverified/new codepoint is introduced; Macro reuses U+E945, the
    /// same "Hotkeys/Trigger" glyph already used for the Hotkeys nav tab and Action tile.
    /// </summary>
    public sealed class ButtonModeIconConverter : IValueConverter
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "Gamepad",  "" },
            { "Keyboard", "" },
            { "Mouse",    "" },
            { "Macro",    "" },
        };

        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is string s && Map.TryGetValue(s, out string glyph)) ? glyph : "";

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
