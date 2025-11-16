using System;
using Windows.UI.Xaml.Data;

namespace XboxGamingBar.Converter
{
    internal class RadeonChillMinFPSStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return $"Idle FPS: {value}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
