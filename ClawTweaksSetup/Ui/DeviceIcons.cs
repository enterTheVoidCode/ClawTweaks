using System;
using System.Windows.Media.Imaging;
using ClawTweaksSetup.Core;

namespace ClawTweaksSetup.Ui
{
    /// <summary>Resolves the bundled device photo for a <see cref="DeviceDetect.Model"/> — mirrors
    /// Navigation/Glyphs.cs's resolver pattern for the controller button images.</summary>
    public static class DeviceIcons
    {
        private const string Base = "pack://application:,,,/Assets/devices/";

        public static BitmapImage For(DeviceDetect.Model model)
        {
            string file;
            switch (model)
            {
                case DeviceDetect.Model.A2VM: file = "a2vm-removebg-preview.png"; break;
                case DeviceDetect.Model.Ex: file = "8ex-removebg-preview.png"; break;
                default: return null;
            }

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(Base + file, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
    }
}
