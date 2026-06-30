using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Runs a reusable PowerShell benchmark suite.
/// </summary>
/// <example>
/// <summary>Run a benchmark spec</summary>
/// <code>Invoke-BenchmarkSuite -Path .\Benchmarks\module.benchmark.ps1</code>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "BenchmarkSuite", DefaultParameterSetName = ParameterSetPath)]
[OutputType(typeof(BenchmarkRunResult))]
[OutputType(typeof(PowerShellBenchmarkWorkItem))]
public sealed class InvokeBenchmarkSuiteCommand : PSCmdlet
{
    private const string ParameterSetPath = "Path";
    private const string ParameterSetSettings = "Settings";

    /// <summary>
    /// Path to a <c>.benchmark.ps1</c> spec.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetPath)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Inline benchmark DSL settings.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetSettings)]
    public ScriptBlock Settings { get; set; } = ScriptBlock.Create(string.Empty);

    /// <summary>
    /// Optional output root override.
    /// </summary>
    [Parameter]
    public string? OutputRoot { get; set; }

    /// <summary>
    /// Optional warmup count override.
    /// </summary>
    [Parameter]
    public int? WarmupCount { get; set; }

    /// <summary>
    /// Optional measured iteration count override.
    /// </summary>
    [Parameter]
    public int? IterationCount { get; set; }

    /// <summary>
    /// Run mode label.
    /// </summary>
    [Parameter]
    public string? RunMode { get; set; }

    /// <summary>
    /// Optional suite name override.
    /// </summary>
    [Parameter]
    public string? Suite { get; set; }

    /// <summary>
    /// Prints the resolved plan instead of executing measurements.
    /// </summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>
    /// Runs the command and emits a terminating error when benchmark execution fails.
    /// </summary>
    protected override void ProcessRecord()
    {
        var scriptRoot = SessionState.Path.CurrentFileSystemLocation.Path;
        ScriptBlock block;
        if (ParameterSetName == ParameterSetPath)
        {
            var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
            scriptRoot = System.IO.Path.GetDirectoryName(resolved) ?? scriptRoot;
            block = ScriptBlock.Create(". '" + resolved.Replace("'", "''") + "'");
        }
        else
        {
            block = Settings;
        }

        var suites = PowerShellBenchmarkDslRuntime.Evaluate(block, scriptRoot);
        var runner = new PowerShellBenchmarkRunner();
        foreach (var suite in suites)
        {
            ApplyOverrides(suite);
            if (Plan)
            {
                WriteObject(runner.Plan(suite), enumerateCollection: true);
                continue;
            }

            WriteObject(runner.Run(suite));
        }
    }

    private void ApplyOverrides(PowerShellBenchmarkSuite suite)
    {
        if (!string.IsNullOrWhiteSpace(OutputRoot)) suite.OutputRoot = OutputRoot!;
        if (WarmupCount.HasValue) suite.WarmupCount = Math.Max(0, WarmupCount.Value);
        if (IterationCount.HasValue) suite.IterationCount = Math.Max(1, IterationCount.Value);
        if (!string.IsNullOrWhiteSpace(RunMode)) suite.RunMode = RunMode!;
        if (!string.IsNullOrWhiteSpace(Suite)) suite.Name = Suite!;
    }
}
