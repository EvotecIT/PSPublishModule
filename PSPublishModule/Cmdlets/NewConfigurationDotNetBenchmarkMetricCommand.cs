using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a benchmark metric extraction rule for DotNet publish gates.
/// </summary>
/// <example>
/// <summary>Create JSON-path metric rule</summary>
/// <code>New-ConfigurationDotNetBenchmarkMetric -Name 'dashboard.storage.ms' -Source JsonPath -Path 'results.storageMs'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetBenchmarkMetric")]
[OutputType(typeof(DotNetPublishBenchmarkMetric))]
public sealed class NewConfigurationDotNetBenchmarkMetricCommand : PSCmdlet
{
    /// <summary>
    /// Metric identifier.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Metric source type.
    /// </summary>
    [Parameter]
    public DotNetPublishBenchmarkMetricSource Source { get; set; } = DotNetPublishBenchmarkMetricSource.JsonPath;

    /// <summary>
    /// JSON path when using JsonPath source.
    /// </summary>
    [Parameter]
    public string? Path { get; set; }

    /// <summary>
    /// Regex pattern when using Regex source.
    /// </summary>
    [Parameter]
    public string? Pattern { get; set; }

    /// <summary>
    /// Regex capture group index.
    /// </summary>
    [Parameter]
    public int Group { get; set; } = 1;

    /// <summary>
    /// Aggregation method.
    /// </summary>
    [Parameter]
    public DotNetPublishBenchmarkMetricAggregation Aggregation { get; set; } = DotNetPublishBenchmarkMetricAggregation.Last;

    /// <summary>
    /// Marks metric as required.
    /// </summary>
    [Parameter]
    public bool Required { get; set; } = true;

    /// <summary>
    /// Emits a <see cref="DotNetPublishBenchmarkMetric"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishBenchmarkMetric
        {
            Name = Name.Trim(),
            Source = Source,
            Path = NormalizeNullable(Path),
            Pattern = NormalizeNullable(Pattern),
            Group = Group < 0 ? 0 : Group,
            Aggregation = Aggregation,
            Required = Required
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

