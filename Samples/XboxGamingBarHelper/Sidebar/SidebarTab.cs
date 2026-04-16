using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XboxGamingBarHelper.Sidebar
{
    internal enum ControlType { Tile, Toggle, TileCycle, ModeSelector, Slider }

    internal abstract class SidebarTab
    {
        // Theme colors (shared with SidebarWindow)
        protected static readonly Color BgColor = (Color)ColorConverter.ConvertFromString("#25282C");
        protected static readonly Color CardColor = (Color)ColorConverter.ConvertFromString("#30343A");
        protected static readonly Color CardBorderColor = (Color)ColorConverter.ConvertFromString("#50555C");
        protected static readonly Color AccentColor = (Color)ColorConverter.ConvertFromString("#0078D4");
        protected static readonly Color GreenColor = (Color)ColorConverter.ConvertFromString("#4CAF50");
        protected static readonly Color GrayColor = (Color)ColorConverter.ConvertFromString("#555555");
        protected static readonly Color TextColor = Colors.White;
        protected static readonly Color SubtextColor = (Color)ColorConverter.ConvertFromString("#888888");
        protected static readonly Color SectionColor = (Color)ColorConverter.ConvertFromString("#AAAAAA");

        // Tile colors
        protected static readonly Color TileColor = (Color)ColorConverter.ConvertFromString("#1A1C1E");
        protected static readonly Color TileActiveColor = (Color)ColorConverter.ConvertFromString("#1A2530");

        internal abstract StackPanel ContentPanel { get; }
        internal abstract Border[] FocusableControls { get; }
        internal abstract void AdjustLeft(int focusIndex);
        internal abstract void AdjustRight(int focusIndex);
        internal abstract void Activate(int focusIndex, ref bool isAdjusting);
        internal abstract void Refresh();

        internal virtual ControlType GetControlType(int focusIndex) => ControlType.Tile;
        internal virtual Slider GetSlider(int focusIndex) => null;
        internal virtual void CommitSliderValue(int focusIndex) { }
        internal virtual void PointerCycleForward(int focusIndex) { }

        #region UI Helpers

        protected static TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(SectionColor),
                Margin = new Thickness(0, 10, 0, 2),
            };
        }

        protected static Border CreateControlCard(out StackPanel content)
        {
            content = new StackPanel();
            return new Border
            {
                Background = new SolidColorBrush(CardColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 3, 0, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(CardBorderColor),
                Child = content,
            };
        }

        protected static Border CreateTile(string icon, string label, out TextBlock stateText, string defaultState)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Icon (Segoe MDL2 Assets)
            stack.Children.Add(new TextBlock
            {
                Text = icon,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            });

            // Label
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(SectionColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            });

            // State text
            stateText = new TextBlock
            {
                Text = defaultState,
                FontSize = 12,
                Foreground = new SolidColorBrush(SubtextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            stack.Children.Add(stateText);

            return new Border
            {
                Background = new SolidColorBrush(TileColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                MinHeight = 80,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(CardBorderColor),
                Child = stack,
            };
        }

        protected static Grid CreateSliderHeader(string label, out TextBlock valueText, string defaultValue)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 14,
                Foreground = new SolidColorBrush(TextColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            valueText = new TextBlock
            {
                Text = defaultValue,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(GreenColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(valueText);

            return grid;
        }

        protected static Grid CreateModeHeader(string label, out TextBlock modeText, string defaultMode)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 14,
                Foreground = new SolidColorBrush(TextColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var valuePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            valuePanel.Children.Add(new TextBlock
            {
                Text = "\u25C0 ",
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtextColor),
                VerticalAlignment = VerticalAlignment.Center,
            });
            modeText = new TextBlock
            {
                Text = defaultMode,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(GreenColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            valuePanel.Children.Add(modeText);
            valuePanel.Children.Add(new TextBlock
            {
                Text = " \u25B6",
                FontSize = 11,
                Foreground = new SolidColorBrush(SubtextColor),
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(valuePanel, 1);
            grid.Children.Add(valuePanel);

            return grid;
        }

        protected static Grid CreateToggleRow(string label, out Border toggleBorder, out TextBlock toggleText)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 14,
                Foreground = new SolidColorBrush(TextColor),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            toggleText = new TextBlock
            {
                Text = "OFF",
                FontSize = 11,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            toggleBorder = new Border
            {
                Background = new SolidColorBrush(GrayColor),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 4, 12, 4),
                Child = toggleText,
            };
            Grid.SetColumn(toggleBorder, 1);
            grid.Children.Add(toggleBorder);

            return grid;
        }

        protected static void UpdateToggleVisual(Border border, TextBlock text, bool isOn)
        {
            border.Background = new SolidColorBrush(isOn ? GreenColor : GrayColor);
            text.Text = isOn ? "ON" : "OFF";
        }

        protected static void UpdateTileState(Border tileBorder, TextBlock stateText, string state, bool isActive)
        {
            tileBorder.Background = new SolidColorBrush(isActive ? TileActiveColor : TileColor);
            stateText.Text = state;
            stateText.Foreground = new SolidColorBrush(isActive ? GreenColor : SubtextColor);
        }

        #endregion
    }
}
