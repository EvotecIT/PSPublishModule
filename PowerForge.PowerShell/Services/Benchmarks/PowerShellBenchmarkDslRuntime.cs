using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Runtime used by locally scoped benchmark DSL helper functions.
/// </summary>
public static class PowerShellBenchmarkDslRuntime
{
    private static readonly AsyncLocal<PowerShellBenchmarkDslContext?> Current = new();

    /// <summary>
    /// Evaluates a script block with benchmark DSL helper functions.
    /// </summary>
    /// <param name="scriptBlock">Benchmark script block.</param>
    /// <param name="scriptRoot">Optional script root exposed as <c>$PSScriptRoot</c>.</param>
    /// <returns>Declared benchmark suites.</returns>
    public static PowerShellBenchmarkSuite[] Evaluate(ScriptBlock scriptBlock, string? scriptRoot = null)
        => Evaluate(scriptBlock, scriptRoot, benchmarkVariables: null);

    /// <summary>
    /// Evaluates a script block with benchmark DSL helper functions and caller-supplied benchmark variables.
    /// </summary>
    /// <param name="scriptBlock">Benchmark script block.</param>
    /// <param name="scriptRoot">Optional script root exposed as <c>$PSScriptRoot</c>.</param>
    /// <param name="benchmarkVariables">Optional caller-supplied variables exposed as <c>$BenchmarkVariables</c>.</param>
    /// <returns>Declared benchmark suites.</returns>
    public static PowerShellBenchmarkSuite[] Evaluate(ScriptBlock scriptBlock, string? scriptRoot, IReadOnlyDictionary<string, string?>? benchmarkVariables)
    {
        if (scriptBlock is null) throw new ArgumentNullException(nameof(scriptBlock));
        var previous = Current.Value;
        var context = new PowerShellBenchmarkDslContext
        {
            ScriptRoot = scriptRoot,
            BenchmarkVariables = CopyBenchmarkVariables(benchmarkVariables)
        };
        Current.Value = context;
        Runspace? createdRunspace = null;
        var previousRunspace = Runspace.DefaultRunspace;
        List<AliasSnapshot?> aliasSnapshots = new();
        FunctionSnapshot? compareFunction = null;
        try
        {
            if (Runspace.DefaultRunspace is null)
            {
                createdRunspace = RunspaceFactory.CreateRunspace();
                createdRunspace.Open();
                Runspace.DefaultRunspace = createdRunspace;
            }

            foreach (var aliasName in CreateFunctionBodies().Keys)
                aliasSnapshots.Add(RemoveAlias(aliasName));
            compareFunction = SetLegacyCompareFunction();
            InvokeRootBlock(scriptBlock);
            return context.Suites.ToArray();
        }
        finally
        {
            RestoreFunction(compareFunction);
            for (var i = aliasSnapshots.Count - 1; i >= 0; i--)
                RestoreAlias(aliasSnapshots[i]);
            Runspace.DefaultRunspace = previousRunspace;
            createdRunspace?.Dispose();
            Current.Value = previous;
        }
    }

    /// <summary>
    /// Adds a benchmark suite from the local DSL.
    /// </summary>
    /// <param name="name">Suite name.</param>
    /// <param name="outputRoot">Output root.</param>
    /// <param name="scriptBlock">Suite body.</param>
    public static void Benchmark(string name, string? outputRoot, ScriptBlock scriptBlock)
    {
        var context = RequireContext();
        var suite = new PowerShellBenchmarkSuite
        {
            Name = RequireName(name, "benchmark name"),
            OutputRoot = string.IsNullOrWhiteSpace(outputRoot) ? Path.Combine("Build", "Benchmarks", RequireName(name, "benchmark name")) : outputRoot!.Trim()
        };
        context.Suites.Add(suite);
        context.SuiteStack.Push(suite);
        try
        {
            InvokeDslBlock(scriptBlock);
        }
        finally
        {
            context.SuiteStack.Pop();
        }
    }

    /// <summary>
    /// Registers caller-visible helper functions captured by the PowerShell command surface.
    /// </summary>
    /// <param name="functions">Function definitions keyed by name.</param>
    public static void RegisterCallerFunctions(IReadOnlyDictionary<string, string>? functions)
    {
        if (functions is null || functions.Count == 0) return;
        var context = RequireContext();
        foreach (var entry in functions)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                continue;
            if (context.CallerFunctions.ContainsKey(entry.Key))
                continue;
            context.CallerFunctions[entry.Key] = CaptureScriptText(ScriptBlock.Create(entry.Value), context.ScriptRoot);
        }
    }

    /// <summary>
    /// Evaluates a cases block.
    /// </summary>
    /// <param name="scriptBlock">Cases body.</param>
    public static void Cases(ScriptBlock scriptBlock) => InvokeDslBlock(scriptBlock);

    /// <summary>
    /// Adds one case.
    /// </summary>
    /// <param name="name">Case name.</param>
    /// <param name="values">Case values.</param>
    public static void Case(string name, Hashtable? values)
    {
        var suite = RequireSuite();
        var item = new PowerShellBenchmarkCase { Name = RequireName(name, "case name") };
        if (values is not null)
        {
            foreach (DictionaryEntry entry in values)
                item.Values[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = entry.Value;
        }

        suite.Cases.Add(item);
    }

    /// <summary>
    /// Adds generated cases from a script block.
    /// </summary>
    /// <param name="scriptBlock">Case source script block.</param>
    public static void From(ScriptBlock scriptBlock)
    {
        var suite = RequireSuite();
        foreach (var value in InvokeStrict(CloseScriptBlock(scriptBlock)))
        {
            var item = ConvertToCase(value);
            suite.Cases.Add(item);
        }
    }

    /// <summary>
    /// Adds generated cases from already evaluated values.
    /// </summary>
    /// <param name="values">Case source values.</param>
    public static void FromValues(object?[] values)
    {
        var suite = RequireSuite();
        foreach (var value in Flatten(values ?? Array.Empty<object?>()))
        {
            if (value is null)
                continue;
            suite.Cases.Add(ConvertToCase(PSObject.AsPSObject(value)));
        }
    }

    /// <summary>
    /// Gets benchmark script text with file-root variables rewritten for later invocation.
    /// </summary>
    /// <param name="scriptBlock">Script block to capture.</param>
    /// <returns>Captured script text.</returns>
    public static string CaptureScriptText(ScriptBlock scriptBlock)
        => CaptureScriptText(scriptBlock, Current.Value?.ScriptRoot);

    /// <summary>
    /// Gets benchmark script text with a supplied file root rewritten for later invocation.
    /// </summary>
    /// <param name="scriptBlock">Script block to capture.</param>
    /// <param name="scriptRoot">Script root used for <c>$PSScriptRoot</c>.</param>
    /// <returns>Captured script text.</returns>
    public static string CaptureScriptText(ScriptBlock scriptBlock, string? scriptRoot)
    {
        if (scriptBlock is null) throw new ArgumentNullException(nameof(scriptBlock));
        var scriptText = scriptBlock.ToString();
        var rooted = string.IsNullOrWhiteSpace(scriptRoot)
            ? scriptText
            : ReplaceScriptRootVariables(scriptText, scriptRoot!);
        return PowerShellNativeExitCodeGuard.AddChecks(rooted);
    }

    /// <summary>
    /// Adds an axis.
    /// </summary>
    /// <param name="name">Axis name.</param>
    /// <param name="values">Axis values.</param>
    public static void Axis(string name, object?[] values)
    {
        var suite = RequireSuite();
        var axisName = RequireName(name, "axis name");
        if (suite.Axes.Any(axis => string.Equals(axis.Name, axisName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Benchmark suite '{suite.Name}' already defines axis '{axisName}'.");
        var axis = new PowerShellBenchmarkAxis { Name = axisName };
        axis.Values.AddRange(Flatten(values));
        suite.Axes.Add(axis);
    }

    /// <summary>
    /// Sets suite setup block.
    /// </summary>
    /// <param name="scriptBlock">Setup block.</param>
    public static void Setup(ScriptBlock scriptBlock) => RequireSuite().Setup = CloseScriptBlock(scriptBlock);

    /// <summary>
    /// Sets suite data block.
    /// </summary>
    /// <param name="scriptBlock">Data block.</param>
    public static void Data(ScriptBlock scriptBlock) => RequireSuite().Data = CloseScriptBlock(scriptBlock);

    /// <summary>
    /// Sets benchmark run policy defaults.
    /// </summary>
    /// <param name="warmup">Optional warmup iteration count.</param>
    /// <param name="iteration">Optional measured iteration count.</param>
    /// <param name="runMode">Optional run mode label.</param>
    /// <param name="order">Optional work-item ordering strategy.</param>
    /// <param name="cooldownMilliseconds">Optional delay between measured samples.</param>
    /// <param name="outlierMode">Optional summary outlier policy.</param>
    public static void Policy(int? warmup, int? iteration, string? runMode, string? order, int? cooldownMilliseconds, string? outlierMode)
        => Policy(warmup, iteration, runMode, order, memoryCleanup: null, cooldownMilliseconds, outlierMode);

    /// <summary>
    /// Applies benchmark execution policy to the current DSL suite.
    /// </summary>
    /// <param name="warmup">Optional warmup iteration count.</param>
    /// <param name="iteration">Optional measured iteration count.</param>
    /// <param name="runMode">Optional run mode label.</param>
    /// <param name="order">Optional work-item ordering strategy.</param>
    /// <param name="memoryCleanup">Optional managed-memory cleanup policy.</param>
    /// <param name="cooldownMilliseconds">Optional delay between measured samples.</param>
    /// <param name="outlierMode">Optional summary outlier policy.</param>
    public static void Policy(int? warmup, int? iteration, string? runMode, string? order, string? memoryCleanup, int? cooldownMilliseconds, string? outlierMode)
    {
        var suite = RequireSuite();
        if (warmup.HasValue)
            suite.WarmupCount = Math.Max(0, warmup.Value);
        if (iteration.HasValue)
            suite.IterationCount = Math.Max(1, iteration.Value);
        if (!string.IsNullOrWhiteSpace(runMode))
            suite.RunMode = runMode!;
        if (!string.IsNullOrWhiteSpace(order))
            suite.RunOrder = ParseEnum<PowerShellBenchmarkRunOrder>(order!, "run order");
        if (!string.IsNullOrWhiteSpace(memoryCleanup))
            suite.MemoryCleanup = ParseEnum<PowerShellBenchmarkMemoryCleanupMode>(memoryCleanup!, "memory cleanup mode");
        if (cooldownMilliseconds.HasValue)
            suite.CooldownMilliseconds = Math.Max(0, cooldownMilliseconds.Value);
        if (!string.IsNullOrWhiteSpace(outlierMode))
            suite.OutlierMode = ParseEnum<PowerShellBenchmarkOutlierMode>(outlierMode!, "outlier mode");
    }

    /// <summary>
    /// Sets suite skip block.
    /// </summary>
    /// <param name="scriptBlock">Skip block.</param>
    public static void Skip(ScriptBlock scriptBlock) => RequireSuite().Skip = CloseScriptBlock(scriptBlock);

    /// <summary>
    /// Sets suite validation block.
    /// </summary>
    /// <param name="scriptBlock">Validation block.</param>
    public static void Validate(ScriptBlock scriptBlock) => RequireSuite().Validate = CloseScriptBlock(scriptBlock);

    /// <summary>
    /// Sets suite profile isolation mode.
    /// </summary>
    /// <param name="name">Profile name.</param>
    /// <param name="cleanup">Optional cleanup mode.</param>
    public static void Profile(string name, string? cleanup)
    {
        var suite = RequireSuite();
        if (!Enum.TryParse<PowerShellBenchmarkProfileKind>(RequireName(name, "profile name"), ignoreCase: true, out var profile))
            throw new NotSupportedException($"Benchmark profile '{name}' is not supported.");
        suite.Profile = profile;
        if (!string.IsNullOrWhiteSpace(cleanup))
            suite.Cleanup = ParseCleanup(cleanup!);
    }

    /// <summary>
    /// Sets suite cleanup mode.
    /// </summary>
    /// <param name="name">Cleanup mode name.</param>
    public static void Cleanup(string name)
        => RequireSuite().Cleanup = ParseCleanup(name);

    /// <summary>
    /// Adds an engine.
    /// </summary>
    /// <param name="name">Engine name.</param>
    /// <param name="scriptBlock">Engine body.</param>
    public static void Engine(string name, ScriptBlock scriptBlock)
    {
        var context = RequireContext();
        var suite = RequireSuite();
        var engineName = RequireName(name, "engine name");
        if (suite.Engines.Any(engine => string.Equals(engine.Name, engineName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Benchmark suite '{suite.Name}' already defines engine '{engineName}'.");
        var engine = new PowerShellBenchmarkEngine { Name = engineName };
        suite.Engines.Add(engine);
        context.EngineStack.Push(engine);
        try
        {
            InvokeDslBlock(scriptBlock);
        }
        finally
        {
            context.EngineStack.Pop();
        }
    }

    /// <summary>
    /// Adds an operation handler to the current engine.
    /// </summary>
    /// <param name="name">Operation name.</param>
    /// <param name="scriptBlock">Operation block.</param>
    public static void Operation(string name, ScriptBlock scriptBlock)
    {
        var context = RequireContext();
        if (context.EngineStack.Count == 0)
            throw new InvalidOperationException("operation can only be used inside engine.");
        var operationName = RequireName(name, "operation name");
        var engine = context.EngineStack.Peek();
        if (engine.Operations.ContainsKey(operationName))
            throw new InvalidOperationException($"Benchmark engine '{engine.Name}' already defines operation '{operationName}'.");
        engine.Operations[operationName] = CloseScriptBlock(scriptBlock);
    }

    /// <summary>
    /// Adds a custom metric.
    /// </summary>
    /// <param name="name">Metric name.</param>
    /// <param name="scriptBlock">Metric block.</param>
    public static void Metric(string name, ScriptBlock scriptBlock)
    {
        var suite = RequireSuite();
        var metricName = RequireName(name, "metric name");
        if (suite.Metrics.Any(metric => string.Equals(metric.Name, metricName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Benchmark suite '{suite.Name}' already defines metric '{metricName}'.");
        if (IsReservedMetricName(metricName))
            throw new InvalidOperationException($"Benchmark metric name '{metricName}' is reserved for built-in benchmark artifact columns.");
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = metricName, ScriptBlock = CloseScriptBlock(scriptBlock) });
    }

    private static bool IsReservedMetricName(string name)
        => ReservedMetricNames.Contains(name);

    private static readonly HashSet<string> ReservedMetricNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "Suite",
        "Scenario",
        "Operation",
        "Engine",
        "Host",
        "OS",
        "RunId",
        "RunMode",
        "Iteration",
        "Status",
        "DurationMs",
        "Reason",
        "AllocatedBytes",
        "WorkingSetDeltaBytes",
        "OutputMetric",
        "SampleCount",
        "FailureCount",
        "MedianMs",
        "MeanMs",
        "MinMs",
        "MaxMs",
        "P95Ms",
        "P95",
        "P99Ms",
        "P99",
        "StdDevMs",
        "StdDev",
        "StdErrMs",
        "StdErr"
    };

    /// <summary>
    /// Adds a comparison definition.
    /// </summary>
    /// <param name="dimension">Dimension name.</param>
    /// <param name="baseline">Baseline value.</param>
    /// <param name="metric">Metric names.</param>
    public static void Compare(string dimension, string baseline, string[]? metric)
        => Compare(dimension, baseline, metric, tieTolerance: 0, requireBaselineFastest: false);

    /// <summary>
    /// Adds a comparison definition with a practical tie tolerance.
    /// </summary>
    /// <param name="dimension">Dimension name.</param>
    /// <param name="baseline">Baseline value.</param>
    /// <param name="metric">Metric names.</param>
    /// <param name="tieTolerance">Fractional tolerance used to label practically equivalent results, such as <c>0.05</c> for five percent.</param>
    public static void Compare(string dimension, string baseline, string[]? metric, double tieTolerance)
        => Compare(dimension, baseline, metric, tieTolerance, requireBaselineFastest: false);

    /// <summary>
    /// Adds a comparison definition with a practical tie tolerance and an optional fastest-baseline gate.
    /// </summary>
    /// <param name="dimension">Dimension name.</param>
    /// <param name="baseline">Baseline value.</param>
    /// <param name="metric">Metric names.</param>
    /// <param name="tieTolerance">Fractional tolerance used to label practically equivalent results, such as <c>0.05</c> for five percent.</param>
    /// <param name="requireBaselineFastest">Whether to fail when the baseline is materially slower than a successful competitor.</param>
    public static void Compare(string dimension, string baseline, string[]? metric, double tieTolerance, bool requireBaselineFastest)
        => RequireSuite().Comparisons.Add(new PowerShellBenchmarkComparison
        {
            Dimension = string.IsNullOrWhiteSpace(dimension) ? "Engine" : dimension.Trim(),
            Baseline = baseline ?? string.Empty,
            Metrics = metric is { Length: > 0 } ? metric : new[] { "MedianMs" },
            TieTolerance = tieTolerance,
            RequireBaselineFastest = requireBaselineFastest
        });

    /// <summary>
    /// Adds a README or Markdown block target.
    /// </summary>
    /// <param name="path">Document path.</param>
    /// <param name="block">Block id.</param>
    /// <param name="renderer">Renderer name.</param>
    public static void Readme(string path, string block, string? renderer)
        => RequireSuite().ReadmeBlocks.Add(new PowerShellBenchmarkReadmeBlock
        {
            Path = path ?? string.Empty,
            BlockId = block ?? string.Empty,
            Renderer = string.IsNullOrWhiteSpace(renderer) ? "SummaryTable" : renderer!.Trim()
        });

    /// <summary>
    /// Sets requested artifacts.
    /// </summary>
    /// <param name="values">Artifact names.</param>
    public static void Artifacts(object?[] values)
    {
        var kinds = BenchmarkArtifactKind.None;
        foreach (var value in Flatten(values))
        {
            var name = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!Enum.TryParse<BenchmarkArtifactKind>(name, ignoreCase: true, out var kind))
                throw new NotSupportedException($"Benchmark artifact kind '{name}' is not supported.");
            kinds |= kind;
        }

        RequireSuite().Artifacts = kinds;
    }

    private static PowerShellBenchmarkCleanupMode ParseCleanup(string name)
    {
        var cleanupName = RequireName(name, "cleanup mode");
        if (!Enum.TryParse<PowerShellBenchmarkCleanupMode>(cleanupName, ignoreCase: true, out var cleanup))
            throw new NotSupportedException($"Benchmark cleanup mode '{cleanupName}' is not supported.");
        return cleanup;
    }

    private static T ParseEnum<T>(string name, string label) where T : struct, Enum
    {
        var valueName = RequireName(name, label);
        if (!Enum.TryParse<T>(valueName, ignoreCase: true, out var value))
            throw new NotSupportedException($"Benchmark {label} '{valueName}' is not supported.");
        return value;
    }

    private static IEnumerable<PSObject> InvokeStrict(ScriptBlock scriptBlock)
        => InvokeWithScriptRoot(scriptBlock, CreateFunctions());

    private static Dictionary<string, string> CreateFunctionBodies()
    {
        static string Call(string methodName, string parameterBlock, string arguments)
            => $"{parameterBlock} __PowerForgeBenchmarkDslInvoke -Name '{methodName}' -Arguments ({arguments})";

        const string AssertPathBody = "param([Parameter(Position=0, Mandatory=$true)] [string] $Path, [switch] $Not, [string] $Message, [switch] $PassThru) $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path); $exists = [System.IO.File]::Exists($resolved) -or [System.IO.Directory]::Exists($resolved); if ($exists -eq (-not $Not.IsPresent)) { if ($PassThru) { $resolved }; return }; if ([string]::IsNullOrWhiteSpace($Message)) { if ($Not.IsPresent) { $Message = \"Expected path '$resolved' not to exist.\" } else { $Message = \"Expected path '$resolved' to exist.\" } }; throw $Message";
        const string AssertValueBody = "param([Parameter(Position=0, Mandatory=$true)] [object] $Actual, [Parameter(Position=1)] [object] $Expected, [switch] $NotNull, [string] $Message, [switch] $PassThru) $passed = if ($NotNull.IsPresent) { $null -ne $Actual } else { [object]::Equals($Actual, $Expected) }; if ($passed) { if ($PassThru) { $Actual }; return }; if ([string]::IsNullOrWhiteSpace($Message)) { if ($NotNull.IsPresent) { $Message = 'Expected benchmark value not to be null.' } else { $Message = \"Expected benchmark value '$Actual' to equal '$Expected'.\" } }; throw $Message";
        const string InputBody = "param([Parameter(Position=0, Mandatory=$true)] [string] $Name, [Parameter(Position=1)] [object] $Default, [switch] $Required, [switch] $Int, [switch] $Bool) if ($Int.IsPresent) { $value = $BenchmarkVariables[$Name]; if ([string]::IsNullOrWhiteSpace([string] $value)) { if ($Required.IsPresent) { throw \"Benchmark variable '$Name' is required.\" }; if ($null -eq $Default) { return @() }; if ($Default -is [array]) { return @($Default | ForEach-Object { [int] $_ }) }; return @([string] $Default -split ',' | Where-Object { $_.Trim() } | ForEach-Object { [int] $_.Trim() }) }; $items = @(); foreach ($entry in ([string] $value -split ',')) { $trimmed = $entry.Trim(); if ($trimmed) { $items += [int] $trimmed } }; if ($items.Count -eq 0) { if ($Required.IsPresent) { throw \"Benchmark variable '$Name' did not contain any integer values.\" }; return $Default }; return $items } if ($Bool.IsPresent) { $value = $BenchmarkVariables[$Name]; if ([string]::IsNullOrWhiteSpace([string] $value)) { if ($Required.IsPresent) { throw \"Benchmark variable '$Name' is required.\" }; if ($null -eq $Default) { return $false }; switch -Regex ([string] $Default) { '^(?i:true|1|yes|on)$' { return $true } '^(?i:false|0|no|off)$' { return $false } default { throw \"Benchmark variable '$Name' default '$Default' is not a boolean value.\" } } }; switch -Regex ([string] $value) { '^(?i:true|1|yes|on)$' { return $true } '^(?i:false|0|no|off)$' { return $false } default { throw \"Benchmark variable '$Name' value '$value' is not a boolean value.\" } } } $value = $BenchmarkVariables[$Name]; if ([string]::IsNullOrWhiteSpace([string] $value)) { if ($Required.IsPresent) { throw \"Benchmark variable '$Name' is required.\" }; return $Default }; [string] $value";
        const string InputIntBody = "param([Parameter(Position=0, Mandatory=$true)] [string] $Name, [Parameter(Position=1)] [int[]] $Default, [switch] $Required) $value = $BenchmarkVariables[$Name]; if ([string]::IsNullOrWhiteSpace([string] $value)) { if ($Required.IsPresent) { throw \"Benchmark variable '$Name' is required.\" }; return $Default }; $items = @(); foreach ($entry in ([string] $value -split ',')) { $trimmed = $entry.Trim(); if ($trimmed) { $items += [int] $trimmed } }; if ($items.Count -eq 0) { if ($Required.IsPresent) { throw \"Benchmark variable '$Name' did not contain any integer values.\" }; return $Default }; $items";
        const string InputBoolBody = "param([Parameter(Position=0, Mandatory=$true)] [string] $Name, [Parameter(Position=1)] [object] $Default = $false, [switch] $Required) $value = $BenchmarkVariables[$Name]; if ([string]::IsNullOrWhiteSpace([string] $value)) { if ($Required.IsPresent) { throw \"Benchmark variable '$Name' is required.\" }; switch -Regex ([string] $Default) { '^(?i:true|1|yes|on)$' { return $true } '^(?i:false|0|no|off)$' { return $false } default { throw \"Benchmark variable '$Name' default '$Default' is not a boolean value.\" } } }; switch -Regex ([string] $value) { '^(?i:true|1|yes|on)$' { return $true } '^(?i:false|0|no|off)$' { return $false } default { throw \"Benchmark variable '$Name' value '$value' is not a boolean value.\" } }";

        return new()
        {
            ["__PowerForgeBenchmarkDslInvoke"] = """
param([Parameter(Mandatory=$true)] [string] $Name, [object[]] $Arguments = @())
$runtimeType = $PowerForgeBenchmarkDslRuntimeType
if ($null -eq $runtimeType) { throw 'Benchmark DSL runtime type was not provided.' }
$flags = [System.Reflection.BindingFlags]'Public, Static'
$methods = @($runtimeType.GetMethods($flags) | Where-Object { $_.Name -eq $Name -and $_.GetParameters().Count -eq $Arguments.Count })
if ($methods.Count -ne 1) { throw "Benchmark DSL runtime method '$Name' with $($Arguments.Count) argument(s) was not found." }
try {
    $methods[0].Invoke($null, $Arguments)
} catch [System.Reflection.TargetInvocationException] {
    if ($_.Exception.InnerException) { throw $_.Exception.InnerException }
    throw
}
""",
            ["benchmark"] = Call("Benchmark", "param([Parameter(Position=0)] [string] $Name, [Alias('out')] [string] $OutputRoot, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $OutputRoot, $ScriptBlock)"),
            ["cases"] = Call("Cases", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["case"] = Call("Case", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [hashtable] $Values)", "[object[]] @($Name, $Values)"),
            ["caseSource"] = "param([Parameter(Position=0, ValueFromRemainingArguments=$true)] [object[]] $InputObject) if ($InputObject.Count -eq 1 -and $InputObject[0] -is [scriptblock]) { __PowerForgeBenchmarkDslInvoke -Name 'From' -Arguments ([object[]] @($InputObject[0])) } else { $arguments = [object[]]::new(1); $arguments[0] = $InputObject; __PowerForgeBenchmarkDslInvoke -Name 'FromValues' -Arguments $arguments }",
            ["from"] = "param([Parameter(Position=0, ValueFromRemainingArguments=$true)] [object[]] $InputObject) if ($InputObject.Count -eq 1 -and $InputObject[0] -is [scriptblock]) { __PowerForgeBenchmarkDslInvoke -Name 'From' -Arguments ([object[]] @($InputObject[0])) } else { $arguments = [object[]]::new(1); $arguments[0] = $InputObject; __PowerForgeBenchmarkDslInvoke -Name 'FromValues' -Arguments $arguments }",
            ["axis"] = "param([Parameter(Position=0)] [string] $Name, [Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) $arguments = [object[]]::new(2); $arguments[0] = $Name; $arguments[1] = $Values; __PowerForgeBenchmarkDslInvoke -Name 'Axis' -Arguments $arguments",
            ["setup"] = Call("Setup", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["data"] = Call("Data", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["skip"] = Call("Skip", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["validate"] = Call("Validate", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["policy"] = "param([int] $Warmup, [Alias('Iterations')] [int] $Iteration, [string] $RunMode, [object] $Order, [object] $MemoryCleanup, [int] $CooldownMilliseconds, [object] $OutlierMode) $w=$null; $i=$null; $c=$null; if ($PSBoundParameters.ContainsKey('Warmup')) { $w=$Warmup }; if ($PSBoundParameters.ContainsKey('Iteration')) { $i=$Iteration }; if ($PSBoundParameters.ContainsKey('CooldownMilliseconds')) { $c=$CooldownMilliseconds }; $arguments = [object[]]::new(7); $arguments[0] = $w; $arguments[1] = $i; $arguments[2] = $RunMode; $arguments[3] = [string] $Order; $arguments[4] = [string] $MemoryCleanup; $arguments[5] = $c; $arguments[6] = [string] $OutlierMode; __PowerForgeBenchmarkDslInvoke -Name 'Policy' -Arguments $arguments",
            ["profile"] = Call("Profile", "param([Parameter(Position=0)] [string] $Name, [string] $Cleanup)", "[object[]] @($Name, $Cleanup)"),
            ["cleanup"] = Call("Cleanup", "param([Parameter(Position=0)] [string] $Name)", "[object[]] @($Name)"),
            ["engine"] = Call("Engine", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $ScriptBlock)"),
            ["operation"] = Call("Operation", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $ScriptBlock)"),
            ["metric"] = Call("Metric", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $ScriptBlock)"),
            ["compare"] = "param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric, [ValidateRange(0, [double]::MaxValue)] [double] $TieTolerance, [switch] $RequireBaselineFastest) $arguments = [object[]]::new(5); $arguments[0] = $Dimension; $arguments[1] = $Baseline; $arguments[2] = $Metric; $arguments[3] = $TieTolerance; $arguments[4] = $RequireBaselineFastest.IsPresent; __PowerForgeBenchmarkDslInvoke -Name 'Compare' -Arguments $arguments",
            ["comparison"] = "param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric, [ValidateRange(0, [double]::MaxValue)] [double] $TieTolerance, [switch] $RequireBaselineFastest) $arguments = [object[]]::new(5); $arguments[0] = $Dimension; $arguments[1] = $Baseline; $arguments[2] = $Metric; $arguments[3] = $TieTolerance; $arguments[4] = $RequireBaselineFastest.IsPresent; __PowerForgeBenchmarkDslInvoke -Name 'Compare' -Arguments $arguments",
            ["readme"] = Call("Readme", "param([Parameter(Position=0)] [string] $Path, [string] $Block, [string] $Renderer)", "[object[]] @($Path, $Block, $Renderer)"),
            ["artifacts"] = "param([Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) $arguments = [object[]]::new(1); $arguments[0] = $Values; __PowerForgeBenchmarkDslInvoke -Name 'Artifacts' -Arguments $arguments",
            ["input"] = InputBody,
            ["inputInt"] = InputIntBody,
            ["inputBool"] = InputBoolBody,
            ["assertPath"] = AssertPathBody,
            ["assertValue"] = AssertValueBody,
            ["New-BenchmarkSuite"] = Call("Benchmark", "param([Parameter(Position=0)] [string] $Name, [Alias('out')] [string] $OutputRoot, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $OutputRoot, $ScriptBlock)"),
            ["Add-BenchmarkCases"] = Call("Cases", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["Add-BenchmarkCase"] = Call("Case", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [hashtable] $Values)", "[object[]] @($Name, $Values)"),
            ["Add-BenchmarkCaseSource"] = "param([Parameter(Position=0, ValueFromRemainingArguments=$true)] [object[]] $InputObject) if ($InputObject.Count -eq 1 -and $InputObject[0] -is [scriptblock]) { __PowerForgeBenchmarkDslInvoke -Name 'From' -Arguments ([object[]] @($InputObject[0])) } else { $arguments = [object[]]::new(1); $arguments[0] = $InputObject; __PowerForgeBenchmarkDslInvoke -Name 'FromValues' -Arguments $arguments }",
            ["Add-BenchmarkAxis"] = "param([Parameter(Position=0)] [string] $Name, [Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) $arguments = [object[]]::new(2); $arguments[0] = $Name; $arguments[1] = $Values; __PowerForgeBenchmarkDslInvoke -Name 'Axis' -Arguments $arguments",
            ["Set-BenchmarkSetup"] = Call("Setup", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["Set-BenchmarkDataFactory"] = Call("Data", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["Set-BenchmarkPolicy"] = "param([int] $Warmup, [Alias('Iterations')] [int] $Iteration, [string] $RunMode, [object] $Order, [object] $MemoryCleanup, [int] $CooldownMilliseconds, [object] $OutlierMode) $w=$null; $i=$null; $c=$null; if ($PSBoundParameters.ContainsKey('Warmup')) { $w=$Warmup }; if ($PSBoundParameters.ContainsKey('Iteration')) { $i=$Iteration }; if ($PSBoundParameters.ContainsKey('CooldownMilliseconds')) { $c=$CooldownMilliseconds }; $arguments = [object[]]::new(7); $arguments[0] = $w; $arguments[1] = $i; $arguments[2] = $RunMode; $arguments[3] = [string] $Order; $arguments[4] = [string] $MemoryCleanup; $arguments[5] = $c; $arguments[6] = [string] $OutlierMode; __PowerForgeBenchmarkDslInvoke -Name 'Policy' -Arguments $arguments",
            ["Set-BenchmarkProfile"] = Call("Profile", "param([Parameter(Position=0)] [string] $Name, [string] $Cleanup)", "[object[]] @($Name, $Cleanup)"),
            ["Set-BenchmarkCleanup"] = Call("Cleanup", "param([Parameter(Position=0)] [string] $Name)", "[object[]] @($Name)"),
            ["Add-BenchmarkEngine"] = Call("Engine", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $ScriptBlock)"),
            ["Add-BenchmarkOperation"] = Call("Operation", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $ScriptBlock)"),
            ["Add-BenchmarkSkipRule"] = Call("Skip", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["Add-BenchmarkValidation"] = Call("Validate", "param([Parameter(Position=0)] [scriptblock] $ScriptBlock)", "[object[]] @($ScriptBlock)"),
            ["Add-BenchmarkMetric"] = Call("Metric", "param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock)", "[object[]] @($Name, $ScriptBlock)"),
            ["Add-BenchmarkComparison"] = "param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric, [ValidateRange(0, [double]::MaxValue)] [double] $TieTolerance, [switch] $RequireBaselineFastest) $arguments = [object[]]::new(5); $arguments[0] = $Dimension; $arguments[1] = $Baseline; $arguments[2] = $Metric; $arguments[3] = $TieTolerance; $arguments[4] = $RequireBaselineFastest.IsPresent; __PowerForgeBenchmarkDslInvoke -Name 'Compare' -Arguments $arguments",
            ["Add-BenchmarkReadmeBlock"] = Call("Readme", "param([Parameter(Position=0)] [string] $Path, [string] $Block, [string] $Renderer)", "[object[]] @($Path, $Block, $Renderer)"),
            ["Set-BenchmarkArtifacts"] = "param([Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) $arguments = [object[]]::new(1); $arguments[0] = $Values; __PowerForgeBenchmarkDslInvoke -Name 'Artifacts' -Arguments $arguments",
            ["Get-BenchmarkInput"] = InputBody,
            ["Assert-BenchmarkPath"] = AssertPathBody,
            ["Assert-BenchmarkValue"] = AssertValueBody
        };
    }

    private static Hashtable CreateFunctions()
    {
        var functions = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in CreateFunctionBodies())
            functions[entry.Key] = ScriptBlock.Create(entry.Value);
        return functions;
    }

    private static PowerShellBenchmarkCase ConvertToCase(PSObject value)
    {
        var item = new PowerShellBenchmarkCase();
        var baseObject = value.BaseObject;
        if (baseObject is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                item.Values[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = entry.Value;
        }
        else
        {
            foreach (var property in value.Properties.Where(p => p.IsGettable))
                item.Values[property.Name] = property.Value;
        }

        var hasName = item.Values.TryGetValue("Name", out var name);
        var hasScenario = item.Values.TryGetValue("Scenario", out var scenario);
        var nameText = Convert.ToString(name, CultureInfo.InvariantCulture);
        var scenarioText = Convert.ToString(scenario, CultureInfo.InvariantCulture);
        item.Name = hasName && !string.IsNullOrWhiteSpace(nameText)
            ? nameText!
            : hasScenario && !string.IsNullOrWhiteSpace(scenarioText)
                ? scenarioText!
                : "Case";
        if (hasName)
            item.Values.Remove("Name");
        if (hasScenario)
            item.Values.Remove("Scenario");
        return item;
    }

    private static IEnumerable<object?> Flatten(IEnumerable<object?> values)
    {
        foreach (var value in values ?? Array.Empty<object?>())
        {
            var baseObject = value is PSObject psObject ? psObject.BaseObject : value;
            if (value is string || value is null || baseObject is IDictionary)
            {
                yield return value;
                continue;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    yield return item;
            }
            else
            {
                yield return value;
            }
        }
    }

    private static PowerShellBenchmarkDslContext RequireContext()
        => Current.Value ?? throw new InvalidOperationException("Benchmark DSL helpers can only be used while evaluating a benchmark spec.");

    private static void InvokeDslBlock(ScriptBlock scriptBlock)
        => InvokeWithScriptRoot(scriptBlock, CreateFunctions());

    private static IEnumerable<PSObject> InvokeRootBlock(ScriptBlock scriptBlock)
        => InvokeWithNativeExitCheck(PrepareRootScriptBlock(scriptBlock), CreateFunctions());

    private static IEnumerable<PSObject> InvokeWithScriptRoot(ScriptBlock scriptBlock, Hashtable? functionsToDefine)
        => InvokeWithNativeExitCheck(scriptBlock, functionsToDefine);

    private static Collection<PSObject> InvokeWithNativeExitCheck(ScriptBlock scriptBlock, Hashtable? functionsToDefine)
        => NativeExitAwareInvokeWrapper.InvokeWithContext(functionsToDefine, CreateInvocationVariables(), new object[] { scriptBlock, Array.Empty<object>(), true, typeof(PowerShellNativeExitCodeTracker) });

    private static readonly ScriptBlock NativeExitAwareInvokeWrapper =
        ScriptBlock.Create(EmbeddedScripts.Load("Scripts/Benchmarks/Invoke-NativeExitAwareBlock.ps1"));

    /// <summary>
    /// Captures a benchmark lifecycle block with local variables, helper functions, and script-root handling.
    /// </summary>
    /// <param name="scriptBlock">Benchmark block to close over.</param>
    /// <returns>A closed script block that can be executed later by the runner.</returns>
    public static ScriptBlock CloseScriptBlock(ScriptBlock scriptBlock)
    {
        if (scriptBlock is null) throw new ArgumentNullException(nameof(scriptBlock));
        var scriptRootLiteral = ToPowerShellLiteral(Current.Value?.ScriptRoot ?? string.Empty);
        var closer = ScriptBlock.Create(EmbeddedScripts
            .Load("Scripts/Benchmarks/Close-BenchmarkBlock.Template.ps1")
            .Replace("__POWERFORGE_SCRIPT_ROOT__", scriptRootLiteral));
        var output = NativeExitAwareInvokeWrapper.InvokeWithContext(
            functionsToDefine: null,
            variablesToDefine: CreateInvocationVariables(),
            new object[] { closer, new object[] { scriptBlock }, true, typeof(PowerShellNativeExitCodeTracker) });
        return output.FirstOrDefault()?.BaseObject as ScriptBlock
               ?? throw new InvalidOperationException("Benchmark block capture did not return a script block.");
    }

    private static ScriptBlock PrepareRootScriptBlock(ScriptBlock scriptBlock)
    {
        var source = CaptureScriptText(scriptBlock);
        if (scriptBlock.Module is not null)
            return scriptBlock.Module.NewBoundScriptBlock(ScriptBlock.Create(source));

        if (string.Equals(source, scriptBlock.ToString(), StringComparison.Ordinal))
            return scriptBlock;

        return ScriptBlock.Create(source);
    }

    private static string ReplaceScriptRootVariables(string script, string scriptRoot)
    {
        var ast = System.Management.Automation.Language.Parser.ParseInput(script, out _, out _);
        var expandableStrings = ast.FindAll(node => node is System.Management.Automation.Language.ExpandableStringExpressionAst, searchNestedScriptBlocks: true)
            .Select(node => node.Extent)
            .ToArray();
        var subExpressions = ast.FindAll(node => node is System.Management.Automation.Language.SubExpressionAst, searchNestedScriptBlocks: true)
            .Select(node => node.Extent)
            .ToArray();
        var replacements = ast.FindAll(node => node is System.Management.Automation.Language.VariableExpressionAst variable
                                             && IsPSScriptRootVariable(variable), searchNestedScriptBlocks: true)
            .Cast<System.Management.Automation.Language.VariableExpressionAst>()
            .OrderByDescending(variable => variable.Extent.StartOffset)
            .ToArray();
        if (replacements.Length == 0)
            return script;

        var rooted = script;
        foreach (var variable in replacements)
        {
            var literal = IsInsideAnyExtent(variable.Extent, subExpressions)
                ? ToPowerShellLiteral(scriptRoot)
                : IsInsideAnyExtent(variable.Extent, expandableStrings)
                ? ToPowerShellExpandableStringText(scriptRoot)
                : ToPowerShellLiteral(scriptRoot);
            rooted = rooted.Remove(variable.Extent.StartOffset, variable.Extent.EndOffset - variable.Extent.StartOffset)
                .Insert(variable.Extent.StartOffset, literal);
        }

        return rooted;
    }

    private static bool IsInsideAnyExtent(System.Management.Automation.Language.IScriptExtent extent, System.Management.Automation.Language.IScriptExtent[] containers)
        => containers.Any(container => extent.StartOffset >= container.StartOffset && extent.EndOffset <= container.EndOffset);

    private static bool IsPSScriptRootVariable(System.Management.Automation.Language.VariableExpressionAst variable)
        => string.Equals(variable.VariablePath.UserPath, "PSScriptRoot", StringComparison.OrdinalIgnoreCase)
           || string.Equals(variable.Extent.Text, "$PSScriptRoot", StringComparison.OrdinalIgnoreCase)
           || string.Equals(variable.Extent.Text, "${PSScriptRoot}", StringComparison.OrdinalIgnoreCase);

    private static string ToPowerShellLiteral(string value)
        => "'" + value.Replace("'", "''") + "'";

    private static string ToPowerShellExpandableStringText(string value)
        => value.Replace("`", "``").Replace("$", "`$").Replace("\"", "`\"");

    private static List<PSVariable> CreateInvocationVariables()
    {
        var variables = new List<PSVariable>
        {
            new("ErrorActionPreference", ActionPreference.Stop),
            new("PSNativeCommandUseErrorActionPreference", true),
            new("BenchmarkSpecEvaluation", true),
            new("BenchmarkVariables", RequireContext().BenchmarkVariables),
            new("BenchmarkVariable", RequireContext().BenchmarkVariables),
            new("BenchmarkCallerFunctions", RequireContext().CallerFunctions),
            new("PowerForgeBenchmarkDslRuntimeType", typeof(PowerShellBenchmarkDslRuntime))
        };
        return variables;
    }

    private static Hashtable CopyBenchmarkVariables(IReadOnlyDictionary<string, string?>? source)
    {
        var variables = new Hashtable(StringComparer.OrdinalIgnoreCase);
        if (source is null) return variables;
        foreach (var entry in source)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key))
                variables[entry.Key] = entry.Value;
        }

        return variables;
    }

    private static AliasSnapshot? RemoveAlias(string name)
    {
        using var getter = PowerShell.Create(RunspaceMode.CurrentRunspace);
        var alias = getter.AddCommand("Get-Alias")
            .AddArgument(name)
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke<AliasInfo>()
            .FirstOrDefault();
        if (alias is null) return null;

        using var remover = PowerShell.Create(RunspaceMode.CurrentRunspace);
        remover.AddCommand("Remove-Item")
            .AddArgument("Alias:" + name)
            .AddParameter("Force")
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke();
        return new AliasSnapshot(alias.Name, alias.Definition, alias.Options);
    }

    private static void RestoreAlias(AliasSnapshot? snapshot)
    {
        if (snapshot is null) return;
        using var setter = PowerShell.Create(RunspaceMode.CurrentRunspace);
        setter.AddCommand("Set-Alias")
            .AddParameter("Name", snapshot.Name)
            .AddParameter("Value", snapshot.Definition)
            .AddParameter("Option", snapshot.Options)
            .AddParameter("Force")
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke();
    }

    private static FunctionSnapshot? SetLegacyCompareFunction()
    {
        using var commandProbe = PowerShell.Create(RunspaceMode.CurrentRunspace);
        var comparisonCommand = commandProbe.AddCommand("Get-Command")
            .AddArgument("Add-BenchmarkComparison")
            .AddParameter("CommandType", CommandTypes.Cmdlet)
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke<CommandInfo>()
            .FirstOrDefault();
        if (comparisonCommand is null) return null;

        ScriptBlock? previous = null;
        var wasMissing = true;
        using (var getter = PowerShell.Create(RunspaceMode.CurrentRunspace))
        {
            previous = getter.AddCommand("Get-Item")
                .AddArgument("Function:compare")
                .AddParameter("ErrorAction", "SilentlyContinue")
                .Invoke<FunctionInfo>()
                .FirstOrDefault()
                ?.ScriptBlock;
            wasMissing = previous is null;
        }

        using var setter = PowerShell.Create(RunspaceMode.CurrentRunspace);
        setter.AddCommand("Set-Item")
            .AddArgument("Function:compare")
            .AddParameter("Value", ScriptBlock.Create("""
param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric, [ValidateRange(0, [double]::MaxValue)] [double] $TieTolerance, [switch] $RequireBaselineFastest)
$parameters = @{ Dimension = $Dimension; Baseline = $Baseline }
if ($PSBoundParameters.ContainsKey('Metric')) { $parameters['Metric'] = $Metric }
if ($PSBoundParameters.ContainsKey('TieTolerance')) { $parameters['TieTolerance'] = $TieTolerance }
if ($RequireBaselineFastest.IsPresent) { $parameters['RequireBaselineFastest'] = $true }
Add-BenchmarkComparison @parameters
"""))
            .AddParameter("Force")
            .AddParameter("ErrorAction", "Stop")
            .Invoke();
        return new FunctionSnapshot("compare", previous, wasMissing);
    }

    private static void RestoreFunction(FunctionSnapshot? snapshot)
    {
        if (snapshot is null) return;
        using var setter = PowerShell.Create(RunspaceMode.CurrentRunspace);
        if (snapshot.WasMissing)
        {
            setter.AddCommand("Remove-Item")
                .AddArgument("Function:" + snapshot.Name)
                .AddParameter("Force")
                .AddParameter("ErrorAction", "SilentlyContinue")
                .Invoke();
            return;
        }

        setter.AddCommand("Set-Item")
            .AddArgument("Function:" + snapshot.Name)
            .AddParameter("Value", snapshot.ScriptBlock)
            .AddParameter("Force")
            .AddParameter("ErrorAction", "SilentlyContinue")
            .Invoke();
    }

    private static PowerShellBenchmarkSuite RequireSuite()
    {
        var context = RequireContext();
        if (context.SuiteStack.Count == 0)
            throw new InvalidOperationException("This benchmark DSL command can only be used inside benchmark.");
        return context.SuiteStack.Peek();
    }

    private static string RequireName(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label} is required.");
        return value!.Trim();
    }

    private sealed class PowerShellBenchmarkDslContext
    {
        internal List<PowerShellBenchmarkSuite> Suites { get; } = new();
        internal Stack<PowerShellBenchmarkSuite> SuiteStack { get; } = new();
        internal Stack<PowerShellBenchmarkEngine> EngineStack { get; } = new();
        internal string? ScriptRoot { get; set; }
        internal Hashtable BenchmarkVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, string> CallerFunctions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AliasSnapshot
    {
        internal AliasSnapshot(string name, string definition, ScopedItemOptions options)
        {
            Name = name;
            Definition = definition;
            Options = options;
        }

        internal string Name { get; }
        internal string Definition { get; }
        internal ScopedItemOptions Options { get; }
    }

    private sealed class FunctionSnapshot
    {
        internal FunctionSnapshot(string name, ScriptBlock? scriptBlock, bool wasMissing)
        {
            Name = name;
            ScriptBlock = scriptBlock;
            WasMissing = wasMissing;
        }

        internal string Name { get; }
        internal ScriptBlock? ScriptBlock { get; }
        internal bool WasMissing { get; }
    }
}
