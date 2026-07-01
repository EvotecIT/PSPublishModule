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
[Cmdlet(VerbsData.Update, "BenchmarkDocument", SupportsShouldProcess = true)]
[OutputType(typeof(BenchmarkDocumentUpdateResult))]
public sealed class UpdateBenchmarkDocumentCommand : PSCmdlet
{
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
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SummaryPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional comparison JSON path.
    /// </summary>
    [Parameter]
    public string? ComparisonPath { get; set; }

    /// <summary>
    /// Renderer name. Use <c>SummaryTable</c> or <c>ComparisonTable</c>.
    /// </summary>
    [Parameter]
    public string Renderer { get; set; } = "SummaryTable";

    /// <summary>
    /// Updates the target document block.
    /// </summary>
    protected override void ProcessRecord()
    {
        var documentPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var summaryPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(SummaryPath);
        var summary = BenchmarkJson.ReadSummary(summaryPath);
        var renderer = new BenchmarkMarkdownRenderer();
        var markdown = Renderer switch
        {
            var value when string.Equals(value, "SummaryTable", System.StringComparison.OrdinalIgnoreCase) => renderer.RenderSummaryTable(summary),
            var value when string.Equals(value, "ComparisonTable", System.StringComparison.OrdinalIgnoreCase) => renderer.RenderComparisonTable(ReadComparison()),
            _ => throw new PSArgumentException($"Benchmark document renderer '{Renderer}' is not supported. Use SummaryTable or ComparisonTable.")
        };

        if (!ShouldProcess(documentPath, $"Update benchmark block '{BlockId}'"))
            return;

        WriteObject(new BenchmarkDocumentUpdater().UpdateBlock(documentPath, BlockId, markdown));

        BenchmarkComparisonRow[] ReadComparison()
        {
            var comparisonPath = string.IsNullOrWhiteSpace(ComparisonPath)
                ? throw new PSArgumentException("ComparisonPath is required when Renderer is ComparisonTable.")
                : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ComparisonPath!);
            return BenchmarkJson.Read<BenchmarkComparisonRow[]>(comparisonPath);
        }
    }
}
