namespace PowerForge;

/// <summary>
/// Request for packaging and publishing a managed module package.
/// </summary>
public sealed class ManagedModulePublishRequest
{
    /// <summary>
    /// Module folder to package.
    /// </summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional existing .nupkg path to publish without repacking a module folder.
    /// </summary>
    public string? PackagePath { get; set; }

    /// <summary>
    /// Optional explicit module manifest path.
    /// </summary>
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Optional package id override.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional package version override.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Repository that receives the package.
    /// </summary>
    public ManagedModuleRepository Repository { get; set; } = null!;

    /// <summary>
    /// Optional repository endpoint used only for package upload when it differs from the read repository.
    /// </summary>
    public ManagedModuleRepository? PublishRepository { get; set; }

    /// <summary>
    /// Optional package staging directory used before remote upload.
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Repository credential used for repository reads and dependency checks.
    /// </summary>
    public RepositoryCredential? Credential { get; set; }

    /// <summary>
    /// Optional credential or API key used only for package upload.
    /// </summary>
    public RepositoryCredential? PublishCredential { get; set; }

    /// <summary>
    /// Optional package authors override.
    /// </summary>
    public string? Authors { get; set; }

    /// <summary>
    /// Optional package description override.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional package project URL override.
    /// </summary>
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// Optional package tags override.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>
    /// Skip checking manifest RequiredModules against the target repository.
    /// </summary>
    public bool SkipDependenciesCheck { get; set; }

    /// <summary>
    /// Skip managed manifest metadata validation before packaging.
    /// </summary>
    public bool SkipModuleManifestValidate { get; set; }

    /// <summary>
    /// Overwrite an existing local package file.
    /// </summary>
    public bool Force { get; set; }
}
