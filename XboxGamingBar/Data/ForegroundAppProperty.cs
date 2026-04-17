using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Enums;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property that receives multiple foreground app paths (pipe-separated) from the helper
    /// </summary>
    internal class ForegroundAppProperty : WidgetProperty<string>
    {
        private const char Separator = '|';
        private readonly StackPanel appsContainer;
        private readonly Page owner;

        /// <summary>
        /// Callback for when the app list changes - provides the list of paths
        /// </summary>
        public Action<List<string>> OnAppsChanged { get; set; }

        public ForegroundAppProperty(StackPanel inAppsContainer, Page inOwner)
            : base("", null, Function.ForegroundApp)
        {
            appsContainer = inAppsContainer;
            owner = inOwner;
        }

        /// <summary>
        /// Gets the list of app paths from the pipe-separated value
        /// </summary>
        public List<string> GetPaths()
        {
            if (string.IsNullOrEmpty(Value))
            {
                return new List<string>();
            }
            return Value.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null)
            {
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var paths = GetPaths();
                    OnAppsChanged?.Invoke(paths);
                });
            }
        }
    }
}
