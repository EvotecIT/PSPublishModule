using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates a marker-delimited benchmark block in a Markdown document.
/// </summary>
/// <example>
/// <summary>Update README benchmark block from a summary file</summary>
/// <code>Update-BenchmarkDocument -Path .\README.MD -BlockId managed-module-benchmark-table -SummaryPath .\Build\Benchmarks\summary.json</code>
/// </example>
/// <example>
/// <summary>Update a README block from published evidence in a catalog</summary>
/// <code>Update-BenchmarkDocument -Path .\README.MD -BlockId excel-read -Catalog .\Website\static\data\benchmarks\index.json -Comparison read -Platform windows -Framework net10.0</code>
/// </example>
[Cmdlet(VerbsData.Update, "BenchmarkDocument", SupportsShouldProcess = true, DefaultParameterSetName = SummaryParameterSet)]
[OutputType(typeof(BenchmarkDocumentUpdateResult))]
public sealed class UpdateBenchmarkDocumentCommand : PSCmdlet
{
    private const string SummaryParameterSet = "Summary";
    private const string CatalogParameterSet = "Catalog";

    /// <summary>
    /// Markdown document path.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Marker block identifier.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string BlockId { get; set; } = string.Empty;

    /// <summary>
    /// Summary JSON path.
    /// </summary>
    [Parameter(ParameterSetName = SummaryParameterSet)]
    [ValidateNotNullOrEmpty]
    public string SummaryPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional comparison JSON path.
    /// </summary>
    [Parameter(ParameterSetName = SummaryParameterSet)]
    public string? ComparisonPath { get; set; }

    /// <summary>
    /// Renderer name. Use <c>SummaryTable</c> or <c>ComparisonTable</c>.
    /// </summary>
    [Parameter(ParameterSetName = SummaryParameterSet)]
    public string Renderer { get; set; } = "SummaryTable";

    /// <summary>
    /// Schema 5 evidence catalog (index.json). The block shows the published runs of the selected comparisons.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = CatalogParameterSet)]
    [ValidateNotNullOrEmpty]
    public string Catalog { get; set; } = string.Empty;

    /// <summary>Comparison identifiers or group names to show.</summary>
    [Parameter(Mandatory = true, ParameterSetName = CatalogParameterSet)]
    [ValidateNotNullOrEmpty]
    public string[] Comparison { get; set; } = System.Array.Empty<string>();

    /// <summary>Platform whose published run is shown: windows, linux or macos.</summary>
    [Parameter(ParameterSetName = CatalogParameterSet)]
    [ValidateNotNullOrEmpty]
    public string Platform { get; set; } = "windows";

    /// <summary>Target framework of the run to show, such as net10.0. The newest published run is used when omitted.</summary>
    [Parameter(ParameterSetName = CatalogParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? Framework { get; set; }

    /// <summary>
    /// Updates the target document block.
    /// </summary>
    protected override void ProcessRecord()
    {
        var documentPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var renderer = new BenchmarkMarkdownRenderer();
        var markdown = ParameterSetName == CatalogParameterSet
            ? new BenchmarkComparisonMarkdownRenderer().Render(
                new BenchmarkComparisonViewBuilder().Build(SessionState.Path.GetUnresolvedProviderPathFromPSPath(Catalog)),
                new BenchmarkComparisonMarkdownRequest { Comparisons = Comparison, Platform = Platform, Framework = Framework })
            : Renderer switch
            {
                var value when string.Equals(value, "SummaryTable", System.StringComparison.OrdinalIgnoreCase) => renderer.RenderSummaryTable(ReadSummary()),
                var value when string.Equals(value, "ComparisonTable", System.StringComparison.OrdinalIgnoreCase) => renderer.RenderComparisonTable(ReadComparison()),
                _ => throw new PSArgumentException($"Benchmark document renderer '{Renderer}' is not supported. Use SummaryTable or ComparisonTable.")
            };

        if (!ShouldProcess(documentPath, $"Update benchmark block '{BlockId}'"))
            return;

        WriteObject(new BenchmarkDocumentUpdater().UpdateBlock(documentPath, BlockId, markdown));

        BenchmarkSummaryRow[] ReadSummary()
        {
            var summaryPath = string.IsNullOrWhiteSpace(SummaryPath)
                ? throw new PSArgumentException("SummaryPath is required when Renderer is SummaryTable.")
                : SessionState.Path.GetUnresolvedProviderPathFromPSPath(SummaryPath);
            return BenchmarkJson.ReadSummary(summaryPath);
        }

        BenchmarkComparisonRow[] ReadComparison()
        {
            var comparisonPath = string.IsNullOrWhiteSpace(ComparisonPath)
                ? throw new PSArgumentException("ComparisonPath is required when Renderer is ComparisonTable.")
                : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ComparisonPath!);
            return BenchmarkJson.ReadComparison(comparisonPath);
        }
    }
}
