using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace CherryKeyLayout.Gui.Converters
{
    public class HandleRightConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width)
                return width - 4;
            return 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
