using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides a way to configure publishing to PowerShell Gallery or GitHub.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet emits publish configuration consumed by <c>Invoke-ModuleBuild</c> / <c>Build-Module</c>.
/// Use <c>-Type</c> to choose a destination. For repository publishing, <c>-Tool</c> selects the provider (PowerShellGet/PSResourceGet/Auto).
/// </para>
/// <para>
/// For private repositories (for example Azure DevOps Artifacts / private NuGet v3 feeds), provide repository URIs and (optionally) credentials.
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
[Cmdlet(VerbsCommon.New, "ConfigurationPublish", DefaultParameterSetName = "ApiFromFile")]
public sealed class NewConfigurationPublishCommand : PSCmdlet
{
    /// <summary>Choose between PowerShellGallery and GitHub.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ApiKey")]
    [Parameter(Mandatory = true, ParameterSetName = "ApiFromFile")]
    public PowerForge.PublishDestination Type { get; set; }

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
    public string? RepositoryName { get; set; }

    /// <summary>Publishing tool/provider used for repository publishing. Ignored for GitHub publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
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
    public bool RepositoryTrusted { get; set; } = true;

    /// <summary>Repository priority for PSResourceGet (lower is higher priority).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public int? RepositoryPriority { get; set; }

    /// <summary>Repository API version for PSResourceGet registration (v2/v3).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public PowerForge.RepositoryApiVersion RepositoryApiVersion { get; set; } = PowerForge.RepositoryApiVersion.Auto;

    /// <summary>When true, registers/updates the repository before publishing. Default: true.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public bool EnsureRepositoryRegistered { get; set; } = true;

    /// <summary>When set, unregisters the repository after publish if it was created by this run.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public SwitchParameter UnregisterRepositoryAfterPublish { get; set; }

    /// <summary>Repository credential username (basic auth).</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? RepositoryCredentialUserName { get; set; }

    /// <summary>Repository credential secret (password/token) in clear text.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? RepositoryCredentialSecret { get; set; }

    /// <summary>Repository credential secret (password/token) in a clear-text file.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? RepositoryCredentialSecretFilePath { get; set; }

    /// <summary>Enable publishing to the chosen destination.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public SwitchParameter Enabled { get; set; }

    /// <summary>Override tag name used for GitHub publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public string? OverwriteTagName { get; set; }

    /// <summary>Allow publishing lower version of a module on a PowerShell repository.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
    public SwitchParameter Force { get; set; }

    /// <summary>Optional ID of the artefact used for publishing.</summary>
    [Parameter(ParameterSetName = "ApiKey")]
    [Parameter(ParameterSetName = "ApiFromFile")]
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
        var apiKeyToUse = ParameterSetName == "ApiFromFile"
            ? File.ReadAllText(FilePath).Trim()
            : ApiKey;

        if (Type == PowerForge.PublishDestination.GitHub && string.IsNullOrWhiteSpace(UserName))
            throw new PSArgumentException("UserName is required for GitHub. Please fix New-ConfigurationPublish and provide UserName");

        var repositorySecret = string.Empty;
        if (MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryCredentialSecretFilePath)) &&
            !string.IsNullOrWhiteSpace(RepositoryCredentialSecretFilePath))
        {
            repositorySecret = File.ReadAllText(RepositoryCredentialSecretFilePath!).Trim();
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryCredentialSecret)) &&
                 !string.IsNullOrWhiteSpace(RepositoryCredentialSecret))
        {
            repositorySecret = RepositoryCredentialSecret!.Trim();
        }

        var anyRepositoryUriProvided =
            !string.IsNullOrWhiteSpace(RepositoryUri) ||
            !string.IsNullOrWhiteSpace(RepositorySourceUri) ||
            !string.IsNullOrWhiteSpace(RepositoryPublishUri);

        if (anyRepositoryUriProvided)
        {
            if (string.IsNullOrWhiteSpace(RepositoryName))
                throw new PSArgumentException("RepositoryName is required when RepositoryUri/RepositorySourceUri/RepositoryPublishUri is provided.");
            if (string.Equals(RepositoryName!.Trim(), "PSGallery", StringComparison.OrdinalIgnoreCase))
                throw new PSArgumentException("RepositoryName cannot be 'PSGallery' when RepositoryUri/RepositorySourceUri/RepositoryPublishUri is provided.");
        }

        PublishRepositoryConfiguration? repoConfig = null;
        var hasRepoCred = !string.IsNullOrWhiteSpace(RepositoryCredentialUserName) && !string.IsNullOrWhiteSpace(repositorySecret);
        var hasRepoOptions = anyRepositoryUriProvided ||
                             hasRepoCred ||
                             MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryPriority)) ||
                             MyInvocation.BoundParameters.ContainsKey(nameof(RepositoryApiVersion)) ||
                             MyInvocation.BoundParameters.ContainsKey(nameof(EnsureRepositoryRegistered)) ||
                             UnregisterRepositoryAfterPublish.IsPresent;

        if (hasRepoOptions)
        {
            repoConfig = new PublishRepositoryConfiguration
            {
                Name = RepositoryName,
                Uri = RepositoryUri,
                SourceUri = RepositorySourceUri,
                PublishUri = RepositoryPublishUri,
                Trusted = RepositoryTrusted,
                Priority = RepositoryPriority,
                ApiVersion = RepositoryApiVersion,
                EnsureRegistered = EnsureRepositoryRegistered,
                UnregisterAfterUse = UnregisterRepositoryAfterPublish.IsPresent,
                Credential = hasRepoCred
                    ? new RepositoryCredential
                    {
                        UserName = RepositoryCredentialUserName,
                        Secret = repositorySecret
                    }
                    : null
            };
        }

        var publish = new PublishConfiguration
        {
            Destination = Type,
            Tool = Tool,
            ApiKey = apiKeyToUse,
            ID = ID,
            Enabled = Enabled.IsPresent,
            UserName = UserName,
            RepositoryName = RepositoryName,
            Repository = repoConfig,
            Force = Force.IsPresent,
            OverwriteTagName = OverwriteTagName,
            DoNotMarkAsPreRelease = DoNotMarkAsPreRelease.IsPresent,
            GenerateReleaseNotes = GenerateReleaseNotes.IsPresent,
            Verbose = MyInvocation.BoundParameters.ContainsKey("Verbose")       
        };

        var settings = new ConfigurationPublishSegment
        {
            Configuration = publish
        };

        WriteObject(settings);
    }
}
