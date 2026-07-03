using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
[Cmdlet(VerbsLifecycle.Invoke, "BenchmarkSuite", DefaultParameterSetName = ParameterSetPath, SupportsShouldProcess = true)]
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
    /// Optional work-item ordering override.
    /// </summary>
    [Parameter]
    public PowerShellBenchmarkRunOrder? RunOrder { get; set; }

    /// <summary>
    /// Optional delay between measured samples, in milliseconds.
    /// </summary>
    [Parameter]
    [ValidateRange(0, int.MaxValue)]
    public int? CooldownMilliseconds { get; set; }

    /// <summary>
    /// Optional summary outlier policy override.
    /// </summary>
    [Parameter]
    public PowerShellBenchmarkOutlierMode? OutlierMode { get; set; }

    /// <summary>
    /// Optional suite name override.
    /// </summary>
    [Parameter]
    public string? Suite { get; set; }

    /// <summary>
    /// Case or scenario names to include.
    /// </summary>
    [Parameter]
    [Alias("Cases", "Scenario", "Scenarios")]
    public string[]? Case { get; set; }

    /// <summary>
    /// Engine names to include.
    /// </summary>
    [Parameter]
    [Alias("Engines")]
    public string[]? Engine { get; set; }

    /// <summary>
    /// Operation names to include.
    /// </summary>
    [Parameter]
    [Alias("Operations")]
    public string[]? Operation { get; set; }

    /// <summary>
    /// Host labels to include.
    /// </summary>
    [Parameter]
    [Alias("Host", "Hosts")]
    public string[]? HostName { get; set; }

    /// <summary>
    /// Maximum time allowed for each external PowerShell host process.
    /// </summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int ExternalHostTimeoutSeconds { get; set; } = 1800;

    /// <summary>
    /// Optional profile override.
    /// </summary>
    [Parameter]
    public PowerShellBenchmarkProfileKind? Profile { get; set; }

    /// <summary>
    /// Optional cleanup override.
    /// </summary>
    [Parameter]
    public PowerShellBenchmarkCleanupMode? Cleanup { get; set; }

    /// <summary>
    /// Optional variables exposed to benchmark specs as <c>$BenchmarkVariables</c>.
    /// </summary>
    [Parameter]
    [Alias("Variables")]
    public Hashtable? Variable { get; set; }

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
        var callerRoot = SessionState.Path.CurrentFileSystemLocation.Path;
        var scriptRoot = callerRoot;
        string? resolvedSpecPath = null;
        ScriptBlock block;
        if (ParameterSetName == ParameterSetPath)
        {
            var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
            resolvedSpecPath = resolved;
            scriptRoot = System.IO.Path.GetDirectoryName(resolved) ?? scriptRoot;
            block = ScriptBlock.Create(File.ReadAllText(resolved));
        }
        else
        {
            block = Settings;
        }

        var benchmarkVariables = GetBenchmarkVariables();
        var suites = PowerShellBenchmarkDslRuntime.Evaluate(block, scriptRoot, benchmarkVariables);
        if (suites.Length == 0)
            ThrowTerminatingError(new ErrorRecord(new InvalidOperationException("Benchmark spec did not declare any benchmark suites."), "BenchmarkSuiteMissing", ErrorCategory.InvalidData, Path));

        var runner = new PowerShellBenchmarkRunner();
        var temporaryUserRunner = new PowerShellBenchmarkTemporaryUserExecutor();
        var hostRunner = new PowerShellBenchmarkHostExecutor();
        for (var suiteIndex = 0; suiteIndex < suites.Length; suiteIndex++)
        {
            var suite = suites[suiteIndex];
            ApplyOverrides(suite);
            PowerShellBenchmarkSuiteFilter.Apply(suite, GetSelection());
            ResolveSuitePaths(suite);
            if (Plan)
            {
                WriteObject(runner.Plan(suite), enumerateCollection: true);
                continue;
            }

            if (suite.Profile == PowerShellBenchmarkProfileKind.Current && hostRunner.RequiresHostProcesses(suite))
            {
                if (string.IsNullOrWhiteSpace(resolvedSpecPath))
                    ThrowTerminatingError(new ErrorRecord(new InvalidOperationException("Benchmark host matrix execution requires Invoke-BenchmarkSuite -Path because inline -Settings script blocks cannot be re-evaluated in child PowerShell hosts."), "BenchmarkHostMatrixRequiresPath", ErrorCategory.InvalidOperation, suite));

                if (!ShouldProcess(suite.OutputRoot, $"Run benchmark suite '{suite.Name}' across selected PowerShell hosts"))
                    continue;

                WriteObject(hostRunner.Run(suite, new PowerShellBenchmarkHostRunRequest
                {
                    SpecPath = resolvedSpecPath!,
                    SuiteIndex = suiteIndex,
                    WorkingDirectory = callerRoot,
                    OutputRoot = suite.OutputRoot,
                    WarmupCount = suite.WarmupCount,
                    IterationCount = suite.IterationCount,
                    RunMode = suite.RunMode,
                    RunOrder = suite.RunOrder,
                    CooldownMilliseconds = suite.CooldownMilliseconds,
                    OutlierMode = suite.OutlierMode,
                    SuiteName = suite.Name,
                    BenchmarkVariables = benchmarkVariables,
                    Selection = GetSelection(),
                    Hosts = GetSuiteHosts(suite),
                    ExternalHostTimeoutSeconds = ExternalHostTimeoutSeconds
                }));
                continue;
            }

            if (suite.Profile == PowerShellBenchmarkProfileKind.TemporaryLocalUser)
            {
                if (string.IsNullOrWhiteSpace(resolvedSpecPath))
                    ThrowTerminatingError(new ErrorRecord(new InvalidOperationException("Benchmark profile 'TemporaryLocalUser' requires Invoke-BenchmarkSuite -Path because inline -Settings script blocks cannot be re-evaluated in a temporary Windows user profile."), "BenchmarkTemporaryUserRequiresPath", ErrorCategory.InvalidOperation, suite));

                if (!ShouldProcess(suite.OutputRoot, $"Run benchmark suite '{suite.Name}' in a temporary local Windows user profile"))
                    continue;

                WriteObject(temporaryUserRunner.Run(new PowerShellBenchmarkTemporaryUserRequest
                {
                    SpecPath = resolvedSpecPath!,
                    SuiteIndex = suiteIndex,
                    WorkingDirectory = callerRoot,
                    OutputRoot = suite.OutputRoot,
                    WarmupCount = suite.WarmupCount,
                    IterationCount = suite.IterationCount,
                    RunMode = suite.RunMode,
                    RunOrder = suite.RunOrder,
                    CooldownMilliseconds = suite.CooldownMilliseconds,
                    OutlierMode = suite.OutlierMode,
                    SuiteName = suite.Name,
                    Cleanup = suite.Cleanup,
                    BenchmarkVariables = benchmarkVariables,
                    Selection = GetTemporaryUserSelection(suite),
                    ReadmePaths = suite.ReadmeBlocks.Select(block => block.Path).ToArray()
                }));
                continue;
            }

            if (!ShouldProcess(suite.OutputRoot, $"Run benchmark suite '{suite.Name}'"))
                continue;

            WriteObject(runner.Run(suite));
        }
    }

    private void ApplyOverrides(PowerShellBenchmarkSuite suite)
    {
        if (!string.IsNullOrWhiteSpace(OutputRoot)) suite.OutputRoot = OutputRoot!;
        if (WarmupCount.HasValue) suite.WarmupCount = Math.Max(0, WarmupCount.Value);
        if (IterationCount.HasValue) suite.IterationCount = Math.Max(1, IterationCount.Value);
        if (!string.IsNullOrWhiteSpace(RunMode)) suite.RunMode = RunMode!;
        if (RunOrder.HasValue) suite.RunOrder = RunOrder.Value;
        if (CooldownMilliseconds.HasValue) suite.CooldownMilliseconds = Math.Max(0, CooldownMilliseconds.Value);
        if (OutlierMode.HasValue) suite.OutlierMode = OutlierMode.Value;
        if (!string.IsNullOrWhiteSpace(Suite)) suite.Name = Suite!;
        if (Profile.HasValue) suite.Profile = Profile.Value;
        if (Cleanup.HasValue) suite.Cleanup = Cleanup.Value;
    }

    private PowerShellBenchmarkSelection GetSelection()
        => new()
        {
            Cases = Case ?? Array.Empty<string>(),
            Engines = Engine ?? Array.Empty<string>(),
            Operations = Operation ?? Array.Empty<string>(),
            Hosts = HostName ?? Array.Empty<string>()
        };

    private static string[] GetSuiteHosts(PowerShellBenchmarkSuite suite)
        => suite.Axes.FirstOrDefault(axis => string.Equals(axis.Name, "Host", StringComparison.OrdinalIgnoreCase))
               ?.Values
               .Select(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
               .Where(value => !string.IsNullOrWhiteSpace(value))
               .Select(value => value!)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToArray()
           ?? Array.Empty<string>();

    private PowerShellBenchmarkSelection GetTemporaryUserSelection(PowerShellBenchmarkSuite suite)
    {
        var selection = GetSelection();
        if (selection.Hosts.Length == 0)
        {
            var suiteHosts = GetSuiteHosts(suite);
            if (suiteHosts.Length > 0)
            {
                selection.Hosts = suiteHosts;
            }
            else
            {
                var currentHost = PowerShellBenchmarkHostRuntime.GetCurrentHostLabel();
                selection.Hosts = currentHost.StartsWith("Desktop-", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Desktop" }
                    : new[] { "Core" };
            }
        }
        return selection;
    }

    private void ResolveSuitePaths(PowerShellBenchmarkSuite suite)
    {
        suite.OutputRoot = ResolveFileSystemPath(suite.OutputRoot);
        foreach (var block in suite.ReadmeBlocks)
        {
            if (!string.IsNullOrWhiteSpace(block.Path))
                block.Path = ResolveFileSystemPath(block.Path);
        }
    }

    private string ResolveFileSystemPath(string path)
        => System.IO.Path.IsPathRooted(path)
            ? path
            : SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

    private Dictionary<string, string?> GetBenchmarkVariables()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (Variable is null) return variables;
        foreach (DictionaryEntry entry in Variable)
        {
            var name = Convert.ToString(entry.Key);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            variables[name!] = ConvertBenchmarkVariableValue(entry.Value);
        }

        return variables;
    }

    private static string? ConvertBenchmarkVariableValue(object? value)
    {
        if (value is null) return null;
        if (value is string text) return text;
        if (value is IEnumerable enumerable)
            return string.Join(",", enumerable.Cast<object?>().Select(static item => Convert.ToString(item)));
        return Convert.ToString(value);
    }
}
