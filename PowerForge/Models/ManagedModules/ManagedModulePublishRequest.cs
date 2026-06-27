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
    /// Optional package staging directory used before remote upload.
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Repository credential or API key.
    /// </summary>
    public RepositoryCredential? Credential { get; set; }

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
