using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates workspace-validation defaults for a PowerShell-authored project build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationProjectWorkspace")]
[OutputType(typeof(ConfigurationProjectWorkspace))]
public sealed class NewConfigurationProjectWorkspaceCommand : PSCmdlet
{
    /// <summary>
    /// Optional workspace validation config path.
    /// </summary>
    [Parameter]
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Optional workspace validation profile.
    /// </summary>
    [Parameter]
    public string? Profile { get; set; }

    /// <summary>
    /// Optional features to enable.
    /// </summary>
    [Parameter]
    public string[]? EnableFeature { get; set; }

    /// <summary>
    /// Optional features to disable.
    /// </summary>
    [Parameter]
    public string[]? DisableFeature { get; set; }

    /// <summary>
    /// When set, disables workspace validation by default for this object.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipValidation { get; set; }

    /// <summary>
    /// Emits a <see cref="ConfigurationProjectWorkspace"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationProjectWorkspace
        {
            ConfigPath = NormalizeNullable(ConfigPath),
            Profile = NormalizeNullable(Profile),
            EnableFeatures = NormalizeStrings(EnableFeature),
            DisableFeatures = NormalizeStrings(DisableFeature),
            SkipValidation = SkipValidation.IsPresent
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
