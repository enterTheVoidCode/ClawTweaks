using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClawTweaksSetup.Navigation;

namespace ClawTweaksSetup.Ui
{
    /// <summary>
    /// Builds a footer action tile (boxed glyph + label, pad- and mouse-clickable) — shared between
    /// <see cref="MainWindow"/>'s per-phase actions and <see cref="CenterMenuWindow"/>'s fixed
    /// X/A/Y/B actions, so both windows render the same "which button does what" tiles identically.
    /// </summary>
    public static class ActionBarBuilder
    {
        public static UIElement BuildChip(PadButton button, string label, bool enabled, System.Action onClick)
        {
            var glyph = new Image
            {
                Source = Glyphs.For(button),
                Width = 24, Height = 24,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetBitmapScalingMode(glyph, BitmapScalingMode.HighQuality);

            var text = new TextBlock
            {
                Text = label,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                Margin = new Thickness(10, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(glyph);
            content.Children.Add(text);

            // The whole tile is clickable too (mouse/touch), with the same action as the pad press.
            var btn = new Button
            {
                Content = content,
                Style = (Style)Application.Current.Resources["ControllerActionTile"],
                Focusable = false,
                IsEnabled = enabled,
            };
            btn.Click += (_, __) => onClick();
            return btn;
        }
    }
}
