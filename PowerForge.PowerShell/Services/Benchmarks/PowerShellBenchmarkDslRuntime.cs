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
    {
        if (scriptBlock is null) throw new ArgumentNullException(nameof(scriptBlock));
        var previous = Current.Value;
        var context = new PowerShellBenchmarkDslContext
        {
            ScriptRoot = scriptRoot
        };
        Current.Value = context;
        Runspace? createdRunspace = null;
        var previousRunspace = Runspace.DefaultRunspace;
        AliasSnapshot? compareAlias = null;
        try
        {
            if (Runspace.DefaultRunspace is null)
            {
                createdRunspace = RunspaceFactory.CreateRunspace();
                createdRunspace.Open();
                Runspace.DefaultRunspace = createdRunspace;
            }

            compareAlias = RemoveAlias("compare");
            InvokeRootBlock(scriptBlock, CreateFunctions());
            return context.Suites.ToArray();
        }
        finally
        {
            RestoreAlias(compareAlias);
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
        foreach (var value in InvokeStrict(scriptBlock))
        {
            var item = ConvertToCase(value);
            suite.Cases.Add(item);
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
        return string.IsNullOrWhiteSpace(scriptRoot)
            ? scriptText
            : ReplaceScriptRootVariables(scriptText, scriptRoot!);
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
    public static void Setup(ScriptBlock scriptBlock) => RequireSuite().Setup = CaptureScriptBlock(scriptBlock);

    /// <summary>
    /// Sets suite data block.
    /// </summary>
    /// <param name="scriptBlock">Data block.</param>
    public static void Data(ScriptBlock scriptBlock) => RequireSuite().Data = CaptureScriptBlock(scriptBlock);

    /// <summary>
    /// Sets suite skip block.
    /// </summary>
    /// <param name="scriptBlock">Skip block.</param>
    public static void Skip(ScriptBlock scriptBlock) => RequireSuite().Skip = CaptureScriptBlock(scriptBlock);

    /// <summary>
    /// Sets suite validation block.
    /// </summary>
    /// <param name="scriptBlock">Validation block.</param>
    public static void Validate(ScriptBlock scriptBlock) => RequireSuite().Validate = CaptureScriptBlock(scriptBlock);

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
        engine.Operations[operationName] = CaptureScriptBlock(scriptBlock);
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
            throw new InvalidOperationException($"Benchmark metric name '{metricName}' is reserved for primary timing statistics.");
        suite.Metrics.Add(new PowerShellBenchmarkMetric { Name = metricName, ScriptBlock = CaptureScriptBlock(scriptBlock) });
    }

    private static bool IsReservedMetricName(string name)
        => string.Equals(name, "MedianMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "MeanMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "MinMs", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "MaxMs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a comparison definition.
    /// </summary>
    /// <param name="dimension">Dimension name.</param>
    /// <param name="baseline">Baseline value.</param>
    /// <param name="metric">Metric names.</param>
    public static void Compare(string dimension, string baseline, string[]? metric)
        => RequireSuite().Comparisons.Add(new PowerShellBenchmarkComparison
        {
            Dimension = string.IsNullOrWhiteSpace(dimension) ? "Engine" : dimension.Trim(),
            Baseline = baseline ?? string.Empty,
            Metrics = metric is { Length: > 0 } ? metric : new[] { "MedianMs" }
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

    private static IEnumerable<PSObject> InvokeStrict(ScriptBlock scriptBlock)
        => InvokeWithScriptRoot(scriptBlock, functionsToDefine: null);

    private static Hashtable CreateFunctions()
    {
        var scriptRootLiteral = ToPowerShellLiteral(Current.Value?.ScriptRoot ?? string.Empty);
        var closeBenchmarkBlock = """
param([scriptblock] $ScriptBlock)
$scriptRoot = __POWERFORGE_SCRIPT_ROOT__
$captured = @{}
$capturedFunctions = @{}
$skipNames = @(
    'args', 'input', 'this', 'PSItem', '_', 'Error',
    'captured', 'capturedFunctions', 'scriptText', 'scriptRoot'
)
$skipFunctions = @(
    '__PowerForgeCloseBenchmarkBlock',
    'benchmark', 'cases', 'case', 'from', 'axis', 'setup', 'data', 'skip', 'validate', 'profile', 'cleanup', 'engine', 'operation', 'metric', 'compare', 'readme', 'artifacts',
    'New-BenchmarkSuite', 'Add-BenchmarkCases', 'Add-BenchmarkCase', 'Add-BenchmarkCaseSource', 'Add-BenchmarkAxis',
    'Set-BenchmarkSetup', 'Set-BenchmarkDataFactory', 'Set-BenchmarkProfile', 'Set-BenchmarkCleanup', 'Add-BenchmarkEngine', 'Add-BenchmarkOperation',
    'Add-BenchmarkSkipRule', 'Add-BenchmarkValidation', 'Add-BenchmarkMetric', 'Add-BenchmarkComparison',
    'Add-BenchmarkReadmeBlock', 'Set-BenchmarkArtifacts'
)
for ($scope = 2; $scope -lt 20; $scope++) {
    try {
        $variables = Get-Variable -Scope $scope -ErrorAction Stop
    } catch {
        break
    }

    foreach ($variable in $variables) {
        if ($skipNames -contains $variable.Name) { continue }
        if ($captured.ContainsKey($variable.Name)) { continue }
        if (($variable.Options -band [System.Management.Automation.ScopedItemOptions]::Constant) -or
            ($variable.Options -band [System.Management.Automation.ScopedItemOptions]::ReadOnly)) { continue }
        $captured[$variable.Name] = $variable.Value
    }
}

foreach ($function in Get-Command -CommandType Function -ErrorAction SilentlyContinue) {
    if ($skipFunctions -contains $function.Name) { continue }
    if ($function.Name -like '*:*') { continue }
    if (-not [string]::IsNullOrWhiteSpace($function.Source)) { continue }
    if (-not [string]::IsNullOrWhiteSpace($function.ModuleName)) { continue }
    if (($function.Options -band [System.Management.Automation.ScopedItemOptions]::Constant) -or
        ($function.Options -band [System.Management.Automation.ScopedItemOptions]::ReadOnly)) { continue }
    if ([string]::IsNullOrWhiteSpace($function.Definition)) { continue }
    if (-not $capturedFunctions.ContainsKey($function.Name)) {
        $capturedFunctions[$function.Name] = $function.Definition
    }
}

$scriptText = [PowerForge.PowerShellBenchmarkDslRuntime]::CaptureScriptText($ScriptBlock, $scriptRoot)
{
    $previousFunctions = @{}
    $missingFunctions = @{}
    try {
        foreach ($entry in $capturedFunctions.GetEnumerator()) {
            $functionPath = "Function:\$($entry.Key)"
            $existingFunction = Get-Item -Path $functionPath -ErrorAction SilentlyContinue
            if ($null -eq $existingFunction) {
                $missingFunctions[$entry.Key] = $true
            } else {
                $previousFunctions[$entry.Key] = $existingFunction.ScriptBlock
            }
            Set-Item -Path $functionPath -Value ([scriptblock]::Create([string] $entry.Value)) -ErrorAction Stop
        }
        foreach ($entry in $captured.GetEnumerator()) {
            Set-Variable -Name $entry.Key -Value $entry.Value -Scope Local
        }
        & ([scriptblock]::Create($scriptText)) @args
    }
    finally {
        foreach ($entry in $capturedFunctions.GetEnumerator()) {
            $functionPath = "Function:\$($entry.Key)"
            if ($previousFunctions.ContainsKey($entry.Key)) {
                Set-Item -Path $functionPath -Value $previousFunctions[$entry.Key] -ErrorAction SilentlyContinue
            } elseif ($missingFunctions.ContainsKey($entry.Key)) {
                Remove-Item -Path $functionPath -ErrorAction SilentlyContinue
            }
        }
    }
}.GetNewClosure()
""".Replace("__POWERFORGE_SCRIPT_ROOT__", scriptRootLiteral);

        return new()
        {
            ["__PowerForgeCloseBenchmarkBlock"] = ScriptBlock.Create(closeBenchmarkBlock),
            ["benchmark"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Alias('out')] [string] $OutputRoot, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Benchmark($Name, $OutputRoot, $ScriptBlock)"),
            ["cases"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Cases($ScriptBlock)"),
            ["case"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [hashtable] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Case($Name, $Values)"),
            ["from"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::From($ScriptBlock)"),
            ["axis"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Axis($Name, $Values)"),
            ["setup"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Setup((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["data"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Data((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["skip"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Skip((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["validate"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Validate((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["profile"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [string] $Cleanup) [PowerForge.PowerShellBenchmarkDslRuntime]::Profile($Name, $Cleanup)"),
            ["cleanup"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name) [PowerForge.PowerShellBenchmarkDslRuntime]::Cleanup($Name)"),
            ["engine"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Engine($Name, $ScriptBlock)"),
            ["operation"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Operation($Name, (__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["metric"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Metric($Name, (__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["compare"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric) [PowerForge.PowerShellBenchmarkDslRuntime]::Compare($Dimension, $Baseline, $Metric)"),
            ["readme"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Path, [string] $Block, [string] $Renderer) [PowerForge.PowerShellBenchmarkDslRuntime]::Readme($Path, $Block, $Renderer)"),
            ["artifacts"] = ScriptBlock.Create("param([Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Artifacts($Values)"),
            ["New-BenchmarkSuite"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Alias('out')] [string] $OutputRoot, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Benchmark($Name, $OutputRoot, $ScriptBlock)"),
            ["Add-BenchmarkCases"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Cases($ScriptBlock)"),
            ["Add-BenchmarkCase"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [hashtable] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Case($Name, $Values)"),
            ["Add-BenchmarkCaseSource"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::From($ScriptBlock)"),
            ["Add-BenchmarkAxis"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Axis($Name, $Values)"),
            ["Set-BenchmarkSetup"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Setup((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["Set-BenchmarkDataFactory"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Data((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["Set-BenchmarkProfile"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [string] $Cleanup) [PowerForge.PowerShellBenchmarkDslRuntime]::Profile($Name, $Cleanup)"),
            ["Set-BenchmarkCleanup"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name) [PowerForge.PowerShellBenchmarkDslRuntime]::Cleanup($Name)"),
            ["Add-BenchmarkEngine"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Engine($Name, $ScriptBlock)"),
            ["Add-BenchmarkOperation"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Operation($Name, (__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["Add-BenchmarkSkipRule"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Skip((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["Add-BenchmarkValidation"] = ScriptBlock.Create("param([Parameter(Position=0)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Validate((__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["Add-BenchmarkMetric"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Name, [Parameter(Position=1)] [scriptblock] $ScriptBlock) [PowerForge.PowerShellBenchmarkDslRuntime]::Metric($Name, (__PowerForgeCloseBenchmarkBlock $ScriptBlock))"),
            ["Add-BenchmarkComparison"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Dimension, [string] $Baseline, [string[]] $Metric) [PowerForge.PowerShellBenchmarkDslRuntime]::Compare($Dimension, $Baseline, $Metric)"),
            ["Add-BenchmarkReadmeBlock"] = ScriptBlock.Create("param([Parameter(Position=0)] [string] $Path, [string] $Block, [string] $Renderer) [PowerForge.PowerShellBenchmarkDslRuntime]::Readme($Path, $Block, $Renderer)"),
            ["Set-BenchmarkArtifacts"] = ScriptBlock.Create("param([Parameter(ValueFromRemainingArguments=$true)] [object[]] $Values) [PowerForge.PowerShellBenchmarkDslRuntime]::Artifacts($Values)")
        };
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

        item.Name = item.Values.TryGetValue("Name", out var name) || item.Values.TryGetValue("Scenario", out name)
            ? Convert.ToString(name, CultureInfo.InvariantCulture) ?? "Case"
            : "Case";
        return item;
    }

    private static IEnumerable<object?> Flatten(IEnumerable<object?> values)
    {
        foreach (var value in values ?? Array.Empty<object?>())
        {
            if (value is string || value is null)
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

    private static IEnumerable<PSObject> InvokeRootBlock(ScriptBlock scriptBlock, Hashtable functionsToDefine)
    {
        var block = PrepareRootScriptBlock(scriptBlock);
        return InvokeWithNativeExitCheck(block, functionsToDefine);
    }

    private static IEnumerable<PSObject> InvokeWithScriptRoot(ScriptBlock scriptBlock, Hashtable? functionsToDefine)
        => InvokeWithNativeExitCheck(scriptBlock, functionsToDefine);

    private static Collection<PSObject> InvokeWithNativeExitCheck(ScriptBlock scriptBlock, Hashtable? functionsToDefine)
        => NativeExitAwareInvokeWrapper.InvokeWithContext(functionsToDefine, CreateInvocationVariables(), new object[] { scriptBlock });

    private static readonly ScriptBlock NativeExitAwareInvokeWrapper = ScriptBlock.Create("""
param([scriptblock] $Block)
$previousLastExitCode = $global:LASTEXITCODE
$global:LASTEXITCODE = 0
try {
    & $Block
    $nativeExitCode = $global:LASTEXITCODE
    if ($null -ne $nativeExitCode -and $nativeExitCode -ne 0) {
        throw "Native command exited with code $nativeExitCode."
    }
}
finally {
    if ($null -eq $previousLastExitCode) {
        Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    } else {
        $global:LASTEXITCODE = $previousLastExitCode
    }
}
""");

    private static ScriptBlock CaptureScriptBlock(ScriptBlock scriptBlock)
        => scriptBlock;

    private static ScriptBlock PrepareRootScriptBlock(ScriptBlock scriptBlock)
    {
        var source = CaptureScriptText(scriptBlock);
        return string.Equals(source, scriptBlock.ToString(), StringComparison.Ordinal)
            ? scriptBlock
            : ScriptBlock.Create(source);
    }

    private static string ReplaceScriptRootVariables(string script, string scriptRoot)
    {
        var ast = System.Management.Automation.Language.Parser.ParseInput(script, out _, out _);
        var expandableStrings = ast.FindAll(node => node is System.Management.Automation.Language.ExpandableStringExpressionAst, searchNestedScriptBlocks: true)
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
            var literal = IsInsideAnyExtent(variable.Extent, expandableStrings)
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
            new("PSNativeCommandUseErrorActionPreference", true)
        };
        var scriptRoot = Current.Value?.ScriptRoot;
        if (!string.IsNullOrWhiteSpace(scriptRoot))
            variables.Add(new PSVariable("PSScriptRoot", scriptRoot));
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
}
