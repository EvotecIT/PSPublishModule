using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates preserve/restore state options for DotNet publish targets.
/// </summary>
/// <example>
/// <summary>Create state preservation options</summary>
/// <code>
/// $rule = New-ConfigurationDotNetStateRule -SourcePath 'config.json' -Overwrite
/// New-ConfigurationDotNetState -Enabled -Rules $rule -StoragePath 'Artifacts/DotNetPublish/State/{target}/{rid}/{framework}/{style}'
/// </code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetState")]
[OutputType(typeof(DotNetPublishStatePreservationOptions))]
public sealed class NewConfigurationDotNetStateCommand : PSCmdlet
{
    /// <summary>
    /// Enables state preservation.
    /// </summary>
    [Parameter]
    public SwitchParameter Enabled { get; set; }

    /// <summary>
    /// Optional storage path template.
    /// </summary>
    [Parameter]
    public string? StoragePath { get; set; }

    /// <summary>
    /// Clears storage before preserving state.
    /// </summary>
    [Parameter]
    public bool ClearStorage { get; set; } = true;

    /// <summary>
    /// Policy for missing source paths.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnMissingSource { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy for restore failures.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnRestoreFailure { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// State rules.
    /// </summary>
    [Parameter]
    public DotNetPublishStateRule[]? Rules { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishStatePreservationOptions"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishStatePreservationOptions
        {
            Enabled = Enabled.IsPresent,
            StoragePath = NormalizeNullable(StoragePath),
            ClearStorage = ClearStorage,
            OnMissingSource = OnMissingSource,
            OnRestoreFailure = OnRestoreFailure,
            Rules = (Rules ?? System.Array.Empty<DotNetPublishStateRule>())
                .Where(r => r is not null)
                .ToArray()
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

