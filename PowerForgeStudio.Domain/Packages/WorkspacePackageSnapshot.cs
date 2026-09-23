namespace PowerForgeStudio.Domain.Packages;

/// <summary>Public package evidence with the provider's generation time and completeness signals.</summary>
public sealed record WorkspacePackageSnapshot(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ReadAtUtc,
    IReadOnlyList<WorkspacePackageMetric> Packages,
    long? NuGetDownloads,
    long? PowerShellGalleryDownloads,
    int WarningCount,
    string SourceUrl,
    bool NuGetAvailable = true,
    bool PowerShellGalleryAvailable = true,
    IReadOnlyList<string>? SourceWarnings = null,
    bool NuGetRetained = false,
    bool PowerShellGalleryRetained = false)
{
    public IReadOnlyList<string> Warnings => SourceWarnings ?? [];
    public int NuGetCount => Packages.Count(static item => item.Registry == "NuGet.org");
    public int PowerShellGalleryCount => Packages.Count(static item => item.Registry == "PowerShell Gallery");
    public int TotalCount => Packages.Count;
    public long? TotalDownloads => NuGetDownloads is { } nuget && PowerShellGalleryDownloads is { } gallery
        ? nuget + gallery : null;
    public bool IsStale => ReadAtUtc - GeneratedAtUtc > TimeSpan.FromDays(2);
    public bool IsPartial => WarningCount > 0;
    public bool HasRetainedMetrics => NuGetRetained || PowerShellGalleryRetained;
    public bool IsRegistryRetained(string registry) => registry switch
    {
        "NuGet.org" => NuGetRetained,
        "PowerShell Gallery" => PowerShellGalleryRetained,
        _ => false
    };
}
