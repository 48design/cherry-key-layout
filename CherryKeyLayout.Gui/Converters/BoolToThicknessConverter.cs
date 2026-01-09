using System;
using Avalonia.Data.Converters;
using Avalonia;
using System.Globalization;

namespace CherryKeyLayout.Gui.Converters
{
    public class BoolToThicknessConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isSelected = value is bool b && b;
            double thickness = 0;
            if (isSelected && parameter != null && double.TryParse(parameter.ToString(), out var t))
                thickness = t;
            else if (isSelected)
                thickness = 2;
            return new Thickness(thickness);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Thickness t)
                return t.Left > 0;
            return false;
        }
    }
}
