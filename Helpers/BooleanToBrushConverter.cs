using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Stalker2ModManager.Helpers
{
    /// <summary>
    /// Конвертер bool -> Brush, используется для подсветки индикатора нескольких установленных версий мода.
    /// True -> предупреждающий цвет, False -> нейтральный/информационный цвет.
    /// </summary>
    public class BooleanToBrushConverter : IValueConverter
    {
        public Brush? TrueBrush { get; set; }
        public Brush? FalseBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = false;
            if (value is bool b)
            {
                flag = b;
            }

            if (flag)
            {
                return TrueBrush ?? Brushes.Red;
            }

            return FalseBrush ?? Brushes.Yellow;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}


