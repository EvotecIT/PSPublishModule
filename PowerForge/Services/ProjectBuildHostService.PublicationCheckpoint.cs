namespace PowerForge;

public sealed partial class ProjectBuildHostService
{
    /// <summary>Publishes exact staged artifacts through the existing NuGet/GitHub owners without another build.</summary>
    private ProjectBuildHostExecutionResult PublishCheckpoint(ProjectBuildHostRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var checkpoint = request.PublicationCheckpoint!;
        if (!checkpoint.Success || checkpoint.Result.Release is not { Success: true } original)
            throw new InvalidOperationException("Deferred package publication requires a successful build checkpoint.");
        var configuration = checkpoint.DeferredPublicationConfiguration
            ?? throw new InvalidOperationException("The package build did not retain its deferred publication configuration.");
        if (!configuration.PublishNuget && !configuration.PublishGitHub)
            return checkpoint;
        var release = ModulePackageReleaseCheckpointService.CreatePublicationRelease(
            original, request.PublicationAssets, requireStagedAssets: true, includeReleaseZips: true);
        using var snapshot = ModulePackagePublicationSnapshot.Create(release, includeReleaseZips: true);
        var publisher = new ProjectBuildPublishHostService(_logger, _publishGitHub);
        void BeforePublish()
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            snapshot.ValidateUnchanged();
            request.RemotePublishAttempted?.Invoke();
        }
        if (configuration.PublishNuget)
        {
            BeforePublish();
            NuGetPackagePublishResult published;
            try
            {
                published = publisher.PublishNuGet(configuration, release, checkpoint.RootPath,
                    BeforePublish, request.Progress, request.CancellationToken);
            }
            finally
            {
                // Keep completed side effects visible even when a later guard or cancellation interrupts publication.
                original.PublishSource = release.PublishSource;
                original.PublishedPackages.AddRange(snapshot.ResolveOriginalPaths(release.PublishedPackages));
                original.SkippedDuplicatePackages.AddRange(snapshot.ResolveOriginalPaths(release.SkippedDuplicatePackages));
                original.FailedPackages.AddRange(snapshot.ResolveOriginalPaths(release.FailedPackages));
                for (var index = 0; index < original.Projects.Count; index++)
                    original.Projects[index].ErrorMessage = release.Projects[index].ErrorMessage;
            }
            if (!published.Success)
            {
                original.Success = false;
                original.ErrorMessage = published.ErrorMessage;
                throw new InvalidOperationException(published.ErrorMessage ?? "Deferred package publication failed.");
            }
        }
        if (configuration.PublishGitHub)
        {
            BeforePublish();
            var published = publisher.PublishGitHub(configuration, release, request.Progress);
            checkpoint.Result.GitHub.AddRange(published.Results);
            if (!published.Success)
                throw new InvalidOperationException(published.ErrorMessage ?? "Deferred project GitHub publication failed.");
        }
        request.CancellationToken.ThrowIfCancellationRequested();
        snapshot.ValidateUnchanged();
        return checkpoint;
    }
}
