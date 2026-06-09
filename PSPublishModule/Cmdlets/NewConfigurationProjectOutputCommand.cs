using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates output-root and staging defaults for a PowerShell-authored project build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationProjectOutput")]
[OutputType(typeof(ConfigurationProjectOutput))]
public sealed class NewConfigurationProjectOutputCommand : PSCmdlet
{
    /// <summary>
    /// Optional DotNetPublish output-root override.
    /// </summary>
    [Parameter]
    public string? OutputRoot { get; set; }

    /// <summary>
    /// Optional unified release staging root.
    /// </summary>
    [Parameter]
    public string? StageRoot { get; set; }

    /// <summary>
    /// Optional unified release manifest path.
    /// </summary>
    [Parameter]
    public string? ManifestJsonPath { get; set; }

    /// <summary>
    /// Optional unified release checksums path.
    /// </summary>
    [Parameter]
    public string? ChecksumsPath { get; set; }

    /// <summary>
    /// Disables top-level release checksum generation.
    /// </summary>
    [Parameter]
    public SwitchParameter NoChecksums { get; set; }

    /// <summary>
    /// Emits a <see cref="ConfigurationProjectOutput"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationProjectOutput
        {
            OutputRoot = NormalizeNullable(OutputRoot),
            StageRoot = NormalizeNullable(StageRoot),
            ManifestJsonPath = NormalizeNullable(ManifestJsonPath),
            ChecksumsPath = NormalizeNullable(ChecksumsPath),
            IncludeChecksums = !NoChecksums.IsPresent
        });
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
            return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
