using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class GitHubViewModel
{
    private CancellationTokenSource? _fileRead;
    private int _fileVersion;
    public ObservableCollection<GitHubPullRequestFile> ChangedFiles { get; } = [];
    [ObservableProperty] private bool _isFilesReview;
    [ObservableProperty] private bool _isFilesLoading;
    [ObservableProperty] private string _filesStatus = "Files have not been loaded.";
    [ObservableProperty] private string _filesRevision = "";
    [ObservableProperty] private GitHubPullRequestFile? _selectedChangedFile;
    public bool CanReviewFiles => !_disposed && HasChecks && HeadSha.Length > 0 && !IsDetailLoading && !IsFilesLoading;
    public string PatchPreview => SelectedChangedFile?.Patch ?? "No text patch available.";
    public string PatchNotice => SelectedChangedFile?.PatchNotice ?? "Select a changed file.";
    public string FilePath => SelectedChangedFile?.Path ?? "Changed files";
    public string PreviousFilePath => SelectedChangedFile?.PreviousPath is { Length: > 0 } previous ? "Previously: " + previous : "";
    partial void OnSelectedChangedFileChanged(GitHubPullRequestFile? value)
    {
        OnPropertyChanged(nameof(PatchPreview)); OnPropertyChanged(nameof(PatchNotice));
        OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(PreviousFilePath));
    }
    partial void OnHeadShaChanged(string value) => OnPropertyChanged(nameof(CanReviewFiles));
    partial void OnIsDetailLoadingChanged(bool value) => OnPropertyChanged(nameof(CanReviewFiles));
    partial void OnIsFilesLoadingChanged(bool value) => OnPropertyChanged(nameof(CanReviewFiles));

    private void ClearFiles()
    {
        ++_fileVersion; _fileRead?.Cancel(); ChangedFiles.Clear(); SelectedChangedFile = null;
        IsFilesReview = false; IsFilesLoading = false; FilesRevision = ""; FilesStatus = "Files have not been loaded.";
    }

    [RelayCommand] private void ShowDiscussion() { ++_fileVersion; _fileRead?.Cancel(); IsFilesLoading = false; IsFilesReview = false; }

    [RelayCommand]
    public async Task ReviewFilesAsync()
    {
        if (!CanReviewFiles || Selected is not { } selected) return;
        _fileRead?.Cancel();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _fileRead = read;
        var version = ++_fileVersion; var context = _contextVersion; var detail = _detailVersion;
        var slug = Slug; var head = HeadSha;
        bool IsCurrent() => Current(context) && version == _fileVersion && detail == _detailVersion;
        ChangedFiles.Clear(); SelectedChangedFile = null; FilesRevision = "";
        IsFilesReview = true; IsFilesLoading = true; FilesStatus = "Loading changed files and checking PR revisions…";
        try
        {
            var result = await _service.FetchPullRequestFilesAsync(slug, selected.Number, head, read.Token);
            if (!IsCurrent()) return;
            FilesRevision = $"Head {result.HeadSha} · Base {result.BaseSha}";
            foreach (var file in result.Files) ChangedFiles.Add(file);
            FilesStatus = $"{result.Files.Count} files loaded." + (result.Files.HasMore ? " Partial listing; more files may exist on GitHub." : "") + " Snapshot checked before and after loading.";
            SelectedChangedFile = ChangedFiles.FirstOrDefault();
        }
        catch (GitHubRevisionChangedException ex) { if (IsCurrent()) FilesStatus = ex.Message; }
        catch (OperationCanceledException) { if (IsCurrent()) FilesStatus = "File request cancelled or timed out. Retry to load files."; }
        catch (Exception ex) { if (IsCurrent()) FilesStatus = SafeError(ex); }
        finally { if (ReferenceEquals(_fileRead, read)) _fileRead = null; if (IsCurrent()) IsFilesLoading = false; }
    }
}
