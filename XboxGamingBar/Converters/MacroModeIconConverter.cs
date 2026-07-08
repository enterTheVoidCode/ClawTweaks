using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace XboxGamingBar
{
    /// <summary>
    /// Maps a Macro repeat-mode name (the ModeComboBox string item — "Once"/"Repeat while held"/
    /// "Hold (press again to release)"/"Hold + Repeat (press again to stop)") to its primary Segoe
    /// Fluent Icons glyph. "Once" reuses the "Launch" Play glyph (U+E768), "Repeat while held"
    /// reuses the "Reload releases" Refresh glyph (U+E72C) — both already proven to render in this
    /// app. "Hold" and the combined "Hold + Repeat" primarily show Lock (U+E72E), a standard Segoe
    /// MDL2/Fluent codepoint not otherwise used yet in this project.
    /// </summary>
    public sealed class MacroModeIconConverter : IValueConverter
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "Once", "" },
            { "Repeat while held", "" },
            { "Hold (press again to release)", "" },
            { "Hold + Repeat (press again to stop)", "" },
        };

        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is string s && Map.TryGetValue(s, out string glyph)) ? glyph : "";

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Secondary glyph shown only for the combined "Hold + Repeat" mode (Refresh, U+E72C, next to
    /// the primary Lock glyph) — the two icons together visually convey "held + repeating". Empty
    /// for every other mode so <see cref="MacroModeSecondaryIconVisibilityConverter"/> collapses it.
    /// </summary>
    public sealed class MacroModeSecondaryIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value as string) == "Hold + Repeat (press again to stop)" ? "" : "";

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Collapses the secondary FontIcon for every Macro mode except the combined "Hold + Repeat"
    /// one (which shows both a Lock and a Refresh glyph side by side).
    /// </summary>
    public sealed class MacroModeSecondaryIconVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value as string) == "Hold + Repeat (press again to stop)" ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
