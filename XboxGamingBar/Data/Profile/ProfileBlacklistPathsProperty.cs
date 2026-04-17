using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for blacklisted paths - apps never treated as games
    /// Paths are stored as a pipe-separated string
    /// </summary>
    internal class ProfileBlacklistPathsProperty : WidgetProperty<string>
    {
        private const char Separator = '|';
        private readonly ItemsControl itemsControl;
        private readonly TextBlock emptyText;
        private readonly Page owner;
        public ObservableCollection<BlacklistItem> BlacklistedApps { get; } = new ObservableCollection<BlacklistItem>();

        public ProfileBlacklistPathsProperty(ItemsControl inItemsControl, TextBlock inEmptyText, Page inOwner)
            : base("", null, Function.ProfileBlacklistPaths)
        {
            itemsControl = inItemsControl;
            emptyText = inEmptyText;
            owner = inOwner;

            if (itemsControl != null)
            {
                itemsControl.ItemsSource = BlacklistedApps;
            }
        }

        /// <summary>
        /// Gets the list of paths from the value
        /// </summary>
        public List<string> GetPaths()
        {
            if (string.IsNullOrEmpty(Value))
            {
                return new List<string>();
            }
            return Value.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        /// <summary>
        /// Adds a path to the blacklist
        /// </summary>
        public void AddPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var paths = GetPaths();
            // Don't add duplicates
            if (paths.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            paths.Add(path);
            SetValue(string.Join(Separator.ToString(), paths));
        }

        /// <summary>
        /// Removes a path from the blacklist
        /// </summary>
        public void RemovePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var paths = GetPaths();
            paths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            SetValue(string.Join(Separator.ToString(), paths));
        }

        /// <summary>
        /// Checks if a path is blacklisted
        /// </summary>
        public bool ContainsPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return GetPaths().Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var paths = GetPaths();
                    BlacklistedApps.Clear();

                    foreach (var path in paths)
                    {
                        BlacklistedApps.Add(new BlacklistItem
                        {
                            FullPath = path,
                            DisplayName = System.IO.Path.GetFileName(path)
                        });
                    }

                    if (emptyText != null)
                    {
                        emptyText.Visibility = paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                });
            }
        }
    }

    /// <summary>
    /// Data class for blacklist items
    /// </summary>
    public class BlacklistItem
    {
        public string FullPath { get; set; }
        public string DisplayName { get; set; }
    }
}
