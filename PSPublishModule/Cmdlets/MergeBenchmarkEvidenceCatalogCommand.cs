using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Consolidates independently produced platform benchmark evidence bundles into one schema 5 catalog.
/// </summary>
/// <remarks>
/// Every lane is verified against its result hash and re-evaluated against the declarations and publish policy.
/// Older runs of the same lane are kept in the superseded history. Catalogs from earlier schemas are rejected.
/// </remarks>
/// <example>
/// <summary>Merge Windows and Linux evidence bundles</summary>
/// <code>Merge-BenchmarkEvidenceCatalog -SourcePath .\windows\index.json, .\linux\index.json -Path .\Website\data\index.json -DeclarationPath .\Benchmarks\benchmarks.json</code>
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

    /// <summary>
    /// Schema 5 declaration document: the library policy and every comparison, including workloads without results.
    /// When omitted, declarations are merged from the source catalogs.
    /// </summary>
    [Parameter]
    [Alias("ComparisonPath")]
    [ValidateNotNullOrEmpty]
    public string? DeclarationPath { get; set; }

    /// <summary>Publish policy every lane is re-evaluated against. Defaults to the standard policy.</summary>
    [Parameter]
    public BenchmarkPublishPolicy? Policy { get; set; }

    /// <summary>Root-relative public URL directory for result links, such as /data/benchmarks/library-comparisons.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ResultPathPrefix { get; set; }

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
            ExpectedPlatform,
            string.IsNullOrWhiteSpace(DeclarationPath) ? null :
                new BenchmarkDeclarationService().Read(
                    SessionState.Path.GetUnresolvedProviderPathFromPSPath(DeclarationPath)),
            ResultPathPrefix,
            Policy);
        WriteObject(result);
    }
}
