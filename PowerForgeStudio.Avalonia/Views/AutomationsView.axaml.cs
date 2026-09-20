using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class AutomationsView : UserControl
{
    public AutomationsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(
            () => PageScroll.Offset = new Vector(0, 0), DispatcherPriority.Loaded);
    }
}
