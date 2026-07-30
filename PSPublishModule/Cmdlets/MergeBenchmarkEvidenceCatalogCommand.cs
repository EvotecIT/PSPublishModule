using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Consolidates independently produced platform benchmark evidence bundles.
/// </summary>
/// <example>
/// <summary>Merge Windows and Linux evidence bundles</summary>
/// <code>Merge-BenchmarkEvidenceCatalog -SourcePath .\windows\index.json, .\linux\index.json -Path .\Website\data\index.json</code>
/// </example>
[Cmdlet(VerbsData.Merge, "BenchmarkEvidenceCatalog", SupportsShouldProcess = true)]
[OutputType(typeof(BenchmarkEvidenceCatalog))]
public sealed class MergeBenchmarkEvidenceCatalogCommand : PSCmdlet
{
    /// <summary>Source bundle catalog paths. Each normalized result must be beside its catalog.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string[] SourcePath { get; set; } = Array.Empty<string>();

    /// <summary>Destination catalog path. Normalized results are published beside it under immutable content-addressed names.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Platforms expected before the public comparison is complete.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? ExpectedPlatform { get; set; }

    /// <summary>Merges the verified bundles.</summary>
    protected override void ProcessRecord()
    {
        string outputPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        string[] sourcePaths = SourcePath
            .Select(SessionState.Path.GetUnresolvedProviderPathFromPSPath)
            .ToArray();
        if (!ShouldProcess(outputPath, $"Merge {sourcePaths.Length} benchmark evidence bundle(s)"))
            return;

        BenchmarkEvidenceCatalog result = new BenchmarkEvidenceCatalogService().MergeFiles(
            outputPath,
            sourcePaths,
            ExpectedPlatform);
        WriteObject(result);
    }
}
