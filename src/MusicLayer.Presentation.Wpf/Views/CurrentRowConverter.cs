using System.Globalization;
using System.Windows.Data;

namespace MusicLayer.Presentation.Wpf.Views;

public sealed class CurrentRowConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values is [int row, int current] && row >= 0 && row == current;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
