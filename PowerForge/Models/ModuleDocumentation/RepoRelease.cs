using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class RepoRelease
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsPrerelease { get; set; }
    public bool IsDraft { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
        = null;
    public List<RepoReleaseAsset> Assets { get; } = new List<RepoReleaseAsset>();
}

internal sealed class RepoReleaseAsset
{
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long? Size { get; set; }
        = null;
    public string? ContentType { get; set; }
        = null;
}
