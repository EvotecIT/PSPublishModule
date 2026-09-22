namespace PowerForgeStudio.Domain.Packages;

/// <summary>One public package observation from the Evotec ecosystem snapshot.</summary>
public sealed record WorkspacePackageMetric(
    string Id,
    string Registry,
    string? LatestVersion,
    long? Downloads,
    string? DetailsUrl)
{
    public string VersionDisplay => string.IsNullOrWhiteSpace(LatestVersion) ? "Unknown" : LatestVersion;
    public string DownloadsDisplay => Downloads is { } count ? count.ToString("N0") : "Not reported";
}
