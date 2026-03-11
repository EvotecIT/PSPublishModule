namespace PowerForge;

internal sealed class PublishConfigurationRequest
{
    public string ParameterSetName { get; set; } = string.Empty;
    public PublishDestination Type { get; set; }
    public string AzureDevOpsOrganization { get; set; } = string.Empty;
    public string? AzureDevOpsProject { get; set; }
    public string AzureArtifactsFeed { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? RepositoryName { get; set; }
    public PublishTool Tool { get; set; } = PublishTool.Auto;
    public string? RepositoryUri { get; set; }
    public string? RepositorySourceUri { get; set; }
    public string? RepositoryPublishUri { get; set; }
    public bool RepositoryTrusted { get; set; } = true;
    public int? RepositoryPriority { get; set; }
    public RepositoryApiVersion RepositoryApiVersion { get; set; } = RepositoryApiVersion.Auto;
    public bool EnsureRepositoryRegistered { get; set; } = true;
    public bool UnregisterRepositoryAfterPublish { get; set; }
    public string? RepositoryCredentialUserName { get; set; }
    public string? RepositoryCredentialSecret { get; set; }
    public bool RepositoryCredentialSecretSpecified { get; set; }
    public string? RepositoryCredentialSecretFilePath { get; set; }
    public bool RepositoryCredentialSecretFilePathSpecified { get; set; }
    public bool Enabled { get; set; }
    public string? OverwriteTagName { get; set; }
    public bool Force { get; set; }
    public string? ID { get; set; }
    public bool DoNotMarkAsPreRelease { get; set; }
    public bool GenerateReleaseNotes { get; set; }
    public bool Verbose { get; set; }
}
