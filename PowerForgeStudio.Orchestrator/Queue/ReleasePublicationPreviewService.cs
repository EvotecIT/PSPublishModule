using PowerForge;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed record ReleasePublicationPreview(IReadOnlyList<ReleasePublishTarget> Targets, string Summary);

public interface IReleasePublicationPreviewService
{
    Task<ReleasePublicationPreview> PreviewAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default);
}

/// <summary>Inspects captured signing checkpoints and current destination settings without publishing or resolving credentials.</summary>
public sealed class ReleasePublicationPreviewService : IReleasePublicationPreviewService
{
    public Task<ReleasePublicationPreview> PreviewAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
        => Task.Run(() => Preview(session, cancellationToken), cancellationToken);

    private static ReleasePublicationPreview Preview(ReleaseQueueSession session, CancellationToken token)
    {
        var item = session.Items.Single();
        if (item.Stage != ReleaseQueueStage.Publish || item.Status != ReleaseQueueItemStatus.ReadyToRun)
            throw new InvalidOperationException("Complete signing before inspecting publication targets.");
        var signing = new ReleaseQueueCheckpointSerializer().TryDeserialize<ReleaseSigningExecutionResult>(item.CheckpointStateJson);
        if (signing?.Succeeded != true || signing.RequiresRebuild)
            throw new InvalidOperationException("A successful signing checkpoint is required.");
        token.ThrowIfCancellationRequested();
        var targets = new ReleasePublishExecutionService().BuildPendingTargets([item]).ToList();
        var repository = new RepositoryCatalogScanner().InspectRepository(item.RootPath);
        if (!string.IsNullOrEmpty(repository.ProjectBuildScriptPath))
        {
            var path = RepositoryPlanPreviewService.ResolveProjectConfigPath(repository.ProjectBuildScriptPath, item.RootPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new InvalidOperationException("Project publication configuration is missing.");
            var config = new ProjectBuildPublishHostService().PreviewConfiguration(path);
            targets = targets.Where(target => target.AdapterKind != "ProjectBuild" ||
                (target.TargetKind != "NuGet" || config.PublishNuGet) && (target.TargetKind != "GitHub" || config.PublishGitHub)).Select(target =>
                target.AdapterKind != "ProjectBuild" ? target : target with { Destination = target.TargetKind switch {
                    "NuGet" => config.NuGetDestination + (config.DestinationRedacted ? " (credentials/query omitted)" : ""),
                    "GitHub" => string.IsNullOrEmpty(config.GitHubRepository) ? "GitHub repository is not configured" : config.GitHubRepository,
                    _ => target.Destination } }).ToList();
        }
        token.ThrowIfCancellationRequested();
        return new(targets.Select(target => target with {
            Destination = StudioOutputSanitizer.Sanitize(target.Destination), TargetName = StudioOutputSanitizer.Sanitize(target.TargetName)
        }).ToArray(), "Destination inspection only. Credentials and publish readiness have not been checked; generic destinations still require resolution. Nothing has been published.");
    }
}
