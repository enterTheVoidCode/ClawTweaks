using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Shared.Data;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // User-defined "Program Actions" (.exe / .ps1). Source for the dropdown's user entries.
        // The dropdown/binding bakes the path in, so this list is only the picker + management UI.
        private List<ProgramAction> _programActions = new List<ProgramAction>();
        private bool _programActionsLoaded;
        private const string ProgramActionsKey = "ProgramActions_Data";

        private void InitializeProgramActions()
        {
            LoadProgramActions();
            RefreshProgramActionsList();
        }

        private void LoadProgramActions()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(ProgramActionsKey, out var obj) && obj is string json)
                    _programActions = ProgramAction.FromJson(json);
                if (_programActions == null) _programActions = new List<ProgramAction>();
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadProgramActions: {ex.Message}");
                _programActions = new List<ProgramAction>();
            }
            finally { _programActionsLoaded = true; }
        }

        private void SaveProgramActions()
        {
            if (!_programActionsLoaded) return;
            try
            {
                ApplicationData.Current.LocalSettings.Values[ProgramActionsKey] = ProgramAction.ToJson(_programActions);
            }
            catch (Exception ex) { Logger.Error($"SaveProgramActions: {ex.Message}"); }
        }

        private void RefreshProgramActionsList()
        {
            if (ProgramActionsItemsControl == null) return;
            ProgramActionsItemsControl.ItemsSource = null;
            ProgramActionsItemsControl.ItemsSource = _programActions;
        }

        private async void BrowseProgramAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.ComputerFolder,
                    ViewMode = PickerViewMode.List
                };
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add(".ps1");
                var file = await picker.PickSingleFileAsync();
                if (file != null && ProgramActionPathTextBox != null)
                    ProgramActionPathTextBox.Text = file.Path;
            }
            catch (Exception ex)
            {
                // Game Bar overlay can occasionally reject the picker; the user can still paste a path.
                Logger.Warn($"BrowseProgramAction failed: {ex.Message}");
            }
        }

        private void AddProgramAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = ProgramActionPathTextBox?.Text?.Trim();
                if (string.IsNullOrEmpty(path)) { Logger.Warn("AddProgramAction: empty path"); return; }
                // Strip surrounding quotes a user may have pasted.
                path = path.Trim('"');

                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".exe" && ext != ".ps1")
                {
                    Logger.Warn($"AddProgramAction: not an .exe/.ps1: {path}");
                    return;
                }
                if (_programActions.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Info("AddProgramAction: duplicate ignored");
                    if (ProgramActionPathTextBox != null) ProgramActionPathTextBox.Text = "";
                    return;
                }

                _programActions.Add(new ProgramAction(path));
                SaveProgramActions();
                RefreshProgramActionsList();
                RefreshActionDropdowns();
                if (ProgramActionPathTextBox != null) ProgramActionPathTextBox.Text = "";
                Logger.Info($"Added program action: {path}");
            }
            catch (Exception ex) { Logger.Error($"AddProgramAction: {ex.Message}"); }
        }

        private void ProgramActionDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button b && b.Tag is ProgramAction pa)
                {
                    _programActions.Remove(pa);
                    SaveProgramActions();
                    RefreshProgramActionsList();
                    RefreshActionDropdowns();
                    Logger.Info($"Deleted program action: {pa.Path}");
                }
            }
            catch (Exception ex) { Logger.Error($"ProgramActionDelete: {ex.Message}"); }
        }
    }
}
