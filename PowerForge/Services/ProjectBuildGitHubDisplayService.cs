using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ProjectBuildGitHubDisplayService
{
    public ProjectBuildGitHubDisplayModel CreateSummary(ProjectBuildGitHubPublishSummary summary)
    {
        if (summary is null)
            throw new ArgumentNullException(nameof(summary));

        var rows = new List<ProjectBuildGitHubDisplayRow>();
        if (!summary.PerProject)
        {
            rows.Add(Row("Mode", "Single"));
            rows.Add(Row("Tag", summary.SummaryTag ?? string.Empty));
            rows.Add(Row("Assets", summary.SummaryAssetsCount.ToString()));
            if (!string.IsNullOrWhiteSpace(summary.SummaryReleaseUrl))
                rows.Add(Row("Release", summary.SummaryReleaseUrl!));
        }
        else
        {
            var ok = summary.Results.Count(result => result.Success);
            var fail = summary.Results.Count(result => !result.Success);
            rows.Add(Row("Mode", "PerProject"));
            rows.Add(Row("Projects", summary.Results.Count.ToString()));
            rows.Add(Row("Succeeded", ok.ToString()));
            rows.Add(Row("Failed", fail.ToString()));
        }

        return new ProjectBuildGitHubDisplayModel
        {
            Title = "GitHub Summary",
            Rows = rows
        };
    }

    private static ProjectBuildGitHubDisplayRow Row(string label, string value)
        => new() { Label = label, Value = value };
}
