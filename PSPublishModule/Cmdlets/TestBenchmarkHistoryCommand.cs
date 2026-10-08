using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>Checks duration medians against accepted history from the same workload and runner environment.</summary>
/// <example>
/// <summary>Verify a run without changing accepted history</summary>
/// <code>Test-BenchmarkHistory -ResultPath ./run-report.json -HistoryPath ./history.json -WorkloadId topology-v1 -RunnerIdentity windows-renderer</code>
/// </example>
[Cmdlet(VerbsDiagnostic.Test, "BenchmarkHistory", SupportsShouldProcess = true)]
[OutputType(typeof(BenchmarkHistoryResult))]
public sealed class TestBenchmarkHistoryCommand : PSCmdlet
{
    /// <summary>Normalized run report including raw measured samples and environment metadata.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ResultPath { get; set; } = string.Empty;
    /// <summary>Local accepted timing history JSON path.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string HistoryPath { get; set; } = string.Empty;
    /// <summary>Stable workload version or fixture hash.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string WorkloadId { get; set; } = string.Empty;
    /// <summary>Stable runner pool or dedicated machine identity.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string RunnerIdentity { get; set; } = string.Empty;
    /// <summary>Explicitly accept this run for calibration instead of verifying it.</summary>
    [Parameter]
    public SwitchParameter Update { get; set; }
    /// <summary>Return an uncalibrated report without a terminating error; regressions still fail.</summary>
    [Parameter]
    public SwitchParameter AllowCalibration { get; set; }
    /// <summary>Minimum independent accepted runs required per lane.</summary>
    [Parameter]
    [ValidateRange(3, 100)]
    public int MinimumRuns { get; set; } = 5;
    /// <summary>Minimum independent measured iterations required per lane.</summary>
    [Parameter]
    [ValidateRange(3, 10000)]
    public int MinimumSamples { get; set; } = 5;
    /// <summary>Maximum recent accepted comparable runs used.</summary>
    [Parameter]
    [ValidateRange(3, 100)]
    public int WindowSize { get; set; } = 20;
    /// <summary>Minimum relative duration allowance in addition to measured noise.</summary>
    [Parameter]
    public double RelativeTolerance { get; set; } = 0.10;
    /// <summary>Minimum absolute duration allowance in milliseconds.</summary>
    [Parameter]
    public double AbsoluteToleranceMs { get; set; }

    /// <summary>Reads normalized data and delegates calibration and gating to the shared history service.</summary>
    protected override void ProcessRecord()
    {
        var historyPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(HistoryPath);
        var resultPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ResultPath);
        if (Update && !ShouldProcess(historyPath, "Accept benchmark run for timing calibration")) return;
        var result = new BenchmarkHistoryService().EvaluateFile(historyPath, BenchmarkJson.Read<BenchmarkRunResult>(resultPath),
            new BenchmarkHistoryRequest
            {
                WorkloadId = WorkloadId, RunnerIdentity = RunnerIdentity, MinimumRuns = MinimumRuns,
                MinimumSamples = MinimumSamples, WindowSize = WindowSize, RelativeTolerance = RelativeTolerance,
                AbsoluteToleranceMs = AbsoluteToleranceMs
            }, Update);
        WriteObject(result);
        if (!Update && !result.Passed && !(AllowCalibration && result.Calibrating && !result.Metrics.Any(metric => metric.Regressed)))
            ThrowTerminatingError(new ErrorRecord(new InvalidOperationException(result.Messages.FirstOrDefault() ?? "Timing history gate failed."),
                "BenchmarkHistoryFailed", ErrorCategory.InvalidResult, result));
    }
}
