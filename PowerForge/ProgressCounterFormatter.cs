using System.Globalization;

namespace PowerForge;

/// <summary>Formats stable, explicitly scoped progress counters.</summary>
internal static class ProgressCounterFormatter
{
    internal static string GetProjectBuildScope(ProjectBuildProgressPhase phase)
        => phase switch
        {
            ProjectBuildProgressPhase.Versioning => "Project",
            ProjectBuildProgressPhase.PackageBuild => "Project",
            ProjectBuildProgressPhase.PackageSigning => "Package",
            ProjectBuildProgressPhase.NuGetPublish => "Package",
            ProjectBuildProgressPhase.GitHubPublish => "Release",
            _ => "Step"
        };

    internal static string Format(int position, int total)
    {
        var safeTotal = Math.Max(1, total);
        var digits = Math.Max(2, safeTotal.ToString(CultureInfo.InvariantCulture).Length);
        var format = new string('0', digits);
        return $"{Math.Max(0, position).ToString(format, CultureInfo.InvariantCulture)}/" +
               safeTotal.ToString(format, CultureInfo.InvariantCulture);
    }

    internal static string Format(string? scope, int position, int total)
    {
        var counter = Format(position, total);
        if (string.IsNullOrWhiteSpace(scope)) {
            return counter;
        }

        var normalizedScope = scope!.Trim();
        return $"{normalizedScope} {counter}";
    }
}
