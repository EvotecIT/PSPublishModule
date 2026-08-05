using System.IO;
using System.Linq;
using System.Diagnostics;

namespace PowerForge;

internal sealed class NuGetPackagePublishService
{
    private readonly ILogger _logger;
    private readonly Func<DotNetNuGetPushRequest, DotNetRepositoryReleaseService.PackagePushResult> _pushPackage;
    private readonly string? _workingDirectory;

    public NuGetPackagePublishService(
        ILogger logger,
        Func<DotNetNuGetPushRequest, DotNetRepositoryReleaseService.PackagePushResult>? pushPackage = null,
        string? workingDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pushPackage = pushPackage ?? PushPackage;
        _workingDirectory = workingDirectory;
    }

    public NuGetPackagePublishResult Execute(NuGetPackagePublishRequest request, Func<string, bool>? shouldPublishPackage = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var result = new NuGetPackagePublishResult();
        var roots = (request.Roots ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length == 0)
        {
            result.Success = false;
            result.ErrorMessage = "No paths were provided.";
            return result;
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                result.Success = false;
                result.ErrorMessage = $"Path '{root}' not found.";
                return result;
            }
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.nupkg", SearchOption.AllDirectories))
            .Where(path => unique.Add(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (packages.Length == 0)
        {
            result.Success = false;
            result.ErrorMessage = $"No packages found in {string.Join(", ", roots)}";
            return result;
        }

        foreach (var package in packages)
        {
            if (shouldPublishPackage is not null && !shouldPublishPackage(package))
            {
                result.PublishedItems.Add(package);
                result.PackagePushResults[package] = new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published
                };
                continue;
            }

            var pushResult = _pushPackage(new DotNetNuGetPushRequest(
                    package,
                    request.ApiKey,
                    request.Source,
                    request.SkipDuplicate,
                    request.WorkingDirectory ?? _workingDirectory,
                    timeout: null,
                    suppressCompanionSymbols: true))
                ?? new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
                    Message = "Push handler returned no result."
                };
            result.PackagePushResults[package] = pushResult;

            switch (pushResult.Outcome)
            {
                case DotNetRepositoryReleaseService.PackagePushOutcome.Published:
                    result.PublishedItems.Add(package);
                    break;
                case DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate:
                    result.PublishedItems.Add(package);
                    result.SkippedDuplicateItems.Add(package);
                    break;
                default:
                    result.Success = false;
                    result.FailedItems.Add(package);
                    _logger.Verbose($"dotnet nuget push failed for {package}.");
                    if (pushResult.Message is string message && message.Length > 0)
                        _logger.Verbose(message);
                    break;
            }
        }

        return result;
    }

    public NuGetPackagePublishResult ExecutePackages(
        IReadOnlyList<string> packages,
        string apiKey,
        string source,
        bool skipDuplicate,
        bool publishFailFast = true,
        bool suppressCompanionSymbols = false,
        Action? remotePublishAttempted = null,
        IProjectBuildProgressReporter? progress = null)
    {
        if (packages is null)
            throw new ArgumentNullException(nameof(packages));

        var result = new NuGetPackagePublishResult();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packagePaths = packages
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => unique.Add(path))
            .ToArray();

        if (packagePaths.Length == 0)
        {
            result.Success = false;
            result.ErrorMessage = "No package paths were provided.";
            return result;
        }

        var detailedProgress = progress as IProjectBuildProgressReporterV2;
        var progressItems = packagePaths
            .Select((path, index) => new
            {
                Path = path,
                Item = new ProjectBuildProgressItem
                {
                    Phase = ProjectBuildProgressPhase.NuGetPublish,
                    Key = $"publish:{index + 1}:{Path.GetFileName(path)}",
                    Title = Path.GetFileName(path),
                    Kind = ProjectBuildProgressPhase.NuGetPublish.ToString(),
                    Position = index + 1,
                    Total = packagePaths.Length
                }
            })
            .ToDictionary(entry => entry.Path, entry => entry.Item, StringComparer.OrdinalIgnoreCase);
        detailedProgress?.ItemsPlanned(
            ProjectBuildProgressPhase.NuGetPublish,
            progressItems.Values.OrderBy(item => item.Position).ToArray());
        progress?.PhaseStarted(
            ProjectBuildProgressPhase.NuGetPublish,
            packagePaths.Length,
            "Publishing existing package artifacts");
        var completed = 0;

        foreach (var package in packagePaths)
        {
            var item = progressItems[package];
            var watch = Stopwatch.StartNew();
            detailedProgress?.ItemUpdated(item, ProjectBuildProgressItemState.Started, "publishing");
            progress?.PhaseUpdated(
                ProjectBuildProgressPhase.NuGetPublish,
                completed,
                packagePaths.Length,
                Path.GetFileName(package));
            if (!File.Exists(package))
            {
                watch.Stop();
                result.Success = false;
                result.FailedItems.Add(package);
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                    result.ErrorMessage = $"Package '{package}' not found.";
                item.Duration = watch.Elapsed;
                detailedProgress?.ItemUpdated(
                    item,
                    ProjectBuildProgressItemState.Failed,
                    result.ErrorMessage);
                completed++;
                if (publishFailFast)
                {
                    progress?.PhaseFailed(ProjectBuildProgressPhase.NuGetPublish, result.ErrorMessage);
                    return result;
                }
                continue;
            }

            if (!DotNetRepositoryReleaseService.CanPublishSymbolPackage(
                    package,
                    packagePaths,
                    primaryPackage =>
                        result.PackagePushResults.TryGetValue(primaryPackage, out var primaryResult) &&
                        (primaryResult.Outcome == DotNetRepositoryReleaseService.PackagePushOutcome.Published ||
                         primaryResult.Outcome == DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate),
                    out var primaryPackage))
            {
                watch.Stop();
                var blockedResult = DotNetRepositoryReleaseService.CreateBlockedCompanionResult(
                    package,
                    primaryPackage);
                result.Success = false;
                result.FailedItems.Add(package);
                result.PackagePushResults[package] = blockedResult;
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                    result.ErrorMessage = blockedResult.Message;
                item.Duration = watch.Elapsed;
                detailedProgress?.ItemUpdated(
                    item,
                    ProjectBuildProgressItemState.Failed,
                    blockedResult.Message);
                completed++;
                if (publishFailFast)
                {
                    progress?.PhaseFailed(ProjectBuildProgressPhase.NuGetPublish, blockedResult.Message);
                    return result;
                }
                continue;
            }

            remotePublishAttempted?.Invoke();
            var pushResult = _pushPackage(new DotNetNuGetPushRequest(
                    package,
                    apiKey,
                    source,
                    skipDuplicate,
                    _workingDirectory,
                    timeout: null,
                    suppressCompanionSymbols))
                ?? new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
                    Message = "Push handler returned no result."
                };
            result.PackagePushResults[package] = pushResult;
            watch.Stop();
            item.Duration = watch.Elapsed;

            switch (pushResult.Outcome)
            {
                case DotNetRepositoryReleaseService.PackagePushOutcome.Published:
                    result.PublishedItems.Add(package);
                    detailedProgress?.ItemUpdated(item, ProjectBuildProgressItemState.Completed, "published");
                    break;
                case DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate:
                    result.PublishedItems.Add(package);
                    result.SkippedDuplicateItems.Add(package);
                    detailedProgress?.ItemUpdated(item, ProjectBuildProgressItemState.Completed, "already existed");
                    break;
                default:
                    result.Success = false;
                    result.FailedItems.Add(package);
                    detailedProgress?.ItemUpdated(
                        item,
                        ProjectBuildProgressItemState.Failed,
                        pushResult.Message);
                    _logger.Verbose($"dotnet nuget push failed for {package}.");
                    if (pushResult.Message is string message && message.Length > 0)
                    {
                        _logger.Verbose(message);
                        if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                            result.ErrorMessage = message;
                    }
                    if (publishFailFast)
                    {
                        progress?.PhaseFailed(
                            ProjectBuildProgressPhase.NuGetPublish,
                            result.ErrorMessage ?? $"Failed to publish {Path.GetFileName(package)}.");
                        return result;
                    }
                    break;
            }
            completed++;
        }

        if (result.Success)
        {
            progress?.PhaseCompleted(
                ProjectBuildProgressPhase.NuGetPublish,
                $"{completed} package(s) processed");
        }
        else
        {
            progress?.PhaseFailed(
                ProjectBuildProgressPhase.NuGetPublish,
                result.ErrorMessage ?? $"{result.FailedItems.Count} package(s) failed");
        }

        return result;
    }

    private static DotNetRepositoryReleaseService.PackagePushResult PushPackage(DotNetNuGetPushRequest request)
        => DotNetRepositoryReleaseService.PushPackage(request);
}
