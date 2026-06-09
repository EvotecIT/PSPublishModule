using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a DotNet publish target entry for DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create a service target with matrix runtimes</summary>
/// <code>New-ConfigurationDotNetTarget -Name 'My.Service' -ProjectPath 'src/My.Service/My.Service.csproj' -Framework 'net10.0-windows' -Runtimes 'win-x64','win-arm64' -Style PortableCompat -Zip</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetTarget")]
[OutputType(typeof(DotNetPublishTarget))]
public sealed class NewConfigurationDotNetTargetCommand : PSCmdlet
{
    /// <summary>
    /// Friendly target name.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional project catalog identifier.
    /// </summary>
    [Parameter]
    public string? ProjectId { get; set; }

    /// <summary>
    /// Path to target project file (*.csproj).
    /// </summary>
    [Parameter]
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Target kind metadata.
    /// </summary>
    [Parameter]
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>
    /// Target framework (for example net10.0 or net10.0-windows).
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Framework { get; set; } = string.Empty;

    /// <summary>
    /// Optional framework matrix values.
    /// </summary>
    [Parameter]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Runtime identifiers.
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
    /// Optional executable rename applied after publish.
    /// </summary>
    [Parameter]
    public string? RenameTo { get; set; }

    /// <summary>
    /// Use staging directory before final copy.
    /// </summary>
    [Parameter]
    public bool UseStaging { get; set; } = true;

    /// <summary>
    /// Clear final output before copy.
    /// </summary>
    [Parameter]
    public bool ClearOutput { get; set; } = true;

    /// <summary>
    /// Apply slimming cleanup.
    /// </summary>
    [Parameter]
    public bool Slim { get; set; } = true;

    /// <summary>
    /// Keep symbol files (*.pdb).
    /// </summary>
    [Parameter]
    public SwitchParameter KeepSymbols { get; set; }

    /// <summary>
    /// Keep documentation files (*.xml, *.pdf).
    /// </summary>
    [Parameter]
    public SwitchParameter KeepDocs { get; set; }

    /// <summary>
    /// Prune ref/ folder from output.
    /// </summary>
    [Parameter]
    public bool PruneReferences { get; set; } = true;

    /// <summary>
    /// Create zip artifact for target output.
    /// </summary>
    [Parameter]
    public SwitchParameter Zip { get; set; }

    /// <summary>
    /// Optional explicit zip output path.
    /// </summary>
    [Parameter]
    public string? ZipPath { get; set; }

    /// <summary>
    /// Optional zip file name template.
    /// </summary>
    [Parameter]
    public string? ZipNameTemplate { get; set; }

    /// <summary>
    /// Optional ReadyToRun toggle for non-AOT styles.
    /// </summary>
    [Parameter]
    public bool? ReadyToRun { get; set; }

    /// <summary>
    /// Optional signing policy for this target.
    /// </summary>
    [Parameter]
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>
    /// Optional service packaging settings for this target.
    /// </summary>
    [Parameter]
    public DotNetPublishServicePackageOptions? Service { get; set; }

    /// <summary>
    /// Optional preserve/restore settings for rebuild-safe deployments.
    /// </summary>
    [Parameter]
    public DotNetPublishStatePreservationOptions? State { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishTarget"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        if (string.IsNullOrWhiteSpace(ProjectId) && string.IsNullOrWhiteSpace(ProjectPath))
            throw new PSArgumentException("Provide either ProjectId or ProjectPath.");

        var target = new DotNetPublishTarget
        {
            Name = Name.Trim(),
            ProjectId = NormalizeNullable(ProjectId),
            ProjectPath = string.IsNullOrWhiteSpace(ProjectPath) ? string.Empty : ProjectPath!.Trim(),
            Kind = Kind,
            Publish = new DotNetPublishPublishOptions
            {
                Framework = Framework.Trim(),
                Frameworks = NormalizeArray(Frameworks),
                Runtimes = NormalizeArray(Runtimes),
                Style = Style,
                Styles = Styles?.Distinct().ToArray() ?? Array.Empty<DotNetPublishStyle>(),
                OutputPath = NormalizeNullable(OutputPath),
                RenameTo = NormalizeNullable(RenameTo),
                UseStaging = UseStaging,
                ClearOutput = ClearOutput,
                Slim = Slim,
                KeepSymbols = KeepSymbols.IsPresent,
                KeepDocs = KeepDocs.IsPresent,
                PruneReferences = PruneReferences,
                Zip = Zip.IsPresent,
                ZipPath = NormalizeNullable(ZipPath),
                ZipNameTemplate = NormalizeNullable(ZipNameTemplate),
                ReadyToRun = ReadyToRun,
                Sign = Sign,
                Service = Service,
                State = State
            }
        };

        WriteObject(target);
    }

    private static string[] NormalizeArray(string[]? values)
    {
        if (values is null || values.Length == 0) return Array.Empty<string>();
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
