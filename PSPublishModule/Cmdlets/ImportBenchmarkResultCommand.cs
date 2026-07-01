using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports BenchmarkDotNet or normalized benchmark artifacts into the common benchmark schema.
/// </summary>
/// <example>
/// <summary>Import BenchmarkDotNet artifacts</summary>
/// <code>Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -OutputPath .\Build\Benchmarks\normalized.json</code>
/// </example>
[Cmdlet(VerbsData.Import, "BenchmarkResult", SupportsShouldProcess = true)]
[OutputType(typeof(BenchmarkRunResult))]
public sealed class ImportBenchmarkResultCommand : PSCmdlet
{
    /// <summary>
    /// Input file or directory.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Optional suite name override.
    /// </summary>
    [Parameter]
    public string? Suite { get; set; }

    /// <summary>
    /// Optional output path for normalized JSON.
    /// </summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Imports and optionally writes normalized JSON.
    /// </summary>
    protected override void ProcessRecord()
    {
        var input = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var result = new BenchmarkResultImporter().Import(input, Suite);
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            var output = SessionState.Path.GetUnresolvedProviderPathFromPSPath(OutputPath!);
            if (ShouldProcess(output, "Write normalized benchmark result"))
            {
                BenchmarkJson.Write(output, result);
                result.Artifacts["normalized.json"] = output;
            }
        }

        WriteObject(result);
    }
}
