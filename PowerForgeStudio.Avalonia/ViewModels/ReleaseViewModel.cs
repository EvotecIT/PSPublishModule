using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>Prepares a release from the captured build rather than the currently selected repository.</summary>
public sealed partial class ReleaseViewModel(IReleaseBuildHandoffService? service = null, IReleaseSigningWorkflow? signing = null,
    PowerForgeStudio.Orchestrator.Storage.IReleaseHistoryService? history = null, IReleasePublicationPreviewService? publication = null,
    IReleasePublicationWorkflow? publishing = null, IReleaseVerificationWorkflow? verification = null) : ObservableObject, IDisposable
{
    private readonly IReleaseBuildHandoffService _service = service ?? new ReleaseBuildHandoffService();
    private readonly IReleaseSigningWorkflow _signing = signing ?? new DurableReleaseSigningWorkflow(PowerForgeStudioHostPaths.GetReleaseHistoryDatabasePath());
    private ReleaseBuildExecutionResult? _candidate;
    private CancellationTokenSource? _preparing;
    private int _version;
    private bool _disposed;
    public ObservableCollection<ReleaseSigningArtifact> Artifacts { get; } = [];
    public ReleaseBuildHandoff? Handoff { get; private set; }
    [ObservableProperty] private bool _isPreparing;
    [ObservableProperty] private string _buildRoot = "";
    [ObservableProperty] private string _status = "Complete a build, then prepare its release artifacts here.";
    [ObservableProperty] private string _stage = "Build required";
    public bool CanPrepare => !_disposed && !IsPreparing && !HasProtectedReleaseWork && !IsLoadingHistory && !RequiresRebuild && SigningResult?.Succeeded != true && _candidate?.Succeeded == true;
    public bool EmphasizePrepare => CanPrepare && !HasHandoff;
    public bool HasHandoff => Handoff is not null;
    partial void OnIsPreparingChanged(bool value) => NotifyReleaseState();

    public void SetBuild(ReleaseBuildExecutionResult? build, bool running, bool cancelled)
    {
        if (_disposed) return;
        if (HasProtectedReleaseWork) throw new InvalidOperationException("A build cannot replace an active release operation or unsaved receipt evidence.");
        ResetPublicationState();
        ResetExecutionProgress();
        SigningResult = null; Receipts.Clear(); RequiresRebuild = false; ConfirmDiscardReceipts = false;
        ++_version; _preparing?.Cancel(); _candidate = !running && !cancelled ? build : null;
        Handoff = null; Artifacts.Clear(); IsPreparing = false;
        BuildRoot = build?.RootPath ?? "";
        Stage = "Build required";
        Status = running ? "Build is running. Release preparation will be available after successful completion."
            : cancelled ? "The build was cancelled. Run a successful build before preparing a release."
            : build?.Succeeded == true ? "Prepare the artifacts captured by this build. No signing or publication occurs during preparation."
            : "Complete a successful build to prepare a release.";
        NotifyReleaseState();
    }

    [RelayCommand]
    public async Task PrepareAsync()
    {
        if (!CanPrepare || _candidate is not { } candidate) return;
        SigningResult = null; Receipts.Clear();
        var version = ++_version;
        using var read = new CancellationTokenSource(); _preparing = read;
        Handoff = null; Artifacts.Clear(); OnPropertyChanged(nameof(HasHandoff));
        IsPreparing = true; Stage = "Checking artifacts"; Status = "Checking captured artifacts and preparing the release checkpoint…";
        try
        {
            var handoff = await _service.PrepareAsync(candidate, read.Token);
            if (_disposed || version != _version) return;
            Handoff = handoff;
            foreach (var artifact in handoff.Artifacts) Artifacts.Add(artifact);
            Stage = "Prepared for signing";
            Status = $"{Artifacts.Count} artifacts found. Signing, publication and verification have not run.";
            NotifyReleaseState();
        }
        catch (OperationCanceledException) { if (!_disposed && version == _version) { Stage = "Cancelled"; Status = "Release preparation cancelled."; } }
        catch (Exception ex) { if (!_disposed && version == _version) { Stage = "Preparation failed"; Status = StudioOutputSanitizer.Sanitize(ex.Message); } }
        finally { if (ReferenceEquals(_preparing, read)) _preparing = null; if (!_disposed && version == _version) IsPreparing = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_version; _preparing?.Cancel(); _signingCancellation?.Cancel();
        _publicationCancellation?.Cancel(); _verificationCancellation?.Cancel();
        if (publishing is null && _publishing is IDisposable disposablePublishing) disposablePublishing.Dispose();
        if (verification is null && _verification is IDisposable disposableVerification) disposableVerification.Dispose();
    }
}
