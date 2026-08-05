using System.Diagnostics;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private bool ExecutePackageSigning(
        DotNetRepositoryReleaseSpec spec,
        DotNetRepositoryReleaseResult result,
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        string signingCertificateSha256,
        IProjectBuildProgressReporter? progress,
        IProjectBuildProgressReporterV2? detailedProgress)
    {
        var packages = projects
            .Where(project => string.IsNullOrWhiteSpace(project.ErrorMessage))
            .SelectMany(project => project.Packages.Concat(project.SymbolPackages))
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packages.Length == 0)
            return false;

        var items = CreateArtifactProgressItems(
            packages,
            ProjectBuildProgressPhase.PackageSigning,
            detailedProgress);
        progress?.PhaseStarted(ProjectBuildProgressPhase.PackageSigning, packages.Length, "Signing NuGet packages");
        foreach (var package in packages)
        {
            UpdateArtifactProgress(
                detailedProgress,
                items[package],
                ProjectBuildProgressItemState.Started,
                "signing");
        }

        _logger.Info($"Signing {packages.Length} NuGet package(s)...");
        var watch = Stopwatch.StartNew();
        if (!_signPackages(packages, spec, signingCertificateSha256, out var error))
        {
            watch.Stop();
            result.ErrorMessage = error;
            result.Success = false;
            MarkPackageSigningFailure(projects, packages, error);
            _logger.Warn(error);
            foreach (var package in packages)
            {
                UpdateArtifactProgress(
                    detailedProgress,
                    items[package],
                    ProjectBuildProgressItemState.Failed,
                    error,
                    watch.Elapsed);
            }

            progress?.PhaseFailed(ProjectBuildProgressPhase.PackageSigning, error);
            return spec.PublishFailFast;
        }

        watch.Stop();
        _logger.Success($"Signed {packages.Length} NuGet package(s) in {FormatDuration(watch.Elapsed)}.");
        foreach (var package in packages)
        {
            UpdateArtifactProgress(
                detailedProgress,
                items[package],
                ProjectBuildProgressItemState.Completed,
                "signed",
                watch.Elapsed);
        }

        progress?.PhaseCompleted(ProjectBuildProgressPhase.PackageSigning, $"{packages.Length} package(s) signed");
        return false;
    }

    private bool ExecuteNuGetPublishing(
        DotNetRepositoryReleaseSpec spec,
        DotNetRepositoryReleaseResult result,
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        string root,
        IProjectBuildProgressReporter? progress,
        IProjectBuildProgressReporterV2? detailedProgress)
    {
        var preflight = ValidatePublishPreflight(projects, spec);
        if (!preflight.Success)
        {
            result.Success = false;
            result.ErrorMessage = preflight.ErrorMessage;
            return true;
        }

        if (string.IsNullOrWhiteSpace(spec.PublishApiKey))
        {
            result.Success = false;
            result.ErrorMessage = "PublishApiKey is required when Publish is enabled.";
            return true;
        }

        var source = string.IsNullOrWhiteSpace(spec.PublishSource)
            ? "https://api.nuget.org/v3/index.json"
            : spec.PublishSource!.Trim();
        result.PublishSource = source;

        var orderedProjects = SortProjectsForPublish(projects);
        var publishSymbolsSeparately = spec.IncludeSymbols && IsLocalPublishSource(source);
        var packages = GetPackagesForPublish(orderedProjects, publishSymbolsSeparately).ToArray();
        var packageLookup = orderedProjects
            .SelectMany(project => (publishSymbolsSeparately
                    ? project.Packages.Concat(project.SymbolPackages)
                    : project.Packages)
                .Select(package => new { Package = package, Project = project }))
            .GroupBy(entry => entry.Package, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Project, StringComparer.OrdinalIgnoreCase);

        var watch = Stopwatch.StartNew();
        var items = CreateArtifactProgressItems(
            packages,
            ProjectBuildProgressPhase.NuGetPublish,
            detailedProgress);
        progress?.PhaseStarted(ProjectBuildProgressPhase.NuGetPublish, packages.Length, "Publishing package artifacts");
        var completed = 0;
        foreach (var package in packages)
        {
            var item = items[package];
            UpdateArtifactProgress(
                detailedProgress,
                item,
                ProjectBuildProgressItemState.Started,
                spec.WhatIf ? "planning publish" : "publishing");
            progress?.PhaseUpdated(ProjectBuildProgressPhase.NuGetPublish, completed, packages.Length, Path.GetFileName(package));
            packageLookup.TryGetValue(package, out var project);
            var artifacts = GetPublishedArtifacts(
                project,
                package,
                includeCompanionSymbols: !publishSymbolsSeparately);
            if (spec.WhatIf)
            {
                result.PublishedPackages.AddRange(artifacts);
                UpdateArtifactProgress(detailedProgress, item, ProjectBuildProgressItemState.Completed, "would publish");
                completed++;
                continue;
            }

            if (publishSymbolsSeparately &&
                !CanPublishSymbolPackage(
                    package,
                    (IEnumerable<string>?)project?.Packages ?? Array.Empty<string>(),
                    primaryPackage =>
                        result.PublishedPackages.Contains(primaryPackage, StringComparer.OrdinalIgnoreCase) ||
                        result.SkippedDuplicatePackages.Contains(primaryPackage, StringComparer.OrdinalIgnoreCase),
                    out var primaryPackage))
            {
                var blocked = CreateBlockedCompanionResult(package, primaryPackage);
                result.Success = false;
                result.FailedPackages.Add(package);
                _logger.Warn(blocked.Message!);
                if (project is not null && string.IsNullOrWhiteSpace(project.ErrorMessage))
                    project.ErrorMessage = blocked.Message;
                UpdateArtifactProgress(detailedProgress, item, ProjectBuildProgressItemState.Failed, blocked.Message);
                completed++;
                if (spec.PublishFailFast)
                {
                    result.ErrorMessage = blocked.Message;
                    return true;
                }

                continue;
            }

            _logger.Info($"Publishing {Path.GetFileName(package)}...");
            var packageWatch = Stopwatch.StartNew();
            spec.RemotePublishAttempted?.Invoke();
            var pushResult = PushPackage(
                package,
                spec.PublishApiKey!,
                source,
                spec.SkipDuplicate,
                suppressCompanionSymbols: !spec.IncludeSymbols || publishSymbolsSeparately,
                workingDirectory: root);
            packageWatch.Stop();
            var outcomes = ClassifyPublishedArtifacts(artifacts, pushResult, spec.SkipDuplicate);
            foreach (var artifact in artifacts)
            {
                switch (outcomes[artifact])
                {
                    case PackagePushOutcome.SkippedDuplicate:
                        result.SkippedDuplicatePackages.Add(artifact);
                        _logger.Info($"Skipped duplicate {Path.GetFileName(artifact)} in {FormatDuration(packageWatch.Elapsed)}; package already exists in the feed.");
                        break;
                    case PackagePushOutcome.Published:
                        result.PublishedPackages.Add(artifact);
                        _logger.Success($"Published {Path.GetFileName(artifact)} in {FormatDuration(packageWatch.Elapsed)}.");
                        break;
                    default:
                        result.FailedPackages.Add(artifact);
                        _logger.Warn($"NuGet push failed for {artifact} after {FormatDuration(packageWatch.Elapsed)}: {pushResult.Message}");
                        break;
                }
            }

            var failures = artifacts
                .Where(artifact => outcomes[artifact] == PackagePushOutcome.Failed)
                .ToArray();
            if (failures.Length > 0)
            {
                UpdateArtifactProgress(
                    detailedProgress,
                    item,
                    ProjectBuildProgressItemState.Failed,
                    pushResult.Message,
                    packageWatch.Elapsed);
                result.Success = false;
                if (project is not null && string.IsNullOrWhiteSpace(project.ErrorMessage))
                    project.ErrorMessage = $"Publish failed for {string.Join(", ", failures.Select(Path.GetFileName))}: {pushResult.Message}";
                if (spec.PublishFailFast)
                {
                    if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                        result.ErrorMessage = $"Publish failed for {string.Join(", ", failures.Select(Path.GetFileName))}.";
                    return true;
                }
            }
            else
            {
                var skipped = artifacts.All(artifact => outcomes[artifact] == PackagePushOutcome.SkippedDuplicate);
                UpdateArtifactProgress(
                    detailedProgress,
                    item,
                    ProjectBuildProgressItemState.Completed,
                    skipped ? "already existed" : "published",
                    packageWatch.Elapsed);
            }

            completed++;
        }

        watch.Stop();
        var summary = spec.WhatIf
            ? $"NuGet publish plan prepared in {FormatDuration(watch.Elapsed)} ({result.PublishedPackages.Count} package artifact(s) would be published)."
            : $"NuGet publish phase completed in {FormatDuration(watch.Elapsed)} ({result.PublishedPackages.Count} published, {result.SkippedDuplicatePackages.Count} skipped duplicate, {result.FailedPackages.Count} failed).";
        if (result.FailedPackages.Count == 0)
        {
            _logger.Success(summary);
            progress?.PhaseCompleted(ProjectBuildProgressPhase.NuGetPublish, summary);
        }
        else
        {
            _logger.Warn(summary);
            progress?.PhaseFailed(ProjectBuildProgressPhase.NuGetPublish, summary);
        }

        return false;
    }
}
