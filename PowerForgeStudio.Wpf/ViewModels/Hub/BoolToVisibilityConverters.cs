using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class BoolToVisibleConverter : IValueConverter
{
    public static readonly BoolToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public sealed class BoolToCollapsedConverter : IValueConverter
{
    public static readonly BoolToCollapsedConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
