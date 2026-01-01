using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Describes what to include/exclude in the module build and how libraries are organized.
/// </summary>
/// <remarks>
/// <para>
/// This configuration segment controls:
/// </para>
/// <list type="bullet">
/// <item><description>Which folders/files are staged into the build output</description></item>
/// <item><description>Which folders are packaged into artefacts</description></item>
/// <item><description>Where compiled libraries are expected (Lib/Core, Lib/Default, Lib/Standard)</description></item>
/// </list>
/// </remarks>
/// <example>
/// <summary>Customize include/exclude rules for packaging</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationInformation -ExcludeFromPackage 'Ignore','Examples','Docs' -IncludeRoot '*.psd1','*.psm1','LICENSE' -IncludeAll 'Bin','Lib','en-US'</code>
/// <para>Controls what ends up in packaged artefacts while keeping staging lean.</para>
/// </example>
/// <example>
/// <summary>Add custom files during staging</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationInformation -IncludeCustomCode { Copy-Item -Path '.\Extras\*' -Destination $StagingPath -Recurse -Force }</code>
/// <para>Injects additional content into the staging folder before packaging.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationInformation")]
public sealed class NewConfigurationInformationCommand : PSCmdlet
{
    /// <summary>Folder name containing public functions to export (e.g., 'Public').</summary>
    [Parameter] public string? FunctionsToExportFolder { get; set; }

    /// <summary>Folder name containing public aliases to export (e.g., 'Public').</summary>
    [Parameter] public string? AliasesToExportFolder { get; set; }

    /// <summary>Paths or patterns to exclude from artefacts (e.g., 'Ignore','Docs','Examples').</summary>
    [Parameter] public string[]? ExcludeFromPackage { get; set; }

    /// <summary>File patterns from the root to include (e.g., '*.psm1','*.psd1','License*').</summary>
    [Parameter] public string[]? IncludeRoot { get; set; }

    /// <summary>Folder names where PS1 files should be included.</summary>
    [Parameter] public string[]? IncludePS1 { get; set; }

    /// <summary>Folder names to include entirely.</summary>
    [Parameter] public string[]? IncludeAll { get; set; }

    /// <summary>Scriptblock executed during staging to add custom files/folders.</summary>
    [Parameter] public ScriptBlock? IncludeCustomCode { get; set; }

    /// <summary>Advanced include rules. Accepts legacy hashtable (Key=&gt;Values) or <see cref="IncludeToArrayEntry"/>[].</summary>
    [Parameter]
    [IncludeToArrayEntriesTransformation]
    public IncludeToArrayEntry[]? IncludeToArray { get; set; }

    /// <summary>Relative path to libraries compiled for Core (default 'Lib/Core').</summary>
    [Parameter] public string? LibrariesCore { get; set; }

    /// <summary>Relative path to libraries for classic .NET (default 'Lib/Default').</summary>
    [Parameter] public string? LibrariesDefault { get; set; }

    /// <summary>Relative path to libraries for .NET Standard (default 'Lib/Standard').</summary>
    [Parameter] public string? LibrariesStandard { get; set; }

    /// <summary>Emits information configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var configuration = new InformationConfiguration
        {
            FunctionsToExportFolder = FunctionsToExportFolder,
            AliasesToExportFolder = AliasesToExportFolder,
            ExcludeFromPackage = ExcludeFromPackage,
            IncludeRoot = IncludeRoot,
            IncludePS1 = IncludePS1,
            IncludeAll = IncludeAll,
            IncludeCustomCode = IncludeCustomCode?.ToString(),
            IncludeToArray = NormalizeIncludeToArray(IncludeToArray),
            LibrariesCore = LibrariesCore,
            LibrariesDefault = LibrariesDefault,
            LibrariesStandard = LibrariesStandard
        };

        WriteObject(new ConfigurationInformationSegment { Configuration = configuration });
    }

    private static IncludeToArrayEntry[]? NormalizeIncludeToArray(IncludeToArrayEntry[]? input)
    {
        if (input is null || input.Length == 0) return null;

        var output = new List<IncludeToArrayEntry>();
        foreach (var entry in input)
        {
            if (entry is null) continue;
            if (string.IsNullOrWhiteSpace(entry.Key)) continue;

            var values = (entry.Values ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToArray();
            if (values.Length == 0) continue;

            output.Add(new IncludeToArrayEntry { Key = entry.Key.Trim(), Values = values });
        }

        return output.Count == 0 ? null : output.ToArray();
    }
}
