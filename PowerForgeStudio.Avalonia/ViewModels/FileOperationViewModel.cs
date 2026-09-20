using CommunityToolkit.Mvvm.ComponentModel;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class FileOperationViewModel : ObservableObject
{
    private readonly WorkspaceViewModel _workspace;
    private readonly WorkspaceFileOperation _operation;
    private readonly string? _source;
    private CancellationTokenSource? _operationCancellation;
    public FileOperationViewModel(WorkspaceViewModel workspace, WorkspaceFileOperation operation)
    {
        _workspace = workspace;
        _operation = operation;
        Root = workspace.ActiveWorkingCopyRoot;
        _source = workspace.SelectedFile?.FullPath;
        Title = operation switch
        {
            WorkspaceFileOperation.CreateFile => "Create file",
            WorkspaceFileOperation.CreateDirectory => "Create folder",
            _ => operation + " selected item"
        };
        Source = operation is WorkspaceFileOperation.CreateFile or WorkspaceFileOperation.CreateDirectory
            ? workspace.CurrentDirectory : _source ?? "";
        var name = operation switch
        {
            WorkspaceFileOperation.CreateFile => "New file.txt",
            WorkspaceFileOperation.CreateDirectory => "New folder",
            WorkspaceFileOperation.Copy => Path.GetFileNameWithoutExtension(Source) + " - copy" + Path.GetExtension(Source),
            _ => Path.GetFileName(Source)
        };
        Destination = Path.GetRelativePath(Root, Path.Combine(workspace.CurrentDirectory, name));
    }

    public string Title { get; }
    public string Root { get; }
    public string Source { get; }
    [ObservableProperty] private string _destination;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _transferStatus = "";

    public async Task<bool> SubmitAsync()
    {
        if (IsBusy) return false;
        IsBusy = true;
        Error = "";
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        try
        {
            if (string.IsNullOrWhiteSpace(Destination)) { Error = "Enter a destination path."; return false; }
            var progress = new Progress<FileTransferProgress>(p =>
                TransferStatus = $"{Path.GetFileName(p.SourcePath)} · {p.BytesCopied / 1048576d:0.0} / {p.TotalBytes / 1048576d:0.0} MiB");
            var succeeded = await _workspace.ExecuteFileOperationAsync(new WorkspaceFileOperationRequest(_operation, Root, _source, Destination), cancellation.Token, progress);
            if (!succeeded) Error = _workspace.Status;
            return succeeded;
        }
        finally { _operationCancellation = null; IsBusy = false; }
    }

    public void CancelOperation() => _operationCancellation?.Cancel();
}
