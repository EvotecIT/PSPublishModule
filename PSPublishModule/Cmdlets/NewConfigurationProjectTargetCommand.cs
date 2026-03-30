using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a high-level target entry for a PowerShell-authored project build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationProjectTarget")]
[OutputType(typeof(ConfigurationProjectTarget))]
public sealed class NewConfigurationProjectTargetCommand : PSCmdlet
{
    /// <summary>
    /// Friendly target name.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Path to the project file to publish.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional target kind metadata.
    /// </summary>
    [Parameter]
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>
    /// Primary target framework.
    /// </summary>
    [Parameter]
    public string? Framework { get; set; }

    /// <summary>
    /// Optional framework matrix values.
    /// </summary>
    [Parameter]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Optional runtime matrix values.
    /// </summary>
    [Parameter]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Primary publish style.
    /// </summary>
    [Parameter]
    public DotNetPublishStyle Style { get; set; } = DotNetPublishStyle.PortableCompat;

    /// <summary>
    /// Optional style matrix values.
    /// </summary>
    [Parameter]
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Optional output path template.
    /// </summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Requested output categories for this target.
    /// </summary>
    [Parameter]
    public ConfigurationProjectTargetOutputType[]? OutputType { get; set; }

    /// <summary>
    /// Creates a zip for the raw publish output.
    /// </summary>
    [Parameter]
    public SwitchParameter Zip { get; set; }

    /// <summary>
    /// Uses a temporary staging directory before final copy.
    /// </summary>
    [Parameter]
    public bool UseStaging { get; set; } = true;

    /// <summary>
    /// Clears the final output directory before copy.
    /// </summary>
    [Parameter]
    public bool ClearOutput { get; set; } = true;

    /// <summary>
    /// Keeps symbol files.
    /// </summary>
    [Parameter]
    public SwitchParameter KeepSymbols { get; set; }

    /// <summary>
    /// Keeps documentation files.
    /// </summary>
    [Parameter]
    public SwitchParameter KeepDocs { get; set; }

    /// <summary>
    /// Optional ReadyToRun override.
    /// </summary>
    [Parameter]
    public bool? ReadyToRun { get; set; }

    /// <summary>
    /// Emits a <see cref="ConfigurationProjectTarget"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var frameworks = NormalizeStrings(Frameworks);
        var framework = NormalizeNullable(Framework);
        if (string.IsNullOrWhiteSpace(framework) && frameworks.Length == 0)
            throw new PSArgumentException("Provide Framework or Frameworks.");

        WriteObject(new ConfigurationProjectTarget
        {
            Name = Name.Trim(),
            ProjectPath = ProjectPath.Trim(),
            Kind = Kind,
            Framework = framework ?? string.Empty,
            Frameworks = frameworks,
            Runtimes = NormalizeStrings(Runtimes),
            Style = Style,
            Styles = Styles?.Distinct().ToArray() ?? Array.Empty<DotNetPublishStyle>(),
            OutputPath = NormalizeNullable(OutputPath),
            OutputType = OutputType is { Length: > 0 }
                ? OutputType.Distinct().ToArray()
                : new[] { ConfigurationProjectTargetOutputType.Tool },
            Zip = Zip.IsPresent,
            UseStaging = UseStaging,
            ClearOutput = ClearOutput,
            KeepSymbols = KeepSymbols.IsPresent,
            KeepDocs = KeepDocs.IsPresent,
            ReadyToRun = ReadyToRun
        });
    }

    private static string[] NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<string>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
            return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
