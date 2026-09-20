using CommunityToolkit.Mvvm.ComponentModel;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>A document tab identity; previews are loaded only when the tab is selected.</summary>
public sealed partial class WorkspaceDocumentViewModel(WorkspaceDocumentReference reference) : ObservableObject
{
    public WorkspaceDocumentReference Reference { get; } = reference;
    public string Name => Path.GetFileName(Reference.Path);
    public string Caption => $"{Name} · {Path.GetFileName(Reference.WorkingCopyRoot)}";
    public string Location => Reference.Path;
    [ObservableProperty] private bool _isActive;
}
