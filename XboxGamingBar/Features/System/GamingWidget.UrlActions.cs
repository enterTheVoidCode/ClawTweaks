using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Shared.Data;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // User-defined "Launch Website" URLs. Source for the dropdown's user website entries.
        private List<UrlAction> _urlActions = new List<UrlAction>();
        private bool _urlActionsLoaded;
        private const string UrlActionsKey = "UrlActions_Data";

        private void InitializeUrlActions()
        {
            LoadUrlActions();
            RefreshUrlActionsList();
        }

        private void LoadUrlActions()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(UrlActionsKey, out var obj) && obj is string json)
                    _urlActions = UrlAction.FromJson(json);
                if (_urlActions == null) _urlActions = new List<UrlAction>();
            }
            catch (Exception ex)
            {
                Logger.Error($"LoadUrlActions: {ex.Message}");
                _urlActions = new List<UrlAction>();
            }
            finally { _urlActionsLoaded = true; }
        }

        private void SaveUrlActions()
        {
            if (!_urlActionsLoaded) return;
            try
            {
                ApplicationData.Current.LocalSettings.Values[UrlActionsKey] = UrlAction.ToJson(_urlActions);
            }
            catch (Exception ex) { Logger.Error($"SaveUrlActions: {ex.Message}"); }
        }

        private void RefreshUrlActionsList()
        {
            if (UrlActionsItemsControl == null) return;
            UrlActionsItemsControl.ItemsSource = null;
            UrlActionsItemsControl.ItemsSource = _urlActions;
        }

        /// <summary>Ensures the URL has a scheme so the default browser opens it as a web page.</summary>
        private static string NormalizeUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string url = raw.Trim();
            if (!url.Contains("://")) url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return null;
            return url;
        }

        private void AddUrlAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = NormalizeUrl(UrlActionTextBox?.Text);
                if (url == null) { Logger.Warn("AddUrlAction: invalid URL"); return; }
                if (_urlActions.Any(u => string.Equals(u.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Info("AddUrlAction: duplicate ignored");
                    if (UrlActionTextBox != null) UrlActionTextBox.Text = "";
                    return;
                }

                _urlActions.Add(new UrlAction(url));
                SaveUrlActions();
                RefreshUrlActionsList();
                RefreshActionDropdowns();
                if (UrlActionTextBox != null) UrlActionTextBox.Text = "";
                Logger.Info($"Added URL action: {url}");
            }
            catch (Exception ex) { Logger.Error($"AddUrlAction: {ex.Message}"); }
        }

        private void UrlActionDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button b && b.Tag is UrlAction ua)
                {
                    _urlActions.Remove(ua);
                    SaveUrlActions();
                    RefreshUrlActionsList();
                    RefreshActionDropdowns();
                    Logger.Info($"Deleted URL action: {ua.Url}");
                }
            }
            catch (Exception ex) { Logger.Error($"UrlActionDelete: {ex.Message}"); }
        }
    }
}
