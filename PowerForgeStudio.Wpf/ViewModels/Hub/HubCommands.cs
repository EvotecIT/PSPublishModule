using System.Windows.Input;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public static class HubCommands
{
    public static RoutedUICommand FocusSearchCommand { get; } = new("Focus Search", "FocusSearch", typeof(HubCommands),
        new InputGestureCollection { new KeyGesture(Key.P, ModifierKeys.Control) });

    public static RoutedUICommand OpenTerminalCommand { get; } = new("Open Terminal", "OpenTerminal", typeof(HubCommands),
        new InputGestureCollection { new KeyGesture(Key.T, ModifierKeys.Control) });

    public static RoutedUICommand OpenFilesCommand { get; } = new("Open Files", "OpenFiles", typeof(HubCommands),
        new InputGestureCollection { new KeyGesture(Key.E, ModifierKeys.Control) });

    public static RoutedUICommand ClearSearchCommand { get; } = new("Clear Search", "ClearSearch", typeof(HubCommands),
        new InputGestureCollection { new KeyGesture(Key.Escape) });
}
