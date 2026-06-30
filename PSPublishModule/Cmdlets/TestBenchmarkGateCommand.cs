using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Tests normalized benchmark summaries against a JSON baseline.
/// </summary>
/// <example>
/// <summary>Verify a benchmark summary against a baseline</summary>
/// <code>Test-BenchmarkGate -SummaryPath .\Build\Benchmarks\summary.json -BaselinePath .\Build\Benchmarks\baseline.json -Metric MedianMs</code>
/// </example>
[Cmdlet(VerbsDiagnostic.Test, "BenchmarkGate")]
[OutputType(typeof(BenchmarkGateResult))]
public sealed class TestBenchmarkGateCommand : PSCmdlet
{
    /// <summary>
    /// Summary JSON path.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SummaryPath { get; set; } = string.Empty;

    /// <summary>
    /// Baseline JSON path.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string BaselinePath { get; set; } = string.Empty;

    /// <summary>
    /// Metric name to evaluate.
    /// </summary>
    [Parameter]
    public string Metric { get; set; } = "MedianMs";

    /// <summary>
    /// Fields used to construct stable metric keys.
    /// </summary>
    [Parameter]
    public string[] GroupBy { get; set; } = { "Suite", "Scenario", "Operation", "Engine", "Host", "Variables" };

    /// <summary>
    /// Updates the baseline instead of verifying it.
    /// </summary>
    [Parameter]
    public SwitchParameter Update { get; set; }

    /// <summary>
    /// Allows new metrics missing from baseline.
    /// </summary>
    [Parameter]
    public SwitchParameter AllowNew { get; set; }

    /// <summary>
    /// Relative tolerance.
    /// </summary>
    [Parameter]
    public double RelativeTolerance { get; set; } = 0.10;

    /// <summary>
    /// Absolute tolerance in milliseconds.
    /// </summary>
    [Parameter]
    public double AbsoluteToleranceMs { get; set; }

    /// <summary>
    /// Emits gate result and writes an error when verification fails.
    /// </summary>
    protected override void ProcessRecord()
    {
        var request = new BenchmarkGateRequest
        {
            SummaryPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(SummaryPath),
            BaselinePath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(BaselinePath),
            Metric = Metric,
            GroupBy = GroupBy,
            BaselineMode = Update ? BenchmarkBaselineMode.Update : BenchmarkBaselineMode.Verify,
            FailOnNew = !AllowNew,
            RelativeTolerance = Math.Max(0, RelativeTolerance),
            AbsoluteToleranceMs = Math.Max(0, AbsoluteToleranceMs)
        };

        var result = new BenchmarkGateService().Evaluate(request);
        WriteObject(result);
        if (!result.Passed)
        {
            var message = result.Messages.Length > 0
                ? result.Messages[0]
                : "Benchmark gate failed.";
            ThrowTerminatingError(new ErrorRecord(new InvalidOperationException(message), "BenchmarkGateFailed", ErrorCategory.InvalidResult, result));
        }
    }
}
