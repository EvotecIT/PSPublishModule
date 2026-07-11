using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Builds reusable summary models for repository-wide .NET release results.
/// </summary>
public sealed class DotNetRepositoryReleaseSummaryService
{
    /// <summary>
    /// Creates a summary view for a repository release result.
    /// </summary>
    public DotNetRepositoryReleaseSummary CreateSummary(DotNetRepositoryReleaseResult result, int maxErrorLength = 140)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var rows = result.Projects
            .OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Select(project => new DotNetRepositoryReleaseProjectSummaryRow
            {
                ProjectName = project.ProjectName,
                IsPackable = project.IsPackable,
                VersionDisplay = BuildVersionDisplay(project),
                PackageCount = project.Packages.Count,
                Status = ResolveStatus(project),
                ErrorMessage = project.ErrorMessage?.Trim() ?? string.Empty,
                ErrorPreview = TrimForSummary(project.ErrorMessage, maxErrorLength)
            })
            .ToArray();

        return new DotNetRepositoryReleaseSummary
        {
            Projects = rows,
            Totals = new DotNetRepositoryReleaseSummaryTotals
            {
                ProjectCount = result.Projects.Count,
                PackableCount = result.Projects.Count(p => p.IsPackable),
                FailedProjectCount = result.Projects.Count(p => !string.IsNullOrWhiteSpace(p.ErrorMessage)),
                PackageCount = result.Projects.Sum(p => p.Packages.Count),
                PublishedPackageCount = result.PublishedPackages.Count,
                SkippedDuplicatePackageCount = result.SkippedDuplicatePackages.Count,
                FailedPublishCount = result.FailedPackages.Count,
                ResolvedVersion = result.ResolvedVersion ?? string.Empty
            }
        };
    }

    /// <summary>
    /// Creates a complete, multiline failure report without truncating project or package details.
    /// </summary>
    /// <param name="result">Repository release result to report.</param>
    /// <returns>A human-readable failure report, or an empty string when the result succeeded.</returns>
    public string CreateFailureReport(DotNetRepositoryReleaseResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (result.Success)
            return string.Empty;

        var failedProjects = result.Projects
            .Where(static project => !string.IsNullOrWhiteSpace(project.ErrorMessage))
            .OrderBy(static project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var failedPackages = result.FailedPackages
            .Where(static package => !string.IsNullOrWhiteSpace(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static package => package, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var headline = failedProjects.Length > 0
            ? $"Project build failed: {failedProjects.Length} of {result.Projects.Count} project(s) failed."
            : "Project build failed.";
        var lines = new List<string> { headline };

        var aggregateError = result.ErrorMessage?.Trim();
        if (!string.IsNullOrWhiteSpace(aggregateError) &&
            !IsGeneratedProjectAggregate(aggregateError!, failedProjects))
        {
            lines.Add($"Cause: {aggregateError}");
        }

        foreach (var project in failedProjects)
            lines.Add($"Detail: {FormatProjectFailure(project)}");

        foreach (var package in failedPackages)
            lines.Add($"Detail: Failed package publish: {package}");

        if (lines.Count == 1)
            lines.Add("Cause: The release pipeline reported failure without a detailed cause.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildVersionDisplay(DotNetRepositoryProjectResult project)
    {
        if (string.IsNullOrWhiteSpace(project.OldVersion) && string.IsNullOrWhiteSpace(project.NewVersion))
            return string.Empty;

        return $"{project.OldVersion ?? "?"} -> {project.NewVersion ?? "?"}";
    }

    private static DotNetRepositoryReleaseProjectStatus ResolveStatus(DotNetRepositoryProjectResult project)
    {
        if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
            return DotNetRepositoryReleaseProjectStatus.Failed;

        return project.IsPackable
            ? DotNetRepositoryReleaseProjectStatus.Ok
            : DotNetRepositoryReleaseProjectStatus.Skipped;
    }

    private static string TrimForSummary(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;
        if (maxLength < 4 || trimmed.Length <= maxLength)
            return trimmed;

        return trimmed.Substring(0, maxLength - 3) + "...";
    }

    private static bool IsGeneratedProjectAggregate(
        string aggregateError,
        IReadOnlyCollection<DotNetRepositoryProjectResult> failedProjects)
    {
        if (failedProjects.Count == 0 ||
            !aggregateError.StartsWith("One or more projects failed:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return failedProjects.All(project =>
            aggregateError.IndexOf(project.ProjectName, StringComparison.OrdinalIgnoreCase) >= 0 &&
            aggregateError.IndexOf(project.ErrorMessage!.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string FormatProjectFailure(DotNetRepositoryProjectResult project)
    {
        var error = project.ErrorMessage!.Trim();
        var projectPrefix = project.ProjectName + ":";
        return error.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)
            ? error
            : $"{project.ProjectName}: {error}";
    }
}
