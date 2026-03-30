using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an installer entry for a PowerShell-authored project build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationProjectInstaller")]
[OutputType(typeof(ConfigurationProjectInstaller))]
public sealed class NewConfigurationProjectInstallerCommand : PSCmdlet
{
    /// <summary>
    /// Installer identifier.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Name of the source target.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Path to the installer project file.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string InstallerProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// When set, prepares from the raw publish output instead of an auto-generated portable bundle.
    /// </summary>
    [Parameter]
    public SwitchParameter FromPublishOutput { get; set; }

    /// <summary>
    /// Harvest mode used during MSI prepare.
    /// </summary>
    [Parameter]
    public DotNetPublishMsiHarvestMode Harvest { get; set; } = DotNetPublishMsiHarvestMode.Auto;

    /// <summary>
    /// Optional runtime filter.
    /// </summary>
    [Parameter]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Optional framework filter.
    /// </summary>
    [Parameter]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Optional style filter.
    /// </summary>
    [Parameter]
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Optional WiX DirectoryRef identifier for generated harvest output.
    /// </summary>
    [Parameter]
    public string? HarvestDirectoryRefId { get; set; }

    /// <summary>
    /// Optional installer-specific MSBuild properties.
    /// </summary>
    [Parameter]
    public Hashtable? MsBuildProperty { get; set; }

    /// <summary>
    /// Emits a <see cref="ConfigurationProjectInstaller"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationProjectInstaller
        {
            Id = Id.Trim(),
            Target = Target.Trim(),
            InstallerProjectPath = InstallerProjectPath.Trim(),
            PrepareFromPortableBundle = !FromPublishOutput.IsPresent,
            Harvest = Harvest,
            Runtimes = NormalizeStrings(Runtimes),
            Frameworks = NormalizeStrings(Frameworks),
            Styles = Styles?.Distinct().ToArray() ?? Array.Empty<DotNetPublishStyle>(),
            HarvestDirectoryRefId = NormalizeNullable(HarvestDirectoryRefId),
            MsBuildProperties = ConvertHashtable(MsBuildProperty)
        });
    }

    private static Dictionary<string, string>? ConvertHashtable(Hashtable? value)
    {
        if (value is null || value.Count == 0)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in value)
        {
            var key = entry.Key?.ToString()?.Trim();
            var item = entry.Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(item))
                continue;

            result[key!] = item!;
        }

        return result.Count == 0 ? null : result;
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
