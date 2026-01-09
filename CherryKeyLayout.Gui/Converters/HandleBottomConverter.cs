using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace CherryKeyLayout.Gui.Converters
{
    public class HandleBottomConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double height)
                return height - 4;
            return 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
