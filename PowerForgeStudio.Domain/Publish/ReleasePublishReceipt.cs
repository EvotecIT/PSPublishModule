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
    public string StatusDisplay => TargetKind == "Winget" && Status == ReleasePublishReceiptStatus.Published
        ? "Submitted · catalog unverified"
        : Status.ToString();
    /// <summary>Identity captured from the published package, when this receipt represents a package.</summary>
    public string? PackageId { get; init; }
    /// <summary>Exact package version captured before publishing.</summary>
    public string? PackageVersion { get; init; }
    /// <summary>Credentials or query values were omitted from the saved destination; authenticated probes need a separate credential source.</summary>
    public bool DestinationCredentialsOmitted { get; init; }
    /// <summary>Published GitHub asset names and byte sizes captured from the checkpointed files. Null means no inventory was recorded.</summary>
    public Dictionary<string, long>? GitHubAssets { get; init; }
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

    public string? WingetPullRequestUrl
    {
        get
        {
            if (TargetKind != "Winget" || !Uri.TryCreate(Destination, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" || !uri.IsDefaultPort ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment)) return null;

            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts[0] != "microsoft" || parts[1] != "winget-pkgs" ||
                parts[2] != "pull" || !long.TryParse(parts[3], out var number) || number <= 0) return null;
            return $"https://github.com/microsoft/winget-pkgs/pull/{number}";
        }
    }
    public bool CanOpenWingetPullRequest => WingetPullRequestUrl is not null;
}
