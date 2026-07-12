using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerForge;

namespace PowerForge.ConsoleShared;

internal sealed class PipelinePublishSummary
{
    internal PipelinePublishSummary(IReadOnlyList<PipelinePublishSummaryRow> rows)
    {
        Rows = rows ?? Array.Empty<PipelinePublishSummaryRow>();
        ChannelCount = Rows
            .Select(static row => row.Channel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    internal IReadOnlyList<PipelinePublishSummaryRow> Rows { get; }

    internal int ChannelCount { get; }
}

internal sealed class PipelinePublishSummaryRow
{
    internal PipelinePublishSummaryRow(
        string channel,
        string target,
        string result,
        string method,
        string reference)
    {
        Channel = channel ?? string.Empty;
        Target = target ?? string.Empty;
        Result = result ?? string.Empty;
        Method = method ?? string.Empty;
        Reference = reference ?? string.Empty;
    }

    internal string Channel { get; }

    internal string Target { get; }

    internal string Result { get; }

    internal string Method { get; }

    internal string Reference { get; }
}

internal static class PipelinePublishSummaryBuilder
{
    internal static PipelinePublishSummary Build(ModulePipelineResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        return Build(result.Plan.ModuleName, result.PublishResults, result.ProjectBuildResults);
    }

    internal static PipelinePublishSummary Build(
        string moduleName,
        IReadOnlyList<ModulePublishResult>? modulePublishes,
        IReadOnlyList<ProjectBuildHostExecutionResult>? projectBuilds)
    {
        var rows = new List<PipelinePublishSummaryRow>();
        AddProjectBuildRows(rows, projectBuilds);
        AddModulePublishRows(rows, moduleName, modulePublishes);

        var ordered = rows
            .OrderBy(static row => GetChannelOrder(row.Channel))
            .ToArray();
        return new PipelinePublishSummary(ordered);
    }

    private static void AddProjectBuildRows(
        ICollection<PipelinePublishSummaryRow> rows,
        IReadOnlyList<ProjectBuildHostExecutionResult>? projectBuilds)
    {
        foreach (var build in projectBuilds ?? Array.Empty<ProjectBuildHostExecutionResult>())
        {
            var buildResult = build?.Result;
            var release = buildResult?.Release;
            if (release is null)
                continue;

            AddNuGetRows(rows, release, release.PublishedPackages, "Published");
            AddNuGetRows(rows, release, release.SkippedDuplicatePackages, "Already existed");
            AddNuGetRows(rows, release, release.FailedPackages, "Failed");
            AddProjectGitHubRows(rows, buildResult!.GitHub, release);
        }
    }

    private static void AddNuGetRows(
        ICollection<PipelinePublishSummaryRow> rows,
        DotNetRepositoryReleaseResult release,
        IEnumerable<string>? packages,
        string outcome)
    {
        foreach (var package in (packages ?? Array.Empty<string>())
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var project = FindProject(release, package);
            var packageId = FirstNonEmpty(project?.PackageId, project?.ProjectName, Path.GetFileNameWithoutExtension(package));
            var version = ResolveProjectVersion(release, project);
            var identity = string.IsNullOrWhiteSpace(version)
                ? packageId
                : $"{packageId} {version}";
            var reference = BuildNuGetReference(release.PublishSource, packageId, version);

            rows.Add(new PipelinePublishSummaryRow(
                channel: "NuGet",
                target: BuildNuGetTarget(release.PublishSource),
                result: $"{outcome} {identity}".Trim(),
                method: "NuGet API",
                reference: reference));
        }
    }

    private static void AddProjectGitHubRows(
        ICollection<PipelinePublishSummaryRow> rows,
        IReadOnlyList<ProjectBuildGitHubResult>? publishes,
        DotNetRepositoryReleaseResult release)
    {
        var assetCount = release.Projects
            .Select(static project => project.ReleaseZipPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        foreach (var group in (publishes ?? Array.Empty<ProjectBuildGitHubResult>())
                     .Where(static publish => publish is not null)
                     .GroupBy(
                         static publish => $"{publish.TagName}\n{publish.ReleaseUrl}\n{publish.Success}",
                         StringComparer.OrdinalIgnoreCase))
        {
            var publish = group.First();
            var tag = string.IsNullOrWhiteSpace(publish.TagName) ? "(tag unavailable)" : publish.TagName!;
            var result = publish.Success
                ? assetCount > 0 ? $"Published {tag} ({assetCount} assets)" : $"Published {tag}"
                : $"Failed {tag}";

            rows.Add(new PipelinePublishSummaryRow(
                channel: "GitHub",
                target: BuildGitHubTarget(publish.ReleaseUrl),
                result: result,
                method: "GitHub API",
                reference: publish.Success ? publish.ReleaseUrl ?? string.Empty : publish.ErrorMessage ?? string.Empty));
        }
    }

    private static void AddModulePublishRows(
        ICollection<PipelinePublishSummaryRow> rows,
        string moduleName,
        IReadOnlyList<ModulePublishResult>? publishes)
    {
        foreach (var publish in publishes ?? Array.Empty<ModulePublishResult>())
        {
            if (publish is null)
                continue;

            if (publish.Destination == PublishDestination.GitHub)
            {
                var tag = string.IsNullOrWhiteSpace(publish.TagName) ? "(tag unavailable)" : publish.TagName!;
                var assetCount = publish.AssetPaths?.Length ?? 0;
                var result = publish.Succeeded
                    ? assetCount > 0 ? $"Published {tag} ({assetCount} assets)" : $"Published {tag}"
                    : $"Failed {tag}";
                rows.Add(new PipelinePublishSummaryRow(
                    channel: "GitHub",
                    target: BuildGitHubTarget(publish.ReleaseUrl, publish.UserName, publish.RepositoryName),
                    result: result,
                    method: "GitHub API",
                    reference: publish.Succeeded ? publish.ReleaseUrl ?? string.Empty : publish.ErrorMessage ?? string.Empty));
                continue;
            }

            var version = string.IsNullOrWhiteSpace(publish.VersionText) ? "(version unavailable)" : publish.VersionText;
            var identity = $"{moduleName} {version}".Trim();
            rows.Add(new PipelinePublishSummaryRow(
                channel: "PowerShellGallery",
                target: string.IsNullOrWhiteSpace(publish.RepositoryName) ? "(repository unavailable)" : publish.RepositoryName!,
                result: publish.Succeeded ? $"Published {identity}" : $"Failed {identity}",
                method: publish.Tool?.ToString() ?? "Repository API",
                reference: publish.Succeeded
                    ? BuildPowerShellGalleryReference(moduleName, publish)
                    : publish.ErrorMessage ?? string.Empty));
        }
    }

    private static DotNetRepositoryProjectResult? FindProject(DotNetRepositoryReleaseResult release, string package)
        => release.Projects.FirstOrDefault(project =>
            project.Packages.Any(candidate => PathsEqual(candidate, package)));

    private static string ResolveProjectVersion(
        DotNetRepositoryReleaseResult release,
        DotNetRepositoryProjectResult? project)
    {
        if (project is null)
            return release.ResolvedVersion ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(project.NewVersion))
            return project.NewVersion!;
        if (release.ResolvedVersionsByProject.TryGetValue(project.ProjectName, out var resolved))
            return resolved;
        return project.OldVersion ?? release.ResolvedVersion ?? string.Empty;
    }

    private static string BuildNuGetTarget(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "(NuGet feed)";
        return IsNuGetOrg(source!) ? "nuget.org" : source!.Trim();
    }

    private static string BuildNuGetReference(string? source, string packageId, string version)
    {
        if (IsNuGetOrg(source) && !string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(version))
            return $"https://www.nuget.org/packages/{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(version)}";
        return source?.Trim() ?? string.Empty;
    }

    private static string BuildPowerShellGalleryReference(string moduleName, ModulePublishResult publish)
    {
        if (string.Equals(publish.RepositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(moduleName) &&
            !string.IsNullOrWhiteSpace(publish.VersionText))
        {
            return $"https://www.powershellgallery.com/packages/{Uri.EscapeDataString(moduleName)}/{Uri.EscapeDataString(publish.VersionText)}";
        }

        return publish.ReleaseUrl ?? string.Empty;
    }

    private static string BuildGitHubTarget(string? releaseUrl, string? owner = null, string? repository = null)
    {
        if (!string.IsNullOrWhiteSpace(owner) || !string.IsNullOrWhiteSpace(repository))
            return $"{FirstNonEmpty(owner, "(owner unavailable)")}/{FirstNonEmpty(repository, "(repository unavailable)")}";

        if (Uri.TryCreate(releaseUrl, UriKind.Absolute, out var uri) &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
                return $"{segments[0]}/{segments[1]}";
        }

        return "(GitHub release)";
    }

    private static bool IsNuGetOrg(string? source)
        => !string.IsNullOrWhiteSpace(source) &&
           source!.IndexOf("nuget.org", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left!), Path.GetFullPath(right!), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left!.Trim(), right!.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static int GetChannelOrder(string channel)
        => channel switch
        {
            "NuGet" => 0,
            "PowerShellGallery" => 1,
            "GitHub" => 2,
            _ => 3
        };
}
