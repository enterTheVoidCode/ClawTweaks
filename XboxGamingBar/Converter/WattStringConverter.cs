using System;
using Windows.UI.Xaml.Data;

namespace XboxGamingBar.Converter
{
    public class WattStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return $"{value}W";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
