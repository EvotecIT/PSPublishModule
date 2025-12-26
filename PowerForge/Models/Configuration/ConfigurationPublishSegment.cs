namespace PowerForge;

/// <summary>
/// Configuration segment that describes publishing behavior (PSGallery/GitHub).
/// </summary>
public sealed class ConfigurationPublishSegment : IConfigurationSegment
{
    /// <summary>Publish configuration payload.</summary>
    public PublishConfiguration Configuration { get; set; } = new();

    /// <inheritdoc />
    public string Type => Configuration.Destination == PublishDestination.PowerShellGallery
        ? "GalleryNuget"
        : "GitHubNuget";
}

/// <summary>
/// Publish configuration payload for <see cref="ConfigurationPublishSegment"/>.
/// </summary>
public sealed class PublishConfiguration
{
    /// <summary>Publish destination type.</summary>
    public PublishDestination Destination { get; set; } = PublishDestination.PowerShellGallery;

    /// <summary>API key used for publishing.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional artefact ID used for publishing.</summary>
    public string? ID { get; set; }

    /// <summary>Enable publishing to the chosen destination.</summary>
    public bool Enabled { get; set; }

    /// <summary>GitHub username (required for GitHub publishing).</summary>
    public string? UserName { get; set; }

    /// <summary>Repository name override (GitHub org/repo or PowerShell repository name).</summary>
    public string? RepositoryName { get; set; }

    /// <summary>Allow publishing lower versions (legacy behavior).</summary>
    public bool Force { get; set; }

    /// <summary>Override tag name used for GitHub publishing.</summary>
    public string? OverwriteTagName { get; set; }

    /// <summary>Publish GitHub release as a release even if prerelease is set.</summary>
    public bool DoNotMarkAsPreRelease { get; set; }

    /// <summary>Verbose mode requested.</summary>
    public bool Verbose { get; set; }
}

