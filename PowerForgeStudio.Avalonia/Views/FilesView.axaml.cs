using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class FilesView : UserControl
{
    public FilesView() => InitializeComponent();

    private async void BeginEdit(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        await model.EditDocumentCommand.ExecuteAsync(null);
        if (model.IsEditorVisible) EditorBox.Focus();
    }

    private async void OpenFile(object? sender, TappedEventArgs args)
    {
        if (DataContext is WorkspaceViewModel model && FileList.SelectedItem is FileItemViewModel entry)
            await model.OpenEntryAsync(entry);
    }

    private async void FileKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || DataContext is not WorkspaceViewModel model || FileList.SelectedItem is not FileItemViewModel entry) return;
        args.Handled = true;
        await model.OpenEntryAsync(entry);
    }

    private async void ManageFile(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model || sender is not Button { Tag: string operation } ||
            !Enum.TryParse<WorkspaceFileOperation>(operation, out var kind) || TopLevel.GetTopLevel(this) is not Window owner) return;
        var dialog = new FileOperationDialog(model, kind);
        await dialog.ShowDialog(owner);
    }

    private async void CopyPath(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try { await clipboard.SetTextAsync(model.SelectedFile?.FullPath ?? model.CurrentDirectory); }
        catch (Exception ex) { model.ReportFileActionError(ex); }
    }

    private void OpenExternally(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel model || model.SelectedFile is not { } entry) return;
        try { Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) { model.ReportFileActionError(ex); }
    }
}
