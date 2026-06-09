using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates config bootstrap copy rules for DotNet publish service packages.
/// </summary>
/// <example>
/// <summary>Create config bootstrap rule</summary>
/// <code>New-ConfigurationDotNetConfigBootstrapRule -SourcePath 'appsettings.example.json' -DestinationPath 'appsettings.json'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetConfigBootstrapRule")]
[OutputType(typeof(DotNetPublishConfigBootstrapRule))]
public sealed class NewConfigurationDotNetConfigBootstrapRuleCommand : PSCmdlet
{
    /// <summary>
    /// Source file path relative to output.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Destination file path relative to output.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Allows overwriting existing destination file.
    /// </summary>
    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    /// <summary>
    /// Policy when source file is missing.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnMissingSource { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Emits a <see cref="DotNetPublishConfigBootstrapRule"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishConfigBootstrapRule
        {
            SourcePath = SourcePath.Trim(),
            DestinationPath = DestinationPath.Trim(),
            Overwrite = Overwrite.IsPresent,
            OnMissingSource = OnMissingSource
        });
    }
}

