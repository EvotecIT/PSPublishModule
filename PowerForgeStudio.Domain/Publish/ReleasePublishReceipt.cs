namespace PowerForgeStudio.Domain.Publish;

public sealed record ReleasePublishReceipt(
    string RootPath,
    string RepositoryName,
    string AdapterKind,
    string TargetName,
    string TargetKind,
    string? Destination,
    string? SourcePath,
    ReleasePublishReceiptStatus Status,
    string Summary,
    DateTimeOffset PublishedAtUtc)
{
    public string StatusDisplay => Status.ToString();
    /// <summary>Identity captured from the published package, when this receipt represents a package.</summary>
    public string? PackageId { get; init; }
    /// <summary>Exact package version captured before publishing.</summary>
    public string? PackageVersion { get; init; }
    public string PackageIdentityDisplay => string.IsNullOrWhiteSpace(PackageId) || string.IsNullOrWhiteSpace(PackageVersion)
        ? string.Empty : $"{PackageId} {PackageVersion}";
    public bool HasPackageIdentity => PackageIdentityDisplay.Length > 0;
    /// <summary>Public registry that can be compared with the ecosystem snapshot without exposing private feeds.</summary>
    public string? PublicRegistry
    {
        get
        {
            if (Status != ReleasePublishReceiptStatus.Published || !HasPackageIdentity || string.IsNullOrWhiteSpace(Destination)) return null;
            if (TargetKind == "PowerShellRepository" &&
                Destination.Equals("PSGallery", StringComparison.OrdinalIgnoreCase))
                return "PowerShell Gallery";
            if (TargetKind is not ("NuGet" or "ModulePackages") ||
                !Uri.TryCreate(Destination, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return null;
            return uri.Host is "api.nuget.org" or "www.nuget.org" ? "NuGet.org" : null;
        }
    }
    public bool CanInspectPublicPackage => PublicRegistry is not null;
}
