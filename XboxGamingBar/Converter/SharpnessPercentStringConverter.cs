using System;
using Windows.UI.Xaml.Data;

namespace XboxGamingBar.Converter
{
    internal class SharpnessPercentStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return $"{value}% Sharpness";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
