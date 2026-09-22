using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class BuildView : UserControl
{
    private BuildViewModel? _observedModel;

    public BuildView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe(DataContext as BuildViewModel);
        AttachedToVisualTree += (_, _) => Observe(DataContext as BuildViewModel);
        DetachedFromVisualTree += (_, _) => Observe(null);
    }

    private void Observe(BuildViewModel? model)
    {
        if (ReferenceEquals(_observedModel, model)) return;
        if (_observedModel is not null) _observedModel.PropertyChanged -= OnModelPropertyChanged;
        _observedModel = model;
        if (model is not null) model.PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not BuildViewModel model) return;
        TextBlock? heading = args.PropertyName switch
        {
            nameof(BuildViewModel.BuildResult) when model.BuildResult is not null => BuildResultHeading,
            nameof(BuildViewModel.IsTaskRunning) when !model.IsTaskRunning && model.HasTaskRun => TaskResultHeading,
            _ => null
        };
        if (heading is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_observedModel, model) && ReferenceEquals(DataContext, model))
            {
                PageScroll.UpdateLayout();
                var point = heading.TranslatePoint(default, PageScroll);
                if (point is { } position)
                {
                    var target = PageScroll.Offset.Y + position.Y - 12;
                    PageScroll.Offset = new Vector(PageScroll.Offset.X,
                        Math.Clamp(target, 0, Math.Max(0, PageScroll.Extent.Height - PageScroll.Viewport.Height)));
                }
            }
        }, DispatcherPriority.Loaded);
    }
}
