using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    /// <summary>
    /// Decorates the controller-button / stick picker ComboBoxes (mouse click mapping,
    /// gyro activation, cursor/scroll stick) with the matching Xbox icon next to each label,
    /// so they look like the button-remap dropdowns. Selection is by Tag/SelectedIndex, so
    /// replacing the item Content with an [icon + label] panel does not affect the mapping.
    /// </summary>
    public sealed partial class GamingWidget
    {
        private GamepadButtonIconConverter _btnComboIconConv;

        /// <summary>Add the matching Xbox icon to each plain-text ComboBoxItem. Idempotent.</summary>
        private void DecorateButtonComboItems(ComboBox combo)
        {
            if (combo == null) return;
            if (_btnComboIconConv == null) _btnComboIconConv = new GamepadButtonIconConverter();

            foreach (var obj in combo.Items)
            {
                if (!(obj is ComboBoxItem item)) continue;
                if (!(item.Content is string label)) continue;  // already decorated / non-text

                var icon = _btnComboIconConv.Convert(label, typeof(ImageSource), null, null) as ImageSource;

                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (icon != null)
                    panel.Children.Add(new Image
                    {
                        Source = icon,
                        Width = 18,
                        Height = 18,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                else
                    // Spacer so labels without an icon (None, M1/M2/M3, Y1, …) stay aligned.
                    panel.Children.Add(new Border { Width = 18, Height = 18 });

                panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                item.Content = panel;
            }
        }

        /// <summary>Decorate every button/stick picker dropdown once (called at init).</summary>
        private void DecorateAllButtonComboBoxes()
        {
            DecorateButtonComboItems(ControllerEmulationMouseLeftClickButtonComboBox);
            DecorateButtonComboItems(ControllerEmulationMouseRightClickButtonComboBox);
            DecorateButtonComboItems(ControllerEmulationGyroActivationButtonComboBox);
            DecorateButtonComboItems(ViiperStickGyroActivationButtonComboBox);
            DecorateButtonComboItems(ControllerEmulationMouseCursorStickComboBox);
            DecorateButtonComboItems(ControllerEmulationMouseScrollStickComboBox);
        }
    }
}
