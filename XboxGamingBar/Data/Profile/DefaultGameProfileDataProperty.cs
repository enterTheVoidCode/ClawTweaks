using Shared.Data;
using Shared.Enums;
using Shared.Utilities;
using System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property containing the serialized default game profile data.
    /// Updates the profile settings display when data changes.
    /// </summary>
    internal class DefaultGameProfileDataProperty : WidgetProperty<string>
    {
        private readonly Page owner;
        private Action<DefaultGameProfile?> dataCallback;

        public DefaultGameProfileDataProperty(Page inOwner) : base("", null, Function.DefaultGameProfileData)
        {
            owner = inOwner;
        }

        public void SetDataCallback(Action<DefaultGameProfile?> callback)
        {
            dataCallback = callback;
            // Invoke immediately with current value
            if (!string.IsNullOrEmpty(Value))
            {
                var profile = ParseProfile(Value);
                callback?.Invoke(profile);
            }
            else
            {
                callback?.Invoke(null);
            }
        }

        /// <summary>
        /// Gets the current profile if available.
        /// </summary>
        public DefaultGameProfile? GetProfile()
        {
            return ParseProfile(Value);
        }

        private DefaultGameProfile? ParseProfile(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }

            try
            {
                return XmlHelper.FromXMLString<DefaultGameProfile>(xml);
            }
            catch
            {
                return null;
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (owner != null && dataCallback != null)
            {
                var profile = ParseProfile(Value);
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    dataCallback(profile);
                });
            }
        }
    }
}
