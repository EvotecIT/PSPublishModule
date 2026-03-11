using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides a way to configure publishing to PowerShell Gallery, GitHub, or private galleries such as Azure Artifacts.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet emits publish configuration consumed by <c>Invoke-ModuleBuild</c> / <c>Build-Module</c>.
/// Use <c>-Type</c> to choose a destination. For repository publishing, <c>-Tool</c> selects the provider
/// (PowerShellGet/PSResourceGet/Auto).
/// </para>
/// <para>
/// For private repositories (for example Azure DevOps Artifacts / private NuGet v3 feeds), provide repository URIs
/// and (optionally) credentials, or use the Azure Artifacts preset parameters to resolve those URIs automatically.
/// To avoid secrets in source control, pass API keys/tokens via <c>-FilePath</c> or environment-specific tooling.
/// </para>
/// </remarks>
/// <example>
/// <summary>Publish to PowerShell Gallery (API key from file)</summary>
/// <code>New-ConfigurationPublish -Type PowerShellGallery -FilePath "$env:USERPROFILE\.secrets\psgallery.key" -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to GitHub Releases (token from file)</summary>
/// <code>New-ConfigurationPublish -Type GitHub -FilePath "$env:USERPROFILE\.secrets\github.token" -UserName 'EvotecIT' -RepositoryName 'MyModule' -Enabled</code>
/// </example>
/// <example>
/// <summary>Publish to Azure Artifacts (private feed preset)</summary>
/// <code>New-ConfigurationPublish -AzureDevOpsOrganization 'contoso' -AzureDevOpsProject 'Platform' -AzureArtifactsFeed 'Modules' -RepositoryCredentialUserName 'user@contoso.com' -RepositoryCredentialSecretFilePath "$env:USERPROFILE\.secrets\azdo.pat" -Enabled</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationPublish", DefaultParameterSetName = "ApiFromFile")]
public sealed class NewConfigurationPublishCommand : PSCmdlet
{
    /// <summary>Choose between PowerShellGallery and GitHub.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ApiKey")]
    [Parameter(Mandatory = true, ParameterSetName = "ApiFromFile")]
    public PowerForge.PublishDestination Type { get; set; }

    /// <summary>Azure DevOps organization name for the Azure Artifacts preset.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "AzureArtifacts")]
    public string AzureDevOpsOrganization { get; set; } = string.Empty;

    /// <summary>Optional Azure DevOps project name for project-scoped feeds.</summary>
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public string? AzureDevOpsProject { get; set; }

    /// <summary>Azure Artifacts feed name for the private gallery preset.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "AzureArtifacts")]
    public string AzureArtifactsFeed { get; set; } = string.Empty;

    /// <summary>API key to be used for publishing in clear text in a file.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ApiFromFile")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>API key to be used for publishing in clear text.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ApiKey")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>GitHub username (required for GitHub publishing).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? UserName { get; set; }

    /// <summary>Repository name override (GitHub or PowerShell repository name).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public string? RepositoryName { get; set; }

    /// <summary>Publishing tool/provider used for repository publishing. Ignored for GitHub publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public PowerForge.PublishTool Tool { get; set; } = PowerForge.PublishTool.Auto;

    /// <summary>Repository base URI (used for both source and publish unless overridden).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? RepositoryUri { get; set; }

    /// <summary>Repository source URI (PowerShellGet SourceLocation).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? RepositorySourceUri { get; set; }

    /// <summary>Repository publish URI (PowerShellGet PublishLocation).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? RepositoryPublishUri { get; set; }

    /// <summary>Whether to mark the repository as trusted (avoids prompts). Default: true.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public bool RepositoryTrusted { get; set; } = true;

    /// <summary>Repository priority for PSResourceGet (lower is higher priority).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public int? RepositoryPriority { get; set; }

    /// <summary>Repository API version for PSResourceGet registration (v2/v3).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public PowerForge.RepositoryApiVersion RepositoryApiVersion { get; set; } = PowerForge.RepositoryApiVersion.Auto;

    /// <summary>When true, registers/updates the repository before publishing. Default: true.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public bool EnsureRepositoryRegistered { get; set; } = true;

    /// <summary>When set, unregisters the repository after publish if it was created by this run.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public SwitchParameter UnregisterRepositoryAfterPublish { get; set; }

    /// <summary>Repository credential username (basic auth).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public string? RepositoryCredentialUserName { get; set; }

    /// <summary>Repository credential secret (password/token) in clear text.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public string? RepositoryCredentialSecret { get; set; }

    /// <summary>Repository credential secret (password/token) in a clear-text file.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public string? RepositoryCredentialSecretFilePath { get; set; }

    /// <summary>Enable publishing to the chosen destination.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public SwitchParameter Enabled { get; set; }

    /// <summary>Override tag name used for GitHub publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? OverwriteTagName { get; set; }

    /// <summary>Allow publishing lower version of a module on a PowerShell repository.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public SwitchParameter Force { get; set; }

    /// <summary>Optional ID of the artefact used for publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    [Parameter(ParameterSetName = "AzureArtifacts")]
    public string? ID { get; set; }

    /// <summary>Publish GitHub release as a release even if module prerelease is set.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public SwitchParameter DoNotMarkAsPreRelease { get; set; }

    /// <summary>When set, asks GitHub to generate release notes automatically.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public SwitchParameter GenerateReleaseNotes { get; set; }

    /// <summary>Emits publish configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var settings = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = ParameterSetName,
            Type = Type,
            AzureDevOpsOrganization = AzureDevOpsOrganization,
            AzureDevOpsProject = AzureDevOpsProject,
            AzureArtifactsFeed = AzureArtifactsFeed,
            FilePath = FilePath,
            ApiKey = ApiKey,
            UserName = UserName,
            RepositoryName = RepositoryName,
            Tool = Tool,
            RepositoryUri = RepositoryUri,
            RepositorySourceUri = RepositorySourceUri,
            RepositoryPublishUri = RepositoryPublishUri,
            RepositoryTrusted = RepositoryTrusted,
            RepositoryPriority = RepositoryPriority,
            RepositoryApiVersion = RepositoryApiVersion,
            EnsureRepositoryRegistered = EnsureRepositoryRegistered,
            UnregisterRepositoryAfterPublish = UnregisterRepositoryAfterPublish.IsPresent,
            RepositoryCredentialUserName = RepositoryCredentialUserName,
            RepositoryCredentialSecret = RepositoryCredentialSecret,
            RepositoryCredentialSecretSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryCredentialSecret)),
            RepositoryCredentialSecretFilePath = RepositoryCredentialSecretFilePath,
            RepositoryCredentialSecretFilePathSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryCredentialSecretFilePath)),
            Enabled = Enabled.IsPresent,
            OverwriteTagName = OverwriteTagName,
            Force = Force.IsPresent,
            ID = ID,
            DoNotMarkAsPreRelease = DoNotMarkAsPreRelease.IsPresent,
            GenerateReleaseNotes = GenerateReleaseNotes.IsPresent,
            Verbose = MyInvocation.BoundParameters.ContainsKey("Verbose")
        });

        WriteObject(settings);
    }
}
