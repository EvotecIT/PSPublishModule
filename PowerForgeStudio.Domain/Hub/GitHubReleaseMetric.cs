namespace PowerForgeStudio.Domain.Hub;

/// <summary>Observed download count for one GitHub release asset.</summary>
public sealed record GitHubReleaseAssetMetric(string Name, long? Downloads, long? Size)
{
    public string DownloadsDisplay => Downloads?.ToString("N0") ?? "Not reported";
    public string SizeDisplay => Size is { } bytes ? $"{bytes:N0} bytes" : "Size not reported";
}

/// <summary>Read-only GitHub release evidence. Counts cover assets, not source archives or other distribution channels.</summary>
public sealed record GitHubReleaseMetric(
    long Id,
    string TagName,
    string Name,
    string? HtmlUrl,
    DateTimeOffset? PublishedAtUtc,
    bool IsDraft,
    bool IsPrerelease,
    IReadOnlyList<GitHubReleaseAssetMetric> Assets,
    bool AssetInventoryComplete)
{
    public string StateDisplay => IsDraft ? "Draft" : IsPrerelease ? "Prerelease" : "Published";
    public string PublishedDisplay => PublishedAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "Not published";
    public long? AssetDownloads => AssetInventoryComplete && Assets.All(static asset => asset.Downloads.HasValue)
        ? Assets.Sum(static asset => asset.Downloads!.Value) : null;
    public string AssetDownloadsDisplay => AssetDownloads?.ToString("N0") ?? "Not reported";
    public string AssetSummary => AssetInventoryComplete
        ? $"{Assets.Count} asset(s) · {AssetDownloadsDisplay} asset download(s)"
        : Assets.Count == 0 ? "Asset inventory unavailable"
        : $"At least {Assets.Count} asset(s) · download total incomplete";
}
