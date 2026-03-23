namespace PowerForge;

internal sealed class StoreSubmissionSpec
{
    public string? Schema { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public StoreSubmissionAuthenticationOptions Authentication { get; set; } = new();

    public StoreSubmissionTarget[] Targets { get; set; } = Array.Empty<StoreSubmissionTarget>();
}

internal sealed class StoreSubmissionAuthenticationOptions
{
    public string? SellerId { get; set; }

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? ClientSecretEnvVar { get; set; }

    public string? AccessToken { get; set; }

    public string? AccessTokenEnvVar { get; set; }

    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";

    public string Resource { get; set; } = "https://manage.devcenter.microsoft.com";

    public string Scope { get; set; } = "https://api.store.microsoft.com/.default";
}

internal sealed class StoreSubmissionTarget
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public StoreSubmissionProviderKind Provider { get; set; } = StoreSubmissionProviderKind.PackagedApp;

    public string ApplicationId { get; set; } = string.Empty;

    public string? SubmissionId { get; set; }

    public string? SourceDirectory { get; set; }

    public bool RecurseSourceDirectory { get; set; }

    public string[] PackagePatterns { get; set; } = Array.Empty<string>();

    public string[] PackagePaths { get; set; } = Array.Empty<string>();

    public string? ZipPath { get; set; }

    public bool Commit { get; set; } = true;

    public bool WaitForCommit { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 30;

    public int PollTimeoutMinutes { get; set; } = 30;

    public string MinimumDirectXVersion { get; set; } = "None";

    public string MinimumSystemRam { get; set; } = "None";

    public StoreSubmissionDesktopPackage[] DesktopPackages { get; set; } = Array.Empty<StoreSubmissionDesktopPackage>();
}

internal sealed class StoreSubmissionRequest
{
    public string? TargetName { get; set; }

    public string? SubmissionId { get; set; }

    public bool? Commit { get; set; }

    public bool? WaitForCommit { get; set; }

    public string[] PackagePaths { get; set; } = Array.Empty<string>();
}

internal sealed class StoreSubmissionTargetSummary
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public StoreSubmissionProviderKind Provider { get; set; }

    public string ApplicationId { get; set; } = string.Empty;
}

internal sealed class StoreSubmissionPlan
{
    public string ConfigPath { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public StoreSubmissionProviderKind Provider { get; set; }

    public string ApplicationId { get; set; } = string.Empty;

    public string? SubmissionId { get; set; }

    public string[] PackageFiles { get; set; } = Array.Empty<string>();

    public string ZipPath { get; set; } = string.Empty;

    public bool Commit { get; set; }

    public bool WaitForCommit { get; set; }

    public int PollIntervalSeconds { get; set; }

    public int PollTimeoutMinutes { get; set; }

    public string MinimumDirectXVersion { get; set; } = "None";

    public string MinimumSystemRam { get; set; } = "None";

    public StoreSubmissionDesktopPackage[] DesktopPackages { get; set; } = Array.Empty<StoreSubmissionDesktopPackage>();
}

internal sealed class StoreSubmissionResult
{
    public bool Succeeded { get; set; }

    public string? ErrorMessage { get; set; }

    public StoreSubmissionPlan? Plan { get; set; }

    public string? SubmissionId { get; set; }

    public bool CreatedSubmission { get; set; }

    public string[] PackageFiles { get; set; } = Array.Empty<string>();

    public string? PackageZipPath { get; set; }

    public bool UploadedPackageArchive { get; set; }

    public bool CommittedSubmission { get; set; }

    public string? FinalStatus { get; set; }

    public string? StatusDetails { get; set; }

    public StoreSubmissionStatusSnapshot[] StatusHistory { get; set; } = Array.Empty<StoreSubmissionStatusSnapshot>();
}

internal sealed class StoreSubmissionStatusSnapshot
{
    public DateTimeOffset CheckedUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Details { get; set; }
}

internal enum StoreSubmissionProviderKind
{
    PackagedApp,
    DesktopInstaller
}

internal sealed class StoreSubmissionDesktopPackage
{
    public string PackageUrl { get; set; } = string.Empty;

    public string[] Languages { get; set; } = Array.Empty<string>();

    public string[] Architectures { get; set; } = Array.Empty<string>();

    public bool IsSilentInstall { get; set; }

    public string? InstallerParameters { get; set; }

    public string? GenericDocUrl { get; set; }

    public string PackageType { get; set; } = "exe";
}
