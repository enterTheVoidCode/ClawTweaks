using System;
using Windows.UI.Xaml.Data;

namespace XboxGamingBar.Converter
{
    internal class MegahertzStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return $"{value}Mhz";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
