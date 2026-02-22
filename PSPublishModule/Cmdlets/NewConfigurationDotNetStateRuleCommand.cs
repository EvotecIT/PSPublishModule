using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a preserve/restore rule for DotNet publish state handling.
/// </summary>
/// <example>
/// <summary>Create state rule for appsettings</summary>
/// <code>New-ConfigurationDotNetStateRule -SourcePath 'appsettings.json' -DestinationPath 'appsettings.json' -Overwrite</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetStateRule")]
[OutputType(typeof(DotNetPublishStateRule))]
public sealed class NewConfigurationDotNetStateRuleCommand : PSCmdlet
{
    /// <summary>
    /// Source path relative to publish output.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Destination path relative to publish output.
    /// </summary>
    [Parameter]
    public string? DestinationPath { get; set; }

    /// <summary>
    /// Overwrite destination during restore.
    /// </summary>
    [Parameter]
    public bool Overwrite { get; set; } = true;

    /// <summary>
    /// Emits a <see cref="DotNetPublishStateRule"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishStateRule
        {
            SourcePath = SourcePath.Trim(),
            DestinationPath = NormalizeNullable(DestinationPath),
            Overwrite = Overwrite
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
