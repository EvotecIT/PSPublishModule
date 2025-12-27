namespace PowerForge;

/// <summary>
/// Request used to publish a module folder or nupkg to a PowerShell repository using PSResourceGet or PowerShellGet.
/// </summary>
public sealed class RepositoryPublishRequest
{
    /// <summary>Path to a module folder (<c>-Path</c>) or a .nupkg file (<c>-NupkgPath</c> for PSResourceGet).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>When true, indicates <see cref="Path"/> points to a nupkg.</summary>
    public bool IsNupkg { get; set; }

    /// <summary>
    /// Repository name to publish to. When not set, the tool default is used (typically PSGallery).
    /// </summary>
    public string? RepositoryName { get; set; }

    /// <summary>Publishing tool/provider used for repository publishing.</summary>
    public PublishTool Tool { get; set; } = PublishTool.Auto;

    /// <summary>
    /// API key used for publishing (PSGallery / NuGet API key).
    /// When publishing via PowerShellGet, this maps to <c>-NuGetApiKey</c>.
    /// When publishing via PSResourceGet, this maps to <c>-ApiKey</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional repository definition and registration settings (private feeds, custom URLs, credentials).
    /// </summary>
    public PublishRepositoryConfiguration? Repository { get; set; }

    /// <summary>DestinationPath passed to PSResourceGet <c>Publish-PSResource</c> (optional).</summary>
    public string? DestinationPath { get; set; }

    /// <summary>Skip dependency check (PSResourceGet only).</summary>
    public bool SkipDependenciesCheck { get; set; }

    /// <summary>Skip module manifest validation (PSResourceGet only).</summary>
    public bool SkipModuleManifestValidate { get; set; }
}
