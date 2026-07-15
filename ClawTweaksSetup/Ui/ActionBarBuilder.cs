using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClawTweaksSetup.Navigation;

namespace ClawTweaksSetup.Ui
{
    /// <summary>
    /// Builds a footer action-bar chip (glyph + label, pad- and mouse-clickable) — shared between
    /// <see cref="MainWindow"/>'s per-phase actions and <see cref="CenterMenuWindow"/>'s fixed
    /// X/A/Y/B actions, so both windows render the same "which button does what" chips identically.
    /// </summary>
    public static class ActionBarBuilder
    {
        public static UIElement BuildChip(PadButton button, string label, bool enabled, System.Action onClick)
        {
            var glyph = new Image
            {
                Source = Glyphs.For(button),
                Width = 52, Height = 52,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetBitmapScalingMode(glyph, BitmapScalingMode.HighQuality);

            var text = new TextBlock
            {
                Text = label,
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(glyph);
            content.Children.Add(text);

            // Whole chip is a clickable button too (mouse/touch), same action as the pad press.
            var btn = new Button
            {
                Content = content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(10, 0, 10, 0),
                Cursor = Cursors.Hand,
                Focusable = false,
                IsEnabled = enabled,
                Opacity = enabled ? 1.0 : 0.35,
                Template = TransparentButtonTemplate(),
            };
            btn.Click += (_, __) => onClick();
            return btn;
        }

        public static ControlTemplate TransparentButtonTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "bd");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.PaddingProperty, new Thickness(0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;

            var over = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), "bd"));
            t.Triggers.Add(over);
            return t;
        }
    }
}
