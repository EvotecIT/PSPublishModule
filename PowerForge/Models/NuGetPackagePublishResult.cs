using System.Collections.Generic;

namespace PowerForge;

internal sealed class NuGetPackagePublishResult
{
    public bool Success { get; set; } = true;
    public List<string> PublishedItems { get; } = new();
    public List<string> FailedItems { get; } = new();
    public string? ErrorMessage { get; set; }
}
