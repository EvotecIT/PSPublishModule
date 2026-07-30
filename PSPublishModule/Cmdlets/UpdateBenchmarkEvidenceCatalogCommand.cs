using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Adds one normalized benchmark result to a platform-aware evidence catalog.
/// </summary>
/// <example>
/// <summary>Record a Windows publish lane</summary>
/// <code>$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts
/// dotnet run -c Release --project .\Benchmarks -- --artifacts .\Build\BenchmarkDotNet.Artifacts
/// $capture | Complete-BenchmarkProvenanceCapture
/// $result = Import-BenchmarkResult .\Build\BenchmarkDotNet.Artifacts
/// $result | Update-BenchmarkEvidenceCatalog -Path .\Website\data\benchmark-index.json -ComparisonId tabular-65k-v1 -ResultPath windows-full.json -RunMode full -Publish</code>
/// </example>
[Cmdlet(VerbsData.Update, "BenchmarkEvidenceCatalog", SupportsShouldProcess = true)]
[OutputType(typeof(BenchmarkEvidenceCatalog))]
public sealed class UpdateBenchmarkEvidenceCatalogCommand : PSCmdlet
{
    /// <summary>Normalized benchmark result, usually supplied by <c>Import-BenchmarkResult</c>.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [ValidateNotNull]
    public BenchmarkRunResult InputObject { get; set; } = null!;

    /// <summary>Evidence catalog JSON path.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Stable identifier shared only by equivalent workloads and fixture versions.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Portable path or URL to the normalized result consumed by the website.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ResultPath { get; set; } = string.Empty;

    /// <summary>Run mode such as quick or full.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string RunMode { get; set; } = string.Empty;

    /// <summary>
    /// Marks this lane as suitable for public benchmark claims. Published evidence must contain
    /// successful measurements without failures, runtime and runner identity, and exact clean-worktree
    /// source provenance in metadata keys <c>gitSha</c> and <c>gitWorktreeClean</c>.
    /// </summary>
    [Parameter]
    public SwitchParameter Publish { get; set; }

    /// <summary>Platforms expected before the public comparison is complete.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? ExpectedPlatform { get; set; }

    /// <summary>
    /// Producing operating-system platform for artifacts, such as BenchmarkDotNet CSV, that do not
    /// carry OS metadata. Conflicting embedded labels are rejected.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Platform { get; set; }

    /// <summary>Updates the catalog.</summary>
    protected override void ProcessRecord()
    {
        var catalogPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        if (!ShouldProcess(catalogPath, $"Record {ComparisonId}/{RunMode} benchmark evidence"))
            return;

        var result = new BenchmarkEvidenceCatalogService().UpdateFile(
            catalogPath,
            InputObject,
            ComparisonId,
            ResultPath,
            RunMode,
            Publish.IsPresent,
            ExpectedPlatform,
            Platform);
        WriteObject(result);
    }
}
