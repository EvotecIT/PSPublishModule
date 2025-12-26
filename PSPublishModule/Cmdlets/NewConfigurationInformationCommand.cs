using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Describes what to include/exclude in the module build and how libraries are organized.
/// </summary>
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

    /// <summary>Advanced form to pass IncludeRoot/IncludePS1/IncludeAll as a single hashtable.</summary>
    [Parameter] public System.Collections.IDictionary? IncludeToArray { get; set; }

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

    private static IncludeToArrayEntry[]? NormalizeIncludeToArray(IDictionary? input)
    {
        if (input is null || input.Count == 0) return null;

        var output = new List<IncludeToArrayEntry>();
        foreach (DictionaryEntry entry in input)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key)) continue;

            var values = entry.Value switch
            {
                null => Array.Empty<string>(),
                string s => new[] { s },
                IEnumerable e => e.Cast<object?>()
                    .Select(v => v?.ToString())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!)
                    .ToArray(),
                _ => new[] { entry.Value.ToString() ?? string.Empty }
            };

            output.Add(new IncludeToArrayEntry { Key = key!, Values = values });
        }

        return output.Count == 0 ? null : output.ToArray();
    }
}
