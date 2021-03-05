using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace TraderVI.WPF.Converters
{
    public class ValueToForegroundColorConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            SolidColorBrush brush = new SolidColorBrush(Colors.Green);//(SolidColorBrush)(new BrushConverter().ConvertFrom("#f03434"));

            double doubleValue = 0.0;
            double.TryParse(value.ToString(), out doubleValue);

            if (doubleValue < 0)
                brush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#f03434"));

            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
