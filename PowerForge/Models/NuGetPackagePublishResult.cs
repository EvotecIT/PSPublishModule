using System.Collections.Generic;

namespace PowerForge;

internal sealed class NuGetPackagePublishResult
{
    public bool Success { get; set; } = true;
    public List<string> PublishedItems { get; } = new();
    public List<string> SkippedDuplicateItems { get; } = new();
    public List<string> FailedItems { get; } = new();
    public Dictionary<string, DotNetRepositoryReleaseService.PackagePushResult> PackagePushResults { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ErrorMessage { get; set; }
}
