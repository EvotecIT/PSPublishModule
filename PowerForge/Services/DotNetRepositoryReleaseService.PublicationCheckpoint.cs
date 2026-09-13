namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    /// <summary>Publishes captured package files with the repository owner's full preflight and dependency policy, without building.</summary>
    internal NuGetPackagePublishResult PublishExistingPackages(DotNetRepositoryReleaseSpec spec,
        DotNetRepositoryReleaseResult release, IProjectBuildProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        var previousCancellationToken = ActiveCancellationToken.Value;
        ActiveCancellationToken.Value = cancellationToken;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!release.Success)
                throw new InvalidOperationException(release.ErrorMessage ?? "Package publication requires a successful release checkpoint.");
            var projects = release.Projects.Where(project => project.IsPackable).ToArray();
            if (projects.Length == 0)
                throw new InvalidOperationException("The package release checkpoint contains no packable projects to publish.");
            spec.Publish = true;
            spec.WhatIf = false;
            ExecuteNuGetPublishing(spec, release, projects, spec.RootPath, progress,
                progress as IProjectBuildProgressReporterV2, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var result = new NuGetPackagePublishResult { Success = release.Success, ErrorMessage = release.ErrorMessage };
            result.PublishedItems.AddRange(release.PublishedPackages);
            result.SkippedDuplicateItems.AddRange(release.SkippedDuplicatePackages);
            result.FailedItems.AddRange(release.FailedPackages);
            return result;
        }
        finally { ActiveCancellationToken.Value = previousCancellationToken; }
    }
}
