using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class StorageView : UserControl
{
    public StorageView() => InitializeComponent();

    private void OpenSelected(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not StorageViewModel { SelectedEntry: { Exists: true } entry })
            return;
        try
        {
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (DataContext is StorageViewModel model)
            {
                model.Status = "Could not open the selected working copy.";
                model.Output = ViewModels.StudioDisplayError.From(ex);
            }
        }
    }

    private async void ReviewRemoval(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not StorageViewModel model || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        if (!await model.ReviewSelectedRemovalAsync())
            return;
        await new WorktreeRemovalDialog { DataContext = model }.ShowDialog(owner);
    }

    private async void ReviewPrune(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not StorageViewModel model || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        if (!await model.ReviewSelectedPruneAsync())
            return;
        await new WorktreePruneDialog { DataContext = model }.ShowDialog(owner);
    }
}
