using CommunityToolkit.Mvvm.ComponentModel;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class FileDeleteViewModel : ObservableObject
{
    private readonly WorkspaceViewModel _workspace;
    private CancellationTokenSource? _operationCancellation;

    public FileDeleteViewModel(
        WorkspaceViewModel workspace,
        WorkspaceFileDeletionPreview preview)
    {
        _workspace = workspace;
        Preview = preview;
        ItemName = Path.GetFileName(preview.SourcePath);
        ItemType = preview.IsDirectory ? "Folder" : "File";
        ItemCount = preview.ItemCount == 1 ? "1 item" : $"{preview.ItemCount} items";
        Size = preview.SizeBytes >= 1024 * 1024
            ? $"{preview.SizeBytes / (1024d * 1024):0.0} MiB"
            : preview.SizeBytes >= 1024
                ? $"{preview.SizeBytes / 1024d:0.#} KiB"
                : $"{preview.SizeBytes} B";
    }

    public WorkspaceFileDeletionPreview Preview { get; }
    public string ItemName { get; }
    public string ItemType { get; }
    public string ItemCount { get; }
    public string Size { get; }
    [ObservableProperty] private bool _confirmed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _error = "";
    public bool CanSubmit => Confirmed && !IsBusy;

    partial void OnConfirmedChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    public async Task<bool> SubmitAsync()
    {
        if (IsBusy || !Confirmed)
            return false;
        IsBusy = true;
        Error = "";
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
            var succeeded = await _workspace.DeleteToRecoveryAsync(Preview, cancellation.Token);
            if (!succeeded)
                Error = _workspace.Status;
            return succeeded;
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    public void CancelOperation() => _operationCancellation?.Cancel();
}
