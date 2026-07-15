using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClawTweaksSetup.Navigation;

namespace ClawTweaksSetup.Ui
{
    /// <summary>Semantic status of a check row / stepper item.</summary>
    public enum StatusKind { Ok, Warning, Error, Info, Working }

    /// <summary>Shared builders for the code-built phase content (consistent look, English base text).</summary>
    public static class UiHelpers
    {
        public static Brush Text => (Brush)Application.Current.Resources["TextBrush"];
        public static Brush Subtle => (Brush)Application.Current.Resources["SubtleTextBrush"];
        public static Brush Ok => (Brush)Application.Current.Resources["OkBrush"];
        public static Brush Warn => (Brush)Application.Current.Resources["WarnBrush"];
        public static Brush Error => (Brush)Application.Current.Resources["ErrorBrush"];
        public static Brush Accent => (Brush)Application.Current.Resources["AccentBrush"];
        public static Brush Card => (Brush)Application.Current.Resources["CardBrush"];

        public static Brush BrushFor(StatusKind k)
        {
            switch (k)
            {
                case StatusKind.Ok: return Ok;
                case StatusKind.Warning: return Warn;
                case StatusKind.Error: return Error;
                default: return Subtle;
            }
        }

        public static TextBlock Title(string t) => new TextBlock
        {
            Text = t,
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = Text,
            Margin = new Thickness(0, 0, 0, 12),
        };

        /// <summary>Small dim caption, e.g. "Last checked 12:34:56".</summary>
        public static TextBlock Caption(string t) => new TextBlock
        {
            Text = t,
            FontSize = 14,
            Foreground = Subtle,
            Opacity = 0.8,
            Margin = new Thickness(0, 2, 0, 14),
        };

        public static TextBlock Body(string t) => new TextBlock
        {
            Text = t,
            FontSize = 19,
            LineHeight = 28,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Subtle,
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 18),
        };

        /// <summary>A coloured badge with a glyph: ✓ ok, ! warning, ✕ error, · info, … working.</summary>
        public static FrameworkElement Badge(StatusKind kind, double size = 30)
        {
            Brush fill; string glyph; Brush fg = System.Windows.Media.Brushes.White;
            switch (kind)
            {
                case StatusKind.Ok:      fill = Ok;   glyph = "✓"; break; // ✓
                case StatusKind.Warning: fill = Warn; glyph = "!";      fg = System.Windows.Media.Brushes.Black; break;
                case StatusKind.Error:   fill = Error; glyph = "✕"; break; // ✕
                case StatusKind.Working: fill = new SolidColorBrush(Color.FromRgb(0x3A,0x3F,0x4B)); glyph = "…"; break; // …
                // Info = detected, differs from the default but is OK/neutral → grey dash.
                default:                 fill = new SolidColorBrush(Color.FromRgb(0x3A,0x3F,0x4B)); glyph = "–"; break;
            }

            var grid = new Grid { Width = size, Height = size };
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Fill = fill });
            grid.Children.Add(new TextBlock
            {
                Text = glyph,
                Foreground = fg,
                FontSize = size * 0.62,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, kind == StatusKind.Working ? -size * 0.18 : 0, 0, 0),
            });
            return grid;
        }

        /// <summary>A status row: status badge + bold name + detail line.</summary>
        public static Border StatusRow(StatusKind kind, string name, string detail)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = Badge(kind);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = Text,
            });
            if (!string.IsNullOrEmpty(detail))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = detail,
                    FontSize = 16,
                    Foreground = Subtle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            // Left accent stripe matching the status colour for stronger at-a-glance meaning.
            return new Border
            {
                Background = Card,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = BrushFor(kind),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Child = grid,
            };
        }

        /// <summary>
        /// Prominent, coloured call-to-action banner with the Ⓐ glyph, e.g.
        /// "Ⓐ  Install the missing tools". Draws the eye to the required action.
        /// </summary>
        public static Border ActionCallout(string text, StatusKind tone = StatusKind.Warning)
        {
            var accent = BrushFor(tone);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = new Image
            {
                Source = Glyphs.For(PadButton.A),
                Width = 40, Height = 40,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
            };
            RenderOptions.SetBitmapScalingMode(glyph, BitmapScalingMode.HighQuality);
            row.Children.Add(glyph);
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Text,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22,
                    ((SolidColorBrush)accent).Color.R, ((SolidColorBrush)accent).Color.G, ((SolidColorBrush)accent).Color.B)),
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 16),
                Child = row,
            };
        }

        /// <summary>Tool row: status badge, name, what-it-does description, and a coloured status line.</summary>
        public static Border ToolRow(StatusKind kind, string name, string whatItDoes, string status)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = Badge(kind);
            badge.VerticalAlignment = VerticalAlignment.Top;
            badge.Margin = new Thickness(0, 2, 16, 0);
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = name, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Text,
            });
            stack.Children.Add(new TextBlock
            {
                Text = whatItDoes, FontSize = 15, Foreground = Subtle,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            });
            stack.Children.Add(new TextBlock
            {
                Text = status, FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = BrushFor(kind), Margin = new Thickness(0, 6, 0, 0),
            });
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            return new Border
            {
                Background = Card,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = BrushFor(kind),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Child = grid,
            };
        }

        /// <summary>Prominent mode banner (e.g. "Mode: Update") — not a check, just context.</summary>
        public static Border ModeBanner(string label, string sub)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Text,
            });
            if (!string.IsNullOrEmpty(sub))
                stack.Children.Add(new TextBlock
                {
                    Text = sub,
                    FontSize = 16,
                    Foreground = Subtle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x33)),
                BorderBrush = Accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20, 16, 20, 16),
                Margin = new Thickness(0, 0, 0, 18),
                Child = stack,
            };
        }
    }
}
