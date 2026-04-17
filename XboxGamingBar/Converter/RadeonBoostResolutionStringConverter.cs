using System;
using Windows.UI.Xaml.Data;

namespace XboxGamingBar.Converter
{
    internal class RadeonBoostResolutionStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if ((double)value == 0)
            {
                return "Performance";
            }
            else
            {
                return "Quality";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
