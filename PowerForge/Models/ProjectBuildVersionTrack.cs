using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Declares a shared version train where one anchor package determines the stepped version
/// for a group of related projects.
/// </summary>
internal sealed class ProjectBuildVersionTrack
{
    public string? ExpectedVersion { get; set; }
    public string? AnchorProject { get; set; }
    public string? AnchorPackageId { get; set; }
    public string[]? Projects { get; set; }
    public string[]? NugetSource { get; set; }
    public bool? IncludePrerelease { get; set; }
}
