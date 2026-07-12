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
        Channels = Rows
            .GroupBy(static row => row.Channel, StringComparer.OrdinalIgnoreCase)
            .Select(BuildChannelSummary)
            .ToArray();
    }

    internal IReadOnlyList<PipelinePublishSummaryRow> Rows { get; }

    internal IReadOnlyList<PipelinePublishChannelSummary> Channels { get; }

    internal int ChannelCount => Channels.Count;

    private static PipelinePublishChannelSummary BuildChannelSummary(
        IGrouping<string, PipelinePublishSummaryRow> channel)
    {
        var rows = channel.ToArray();
        var itemName = channel.Key switch
        {
            "NuGet" => rows.Length == 1 ? "package" : "packages",
            "PowerShell Gallery" => rows.Length == 1 ? "module" : "modules",
            "GitHub" => rows.Length == 1 ? "release" : "releases",
            _ => rows.Length == 1 ? "result" : "results"
        };
        var parts = new List<string> { $"{rows.Length} {itemName}" };

        AddOutcome(parts, rows, PipelinePublishOutcome.Published, "published");
        AddOutcome(parts, rows, PipelinePublishOutcome.AlreadyExisted, "already existed");
        AddOutcome(parts, rows, PipelinePublishOutcome.Failed, "failed");

        var targets = rows
            .Select(static row => row.Target)
            .Where(static target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 1)
            parts.Add(targets[0]);
        else if (targets.Length > 1)
            parts.Add($"{targets.Length} targets");

        var methods = rows
            .Select(static row => row.Method)
            .Where(static method => !string.IsNullOrWhiteSpace(method))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (methods.Length == 1)
            parts.Add($"via {methods[0]}");
        else if (methods.Length > 1)
            parts.Add($"via {methods.Length} methods");

        return new PipelinePublishChannelSummary(channel.Key, string.Join(", ", parts));
    }

    private static void AddOutcome(
        ICollection<string> parts,
        IEnumerable<PipelinePublishSummaryRow> rows,
        PipelinePublishOutcome outcome,
        string label)
    {
        var count = rows.Count(row => row.Outcome == outcome);
        if (count > 0)
            parts.Add($"{count} {label}");
    }
}

internal sealed class PipelinePublishChannelSummary
{
    internal PipelinePublishChannelSummary(string channel, string label)
    {
        Channel = channel ?? string.Empty;
        Label = label ?? string.Empty;
    }

    internal string Channel { get; }

    internal string Label { get; }
}

internal sealed class PipelinePublishSummaryRow
{
    internal PipelinePublishSummaryRow(
        string channel,
        string target,
        string result,
        string method,
        string reference,
        PipelinePublishOutcome outcome)
    {
        Channel = channel ?? string.Empty;
        Target = target ?? string.Empty;
        Result = result ?? string.Empty;
        Method = method ?? string.Empty;
        Reference = reference ?? string.Empty;
        Outcome = outcome;
    }

    internal string Channel { get; }

    internal string Target { get; }

    internal string Result { get; }

    internal string Method { get; }

    internal string Reference { get; }

    internal PipelinePublishOutcome Outcome { get; }
}

internal enum PipelinePublishOutcome
{
    Published,
    AlreadyExisted,
    Failed
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

            AddNuGetRows(rows, release, release.PublishedPackages, PipelinePublishOutcome.Published);
            AddNuGetRows(rows, release, release.SkippedDuplicatePackages, PipelinePublishOutcome.AlreadyExisted);
            AddNuGetRows(rows, release, release.FailedPackages, PipelinePublishOutcome.Failed);
            AddProjectGitHubRows(rows, buildResult!.GitHub, release);
        }
    }

    private static void AddNuGetRows(
        ICollection<PipelinePublishSummaryRow> rows,
        DotNetRepositoryReleaseResult release,
        IEnumerable<string>? packages,
        PipelinePublishOutcome outcome)
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
                result: $"{FormatOutcome(outcome)} {identity}".Trim(),
                method: "dotnet nuget push",
                reference: reference,
                outcome: outcome));
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
                reference: publish.Success ? publish.ReleaseUrl ?? string.Empty : publish.ErrorMessage ?? string.Empty,
                outcome: publish.Success ? PipelinePublishOutcome.Published : PipelinePublishOutcome.Failed));
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
                    reference: publish.Succeeded ? publish.ReleaseUrl ?? string.Empty : publish.ErrorMessage ?? string.Empty,
                    outcome: publish.Succeeded ? PipelinePublishOutcome.Published : PipelinePublishOutcome.Failed));
                continue;
            }

            var version = string.IsNullOrWhiteSpace(publish.VersionText) ? "(version unavailable)" : publish.VersionText;
            var identity = $"{moduleName} {version}".Trim();
            rows.Add(new PipelinePublishSummaryRow(
                channel: "PowerShell Gallery",
                target: string.IsNullOrWhiteSpace(publish.RepositoryName) ? "(repository unavailable)" : publish.RepositoryName!,
                result: publish.Succeeded ? $"Published {identity}" : $"Failed {identity}",
                method: publish.Tool?.ToString() ?? "Repository API",
                reference: publish.Succeeded
                    ? BuildPowerShellGalleryReference(moduleName, publish)
                    : publish.ErrorMessage ?? string.Empty,
                outcome: publish.Succeeded ? PipelinePublishOutcome.Published : PipelinePublishOutcome.Failed));
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
        var safeSource = SanitizeRepositorySource(source);
        if (string.IsNullOrWhiteSpace(safeSource))
            return "(NuGet feed)";
        return IsNuGetOrg(safeSource) ? "nuget.org" : safeSource;
    }

    private static string BuildNuGetReference(string? source, string packageId, string version)
    {
        if (IsNuGetOrg(source) && !string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(version))
            return $"https://www.nuget.org/packages/{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(version)}";
        return SanitizeRepositorySource(source);
    }

    private static string SanitizeRepositorySource(string? source)
    {
        var value = source?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.UserInfo))
            return value;

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri.AbsoluteUri;
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

    private static string FormatOutcome(PipelinePublishOutcome outcome)
        => outcome switch
        {
            PipelinePublishOutcome.Published => "Published",
            PipelinePublishOutcome.AlreadyExisted => "Already existed",
            PipelinePublishOutcome.Failed => "Failed",
            _ => outcome.ToString()
        };

    private static int GetChannelOrder(string channel)
        => channel switch
        {
            "NuGet" => 0,
            "PowerShell Gallery" => 1,
            "GitHub" => 2,
            _ => 3
        };
}
