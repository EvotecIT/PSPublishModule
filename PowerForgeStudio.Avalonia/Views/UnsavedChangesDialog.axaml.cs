using Avalonia.Controls;
using Avalonia.Interactivity;
using PowerForgeStudio.Avalonia.ViewModels;

namespace PowerForgeStudio.Avalonia.Views;

public sealed partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog() => InitializeComponent();
    public UnsavedChangesDialog(IReadOnlyList<WorkspaceDocumentViewModel> documents) : this()
        => DocumentNames.Text = string.Join("\n", documents.Select(document => document.Location));
    private void Cancel(object? sender, RoutedEventArgs args) => Close(UnsavedChangesChoice.Cancel);
    private void Discard(object? sender, RoutedEventArgs args) => Close(UnsavedChangesChoice.Discard);
    private void Save(object? sender, RoutedEventArgs args) => Close(UnsavedChangesChoice.Save);
}
