using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;
using System.Collections.ObjectModel;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class ReleaseViewModel
{
    private readonly IReleasePublicationPreviewService _publication = publication ?? new ReleasePublicationPreviewService();
    public ObservableCollection<ReleasePublishTarget> PublicationTargets { get; } = [];
    [ObservableProperty] private string _publicationSummary = "";
    [ObservableProperty] private bool _isInspectingPublication;
    public bool CanInspectPublication => !_disposed && !HasProtectedReleaseWork && !IsLoadingHistory && !IsInspectingPublication
        && Handoff?.Session.Items.SingleOrDefault()?.Stage == ReleaseQueueStage.Publish;
    partial void OnIsInspectingPublicationChanged(bool value) => OnPropertyChanged(nameof(CanInspectPublication));

    [RelayCommand]
    public async Task InspectPublicationAsync()
    {
        if (!CanInspectPublication || Handoff is not { } handoff) return;
        var version = _version; IsInspectingPublication = true;
        PublicationTargets.Clear(); PublicationSummary = "Inspecting destinations…";
        try
        {
            var preview = await _publication.PreviewAsync(handoff.Session);
            if (_disposed || version != _version || !ReferenceEquals(Handoff, handoff)) return;
            foreach (var target in preview.Targets) PublicationTargets.Add(target);
            PublicationSummary = preview.Summary;
        }
        catch (Exception ex) { if (!_disposed && version == _version) PublicationSummary = StudioOutputSanitizer.Sanitize(ex.Message); }
        finally { IsInspectingPublication = false; }
    }
}
