using System;
using System.Windows.Media.Imaging;

namespace ClawTweaksSetup.Navigation
{
    /// <summary>Resolves the bundled Xbox glyph image for a <see cref="PadButton"/>.</summary>
    public static class Glyphs
    {
        private const string Base = "pack://application:,,,/Assets/xbox/";

        public static BitmapImage For(PadButton b)
        {
            string file;
            switch (b)
            {
                case PadButton.A: file = "xbox_button_color_a.png"; break;
                case PadButton.B: file = "xbox_button_color_b.png"; break;
                case PadButton.X: file = "xbox_button_color_x.png"; break;
                case PadButton.Y: file = "xbox_button_color_y.png"; break;
                case PadButton.Menu: file = "xbox_button_menu.png"; break;
                default: file = "xbox_button_view.png"; break;
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
