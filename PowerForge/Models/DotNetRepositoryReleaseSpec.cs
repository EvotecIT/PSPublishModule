namespace PowerForge;

/// <summary>
/// Specification for repository-wide .NET package release (versioning, packing, publishing).
/// </summary>
public sealed class DotNetRepositoryReleaseSpec
{
    /// <summary>Root path of the repository to scan for projects.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Optional project names to include (csproj file name without extension).</summary>
    public string[]? IncludeProjects { get; set; }

    /// <summary>Project names to exclude from packing/publishing.</summary>
    public string[]? ExcludeProjects { get; set; }

    /// <summary>Directory names to exclude from project discovery.</summary>
    public string[]? ExcludeDirectories { get; set; }

    /// <summary>
    /// Expected version or X-pattern (e.g., 1.2.X). When null/empty, no version stepping occurs.
    /// </summary>
    public string? ExpectedVersion { get; set; }

    /// <summary>
    /// Optional per-project expected versions (project name -> version or X-pattern).
    /// When provided, overrides <see cref="ExpectedVersion"/> for matching projects.
    /// </summary>
    public Dictionary<string, string>? ExpectedVersionsByProject { get; set; }

    /// <summary>When false, skips updating project files and uses existing csproj versions.</summary>
    public bool UpdateVersions { get; set; } = true;

    /// <summary>
    /// When true, only projects matching <see cref="ExpectedVersionsByProject"/> are included.
    /// </summary>
    public bool ExpectedVersionMapAsInclude { get; set; }

    /// <summary>
    /// When true, keys in <see cref="ExpectedVersionsByProject"/> may contain wildcards (*, ?).
    /// </summary>
    public bool ExpectedVersionMapUseWildcards { get; set; }

    /// <summary>Sources used to resolve the current package version (v3 index or local path).</summary>
    public string[]? VersionSources { get; set; }

    /// <summary>Credential used for private version sources.</summary>
    public RepositoryCredential? VersionSourceCredential { get; set; }

    /// <summary>Whether to include prerelease versions during version resolution.</summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>Build configuration for dotnet pack (Release/Debug).</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Optional output path for generated packages.</summary>
    public string? OutputPath { get; set; }

    /// <summary>When true, runs dotnet pack.</summary>
    public bool Pack { get; set; } = true;

    /// <summary>When true, create a release zip from the build output.</summary>
    public bool CreateReleaseZip { get; set; }

    /// <summary>Optional output path for release zips.</summary>
    public string? ReleaseZipOutputPath { get; set; }

    /// <summary>Certificate thumbprint used for signing packages (optional).</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store location used to resolve the signing certificate.</summary>
    public CertificateStoreLocation CertificateStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Timestamp server URL used during package signing.</summary>
    public string? TimeStampServer { get; set; }

    /// <summary>When true, publishes packages with dotnet nuget push.</summary>
    public bool Publish { get; set; }

    /// <summary>Publish source URL (defaults to nuget.org v3 index).</summary>
    public string? PublishSource { get; set; }

    /// <summary>API key used for publishing packages.</summary>
    public string? PublishApiKey { get; set; }

    /// <summary>When publishing, skip duplicates on push.</summary>
    public bool SkipDuplicate { get; set; }

    /// <summary>When true, stop on the first publish/signing failure.</summary>
    public bool PublishFailFast { get; set; }

    /// <summary>When true, computes plan only (no file modifications or dotnet operations).</summary>
    public bool WhatIf { get; set; }
}
