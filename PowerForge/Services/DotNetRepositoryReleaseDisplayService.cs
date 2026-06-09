using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class DotNetRepositoryReleaseDisplayService
{
    public DotNetRepositoryReleaseDisplayModel CreateDisplay(DotNetRepositoryReleaseSummary summary, bool isPlan)
    {
        if (summary is null)
            throw new ArgumentNullException(nameof(summary));

        return new DotNetRepositoryReleaseDisplayModel
        {
            Title = isPlan ? "Plan" : "Summary",
            Projects = summary.Projects.Select(project => new DotNetRepositoryReleaseProjectDisplayRow
            {
                ProjectName = project.ProjectName,
                Packable = project.IsPackable ? "Yes" : "No",
                VersionDisplay = project.VersionDisplay,
                PackageCount = project.PackageCount.ToString(),
                StatusText = ResolveStatusText(project.Status),
                StatusColor = ResolveStatusColor(project.Status),
                ErrorPreview = project.ErrorPreview
            }).ToArray(),
            Totals = CreateTotals(summary.Totals)
        };
    }

    private static IReadOnlyList<DotNetRepositoryReleaseTotalsDisplayRow> CreateTotals(DotNetRepositoryReleaseSummaryTotals totals)
    {
        var rows = new List<DotNetRepositoryReleaseTotalsDisplayRow>
        {
            Row("Projects", totals.ProjectCount),
            Row("Packable", totals.PackableCount),
            Row("Failed", totals.FailedProjectCount),
            Row("Packages", totals.PackageCount)
        };

        if (totals.PublishedPackageCount > 0)
            rows.Add(Row("Published", totals.PublishedPackageCount));
        if (totals.SkippedDuplicatePackageCount > 0)
            rows.Add(Row("Skipped duplicates", totals.SkippedDuplicatePackageCount));
        if (totals.FailedPublishCount > 0)
            rows.Add(Row("Failed publishes", totals.FailedPublishCount));
        if (!string.IsNullOrWhiteSpace(totals.ResolvedVersion))
            rows.Add(new DotNetRepositoryReleaseTotalsDisplayRow { Label = "Resolved version", Value = totals.ResolvedVersion });

        return rows;
    }

    private static string ResolveStatusText(DotNetRepositoryReleaseProjectStatus status)
        => status switch
        {
            DotNetRepositoryReleaseProjectStatus.Ok => "Ok",
            DotNetRepositoryReleaseProjectStatus.Skipped => "Skipped",
            _ => "Fail"
        };

    private static ConsoleColor ResolveStatusColor(DotNetRepositoryReleaseProjectStatus status)
        => status switch
        {
            DotNetRepositoryReleaseProjectStatus.Ok => ConsoleColor.Green,
            DotNetRepositoryReleaseProjectStatus.Skipped => ConsoleColor.Gray,
            _ => ConsoleColor.Red
        };

    private static DotNetRepositoryReleaseTotalsDisplayRow Row(string label, int value)
        => new() { Label = label, Value = value.ToString() };
}
