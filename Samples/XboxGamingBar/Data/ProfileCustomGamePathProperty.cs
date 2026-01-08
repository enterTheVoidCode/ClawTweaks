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
    /// Property for custom game paths - apps always treated as games for profile detection
    /// Paths are stored as a pipe-separated string
    /// </summary>
    internal class ProfileCustomGamePathProperty : WidgetProperty<string>
    {
        private const char Separator = '|';
        private readonly ItemsControl itemsControl;
        private readonly TextBlock emptyText;
        private readonly Page owner;
        public ObservableCollection<CustomGameItem> CustomGames { get; } = new ObservableCollection<CustomGameItem>();

        public ProfileCustomGamePathProperty(ItemsControl inItemsControl, TextBlock inEmptyText, Page inOwner)
            : base("", null, Function.ProfileCustomGamePath)
        {
            itemsControl = inItemsControl;
            emptyText = inEmptyText;
            owner = inOwner;

            if (itemsControl != null)
            {
                itemsControl.ItemsSource = CustomGames;
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
        /// Adds a path to the list
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
        /// Removes a path from the list
        /// </summary>
        public void RemovePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var paths = GetPaths();
            paths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            SetValue(string.Join(Separator.ToString(), paths));
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var paths = GetPaths();
                    CustomGames.Clear();

                    foreach (var path in paths)
                    {
                        CustomGames.Add(new CustomGameItem
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
    /// Data class for custom game list items
    /// </summary>
    public class CustomGameItem
    {
        public string FullPath { get; set; }
        public string DisplayName { get; set; }
    }
}
