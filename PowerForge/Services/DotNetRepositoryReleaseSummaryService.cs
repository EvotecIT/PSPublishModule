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
}
