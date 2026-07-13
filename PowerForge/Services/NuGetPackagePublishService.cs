using System.IO;
using System.Linq;

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
        bool suppressCompanionSymbols = false)
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

        foreach (var package in packagePaths)
        {
            if (!File.Exists(package))
            {
                result.Success = false;
                result.FailedItems.Add(package);
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                    result.ErrorMessage = $"Package '{package}' not found.";
                if (publishFailFast)
                    return result;
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
                var blockedResult = DotNetRepositoryReleaseService.CreateBlockedCompanionResult(
                    package,
                    primaryPackage);
                result.Success = false;
                result.FailedItems.Add(package);
                result.PackagePushResults[package] = blockedResult;
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                    result.ErrorMessage = blockedResult.Message;
                if (publishFailFast)
                    return result;
                continue;
            }

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
                    {
                        _logger.Verbose(message);
                        if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                            result.ErrorMessage = message;
                    }
                    if (publishFailFast)
                        return result;
                    break;
            }
        }

        return result;
    }

    private static DotNetRepositoryReleaseService.PackagePushResult PushPackage(DotNetNuGetPushRequest request)
        => DotNetRepositoryReleaseService.PushPackage(request);
}
