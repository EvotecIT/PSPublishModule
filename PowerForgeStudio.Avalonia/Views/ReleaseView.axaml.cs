using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class ReleaseView : UserControl
{
    private ReleaseViewModel? _observedModel;

    public ReleaseView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe(DataContext as ReleaseViewModel);
        AttachedToVisualTree += (_, _) => Observe(DataContext as ReleaseViewModel);
        DetachedFromVisualTree += (_, _) => Observe(null);
    }

    private void Observe(ReleaseViewModel? model)
    {
        if (ReferenceEquals(_observedModel, model)) return;
        if (_observedModel is not null) _observedModel.PropertyChanged -= OnModelPropertyChanged;
        _observedModel = model;
        if (model is not null) model.PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not ReleaseViewModel model) return;
        TextBlock? heading = args.PropertyName switch
        {
            nameof(ReleaseViewModel.HasPublicationTargets) when model.HasPublicationTargets => PublicationHeading,
            nameof(ReleaseViewModel.HasPublicationReceipts) when model.HasPublicationReceipts => PublicationHeading,
            nameof(ReleaseViewModel.HasVerificationReceipts) when model.HasVerificationReceipts => VerificationHeading,
            _ => null
        };
        if (heading is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_observedModel, model) || !ReferenceEquals(DataContext, model)) return;
            PageScroll.UpdateLayout();
            if (heading.TranslatePoint(default, PageScroll) is not { } position) return;
            var target = PageScroll.Offset.Y + position.Y - 12;
            PageScroll.Offset = new Vector(PageScroll.Offset.X,
                Math.Clamp(target, 0, Math.Max(0, PageScroll.Extent.Height - PageScroll.Viewport.Height)));
        }, DispatcherPriority.Loaded);
    }
}
