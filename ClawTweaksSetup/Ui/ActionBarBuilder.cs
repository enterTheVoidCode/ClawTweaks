using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClawTweaksSetup.Navigation;

namespace ClawTweaksSetup.Ui
{
    /// <summary>Builds shared controller and pointer footer actions.</summary>
    public static class ActionBarBuilder
    {
        private const double GlyphSize = 24;
        private const double LabelFontSize = 14;

        public static UIElement BuildChip(PadButton button, string label, bool enabled, System.Action onClick)
        {
            var glyph = new Image
            {
                Source = Glyphs.For(button),
                Width = GlyphSize, Height = GlyphSize,
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
                FontSize = LabelFontSize,
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

            // Pointer input invokes the same action as the controller.
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

        /// <summary>Builds a noninteractive hint aligned with action tiles.</summary>
        public static UIElement BuildHint(ImageSource glyphSource, string label)
        {
            var glyph = new Image
            {
                Source = glyphSource,
                Width = GlyphSize, Height = GlyphSize,
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
                FontSize = LabelFontSize,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["SubtleTextBrush"],
                Margin = new Thickness(10, 0, 0, 0),
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(glyph);
            content.Children.Add(text);

            return new Border
            {
                Padding = new Thickness(8, 5, 12, 5),
                Margin = new Thickness(3),
                MinHeight = 40,
                Child = content,
            };
        }
    }
}
