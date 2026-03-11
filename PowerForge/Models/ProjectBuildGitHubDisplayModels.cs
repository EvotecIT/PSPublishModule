using System.Collections.Generic;

namespace PowerForge;

internal sealed class ProjectBuildGitHubDisplayModel
{
    internal string Title { get; set; } = string.Empty;
    internal IReadOnlyList<ProjectBuildGitHubDisplayRow> Rows { get; set; } = System.Array.Empty<ProjectBuildGitHubDisplayRow>();
}

internal sealed class ProjectBuildGitHubDisplayRow
{
    internal string Label { get; set; } = string.Empty;
    internal string Value { get; set; } = string.Empty;
}
