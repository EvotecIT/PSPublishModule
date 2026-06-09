using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a benchmark gate definition for DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create benchmark gate with metrics</summary>
/// <code>
/// $m = New-ConfigurationDotNetBenchmarkMetric -Name 'storage.ms' -Source JsonPath -Path 'storage.ms'
/// New-ConfigurationDotNetBenchmarkGate -Id 'storage' -SourcePath 'Artifacts\bench.json' -BaselinePath 'Build\Baselines\storage.json' -Metrics $m
/// </code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetBenchmarkGate")]
[OutputType(typeof(DotNetPublishBenchmarkGate))]
public sealed class NewConfigurationDotNetBenchmarkGateCommand : PSCmdlet
{
    /// <summary>
    /// Gate identifier.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Source benchmark file path.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Baseline file path.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string BaselinePath { get; set; } = string.Empty;

    /// <summary>
    /// Enables this gate.
    /// </summary>
    [Parameter]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Baseline operation mode.
    /// </summary>
    [Parameter]
    public DotNetPublishBaselineMode BaselineMode { get; set; } = DotNetPublishBaselineMode.Verify;

    /// <summary>
    /// Fail when new metrics appear.
    /// </summary>
    [Parameter]
    public bool FailOnNew { get; set; } = true;

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
    /// Policy on regression.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnRegression { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>
    /// Policy on missing metrics.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnMissingMetric { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>
    /// Metric extraction rules.
    /// </summary>
    [Parameter]
    public DotNetPublishBenchmarkMetric[]? Metrics { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishBenchmarkGate"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishBenchmarkGate
        {
            Id = Id.Trim(),
            SourcePath = SourcePath.Trim(),
            BaselinePath = BaselinePath.Trim(),
            Enabled = Enabled,
            BaselineMode = BaselineMode,
            FailOnNew = FailOnNew,
            RelativeTolerance = RelativeTolerance < 0 ? 0 : RelativeTolerance,
            AbsoluteToleranceMs = AbsoluteToleranceMs < 0 ? 0 : AbsoluteToleranceMs,
            OnRegression = OnRegression,
            OnMissingMetric = OnMissingMetric,
            Metrics = (Metrics ?? System.Array.Empty<DotNetPublishBenchmarkMetric>())
                .Where(m => m is not null)
                .ToArray()
        });
    }
}

