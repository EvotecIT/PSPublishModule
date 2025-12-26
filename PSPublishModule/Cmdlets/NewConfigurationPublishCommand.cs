using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides a way to configure publishing to PowerShell Gallery or GitHub.
/// </summary>
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

    /// <summary>Emits publish configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var apiKeyToUse = ParameterSetName == "ApiFromFile"
            ? System.IO.File.ReadAllText(FilePath).Trim()
            : ApiKey;

        if (Type == PowerForge.PublishDestination.GitHub && string.IsNullOrWhiteSpace(UserName))
            throw new PSArgumentException("UserName is required for GitHub. Please fix New-ConfigurationPublish and provide UserName");

        var publish = new PublishConfiguration
        {
            Destination = Type,
            ApiKey = apiKeyToUse,
            ID = ID,
            Enabled = Enabled.IsPresent,
            UserName = UserName,
            RepositoryName = RepositoryName,
            Force = Force.IsPresent,
            OverwriteTagName = OverwriteTagName,
            DoNotMarkAsPreRelease = DoNotMarkAsPreRelease.IsPresent,
            Verbose = MyInvocation.BoundParameters.ContainsKey("Verbose")
        };

        var settings = new ConfigurationPublishSegment
        {
            Configuration = publish
        };

        WriteObject(settings);
    }
}
