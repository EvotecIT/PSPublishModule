using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void InvokeBenchmarkSuiteCommand_ForeachMetricsRetainEachAuthoredNameInArtifacts()
    {
        RunMetricClosureSuite("""
$settings = {
    New-BenchmarkSuite 'loop-metrics' {
        Set-BenchmarkPolicy -Warmup 0 -Iterations 1
        Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { param($case, $run) } }
        Add-BenchmarkValidation { param($case, $run) $run.Values = @{ First = 11; Second = 22 } }
        foreach ($name in 'First', 'Second') {
            Add-BenchmarkMetric $name { param($case, $run) $run.Values[$name] }
        }
    }
}
""", new Dictionary<string, double> { ["First"] = 11, ["Second"] = 22 });
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_ExplicitMetricClosuresKeepBoundValuesAfterCallerChanges()
    {
        RunMetricClosureSuite("""
$name = 'First'
$first = { param($case, $run) $run.Values[$name] }.GetNewClosure()
$name = 'Second'
$second = { param($case, $run) $run.Values[$name] }.GetNewClosure()
$name = $null
$missing = { param($case, $run) [int] ($null -eq $name) }.GetNewClosure()
$name = 'Changed'
$settings = {
    New-BenchmarkSuite 'explicit-metrics' {
        Set-BenchmarkPolicy -Warmup 0 -Iterations 1
        Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { param($case, $run) } }
        Add-BenchmarkValidation { param($case, $run) $run.Values = @{ First = 11; Second = 22; Changed = 99 } }
        Add-BenchmarkMetric First $first
        Add-BenchmarkMetric Second $second
        Add-BenchmarkMetric NullBinding $missing
    }
}.GetNewClosure()
""", new Dictionary<string, double> { ["First"] = 11, ["Second"] = 22, ["NullBinding"] = 1 });
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_LifecycleClosuresUseNearestAuthoredScopeInsteadOfDslParameters()
    {
        RunMetricClosureSuite("""
$name = 'outer'
$Arguments = 'outer-arguments'
$ScriptBlock = 'outer-block'
$settings = {
    New-BenchmarkSuite 'lifecycle-scopes' {
        $name = 'suite'
        $Arguments = 'suite-arguments'
        $ScriptBlock = 'suite-block'
        Set-BenchmarkPolicy -Warmup 0 -Iterations 1
        Set-BenchmarkSetup {
            param($case, $run)
            if ($name -ne 'suite' -or $Arguments -ne 'suite-arguments' -or $ScriptBlock -ne 'suite-block') {
                throw 'Setup captured DSL parameters instead of the nearest authored scope.'
            }
            $run.SetupValue = 1
        }
        Add-BenchmarkEngine Managed {
            $name = 'engine'
            $Arguments = 'engine-arguments'
            $ScriptBlock = 'engine-block'
            Add-BenchmarkOperation Run {
                param($case, $run)
                if ($name -ne 'engine' -or $Arguments -ne 'engine-arguments' -or $ScriptBlock -ne 'engine-block') {
                    throw 'Operation captured DSL parameters instead of the nearest authored scope.'
                }
                $run.OperationValue = 2
            }
        }
        Add-BenchmarkValidation {
            param($case, $run)
            if ($name -ne 'suite' -or $Arguments -ne 'suite-arguments' -or $ScriptBlock -ne 'suite-block') {
                throw 'Validation captured DSL parameters instead of the nearest authored scope.'
            }
            $run.ValidatedValue = $run.SetupValue + $run.OperationValue
        }
        Add-BenchmarkMetric Scope {
            param($case, $run)
            if ($name -ne 'suite' -or $Arguments -ne 'suite-arguments' -or $ScriptBlock -ne 'suite-block') {
                throw 'Metric captured DSL parameters instead of the nearest authored scope.'
            }
            $run.ValidatedValue
        }
    }
}.GetNewClosure()
""", new Dictionary<string, double> { ["Scope"] = 3 });
    }

    [Theory]
    [InlineData("Setup", true)]
    [InlineData("Operation", true)]
    [InlineData("Validation", true)]
    [InlineData("Metrics", true)]
    [InlineData("Metrics", false)]
    public void InvokeBenchmarkSuiteCommand_ExplicitLifecycleClosuresUseSuiteHelpersWithoutChangingModuleFunctions(string stage, bool closedSettings)
    {
        RunMetricClosureSuite("""
function global:Get-Probe { 99 }
$callbacks = [System.Collections.Generic.List[scriptblock]]::new()
$stage = '__STAGE__'
$settings = {
    New-BenchmarkSuite 'explicit-helpers' {
        function Get-Probe { 11 }
        Set-BenchmarkPolicy -Warmup 0 -Iterations 1
        $callback = { param($case, $run) $run.Probe = Get-Probe; $run.Probe }.GetNewClosure()
        if ($stage -eq 'Metrics') {
            $null = $callback.Module.Invoke({ function script:Get-Probe { 77 } })
        }
        $callbacks.Add($callback)
        if ($stage -eq 'Setup') { Set-BenchmarkSetup $callback }
        Add-BenchmarkEngine Managed {
            if ($stage -eq 'Operation') { Add-BenchmarkOperation Run $callback }
            else { Add-BenchmarkOperation Run { param($case, $run) } }
        }
        if ($stage -eq 'Validation') { Add-BenchmarkValidation $callback }
        if ($stage -eq 'Metrics') { Add-BenchmarkMetric Probe $callback }
        else { Add-BenchmarkMetric Probe { param($case, $run) $run.Probe } }
    }
}__SETTINGS_CLOSURE__
""".Replace("__STAGE__", stage).Replace("__SETTINGS_CLOSURE__", closedSettings ? ".GetNewClosure()" : ""),
            new Dictionary<string, double> { ["Probe"] = 11 }, """
if ((Get-Probe) -ne 99) { throw 'The global helper was changed.' }
if ($stage -eq 'Metrics') {
    if ($callbacks[0].Module.Invoke({ Get-Probe })[0] -ne 77) { throw 'The closure module helper was changed.' }
}
""");
    }

    [Theory]
    [InlineData("Setup", false)]
    [InlineData("Operation", false)]
    [InlineData("Validation", false)]
    [InlineData("Metrics", false)]
    [InlineData("Setup", true)]
    [InlineData("Operation", true)]
    [InlineData("Validation", true)]
    [InlineData("Metrics", true)]
    public void InvokeBenchmarkSuiteCommand_ExplicitLifecycleClosuresCannotSuppressFailuresAndRestorePreferences(string stage, bool defaults)
    {
        RunMetricClosureSuite("""
$callbacks = [System.Collections.Generic.List[scriptblock]]::new()
$stage = '__STAGE__'
$defaults = __DEFAULTS__
$settings = {
    New-BenchmarkSuite 'explicit-preferences' {
        Set-BenchmarkPolicy -Warmup 0 -Iterations 1
        $ErrorActionPreference = if ($defaults) { 'Stop' } else { 'Continue' }
        $PSDefaultParameterValues = if ($defaults) { @{ 'Write-Error:ErrorAction' = 'SilentlyContinue' } } else { @{} }
        $PSNativeCommandUseErrorActionPreference = $false
        $callback = { param($case, $run) Write-Error 'must fail'; 11 }.GetNewClosure()
        $callbacks.Add($callback)
        if ($stage -eq 'Setup') { Set-BenchmarkSetup $callback }
        Add-BenchmarkEngine Managed {
            if ($stage -eq 'Operation') { Add-BenchmarkOperation Run $callback }
            else { Add-BenchmarkOperation Run { param($case, $run) } }
        }
        if ($stage -eq 'Validation') { Add-BenchmarkValidation $callback }
        if ($stage -eq 'Metrics') { Add-BenchmarkMetric Probe $callback }
        else { Add-BenchmarkMetric Probe { param($case, $run) 11 } }
    }
}.GetNewClosure()
""".Replace("__STAGE__", stage).Replace("__DEFAULTS__", defaults ? "$true" : "$false"),
            new Dictionary<string, double>(), """
$moduleVariables = $callbacks[0].Module.SessionState.PSVariable
$expectedPreference = if ($defaults) { 'Stop' } else { 'Continue' }
if ([string] $moduleVariables.GetValue('ErrorActionPreference') -ne $expectedPreference) { throw 'The closure error preference was changed.' }
if ($moduleVariables.GetValue('PSNativeCommandUseErrorActionPreference') -ne $false) { throw 'The closure native preference was changed.' }
$originalDefaults = $moduleVariables.GetValue('PSDefaultParameterValues')
if ($defaults) {
    if ($originalDefaults.Count -ne 1 -or $originalDefaults['Write-Error:ErrorAction'] -ne 'SilentlyContinue') {
        throw 'The closure default parameters were changed.'
    }
} elseif ($originalDefaults.Count -ne 0) { throw 'Default parameters leaked into the closure module.' }
""", stage);
    }

    [Fact]
    public void DslRuntime_ExplicitClosuresAndHelpersSurviveEvaluatingRunspaceDisposal()
    {
        var outputRoot = CreateTempRoot();
        var suite = Assert.Single(EvaluateBenchmarkDslWithoutImportedCommands(ScriptBlock.Create("""
New-BenchmarkSuite 'deferred-closures' {
    Set-BenchmarkPolicy -Warmup 0 -Iterations 1
    function Get-Probe { 11 }
    $offset = 5
    Add-BenchmarkEngine Managed {
        Add-BenchmarkOperation Run ({ param($case, $run) $run.Probe = (Get-Probe) + $offset }.GetNewClosure())
    }
    Add-BenchmarkMetric Probe ({ param($case, $run) $run.Probe }.GetNewClosure())
}
""")));
        suite.OutputRoot = outputRoot;
        try
        {
            var sample = Assert.Single(new PowerShellBenchmarkRunner().Run(suite).Samples);
            Assert.True(sample.Status == BenchmarkSampleStatus.Succeeded, sample.Reason);
            Assert.Equal(16, sample.Metrics["Probe"]);
        }
        finally { Directory.Delete(outputRoot, recursive: true); }
    }

    private static void RunMetricClosureSuite(string declarations, IReadOnlyDictionary<string, double> expected,
        string verification = "", string? expectedFailureStage = null)
    {
        var outputRoot = CreateTempRoot();
        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = PowerShell.Create(runspace);
        try
        {
            ps.AddScript("param($outputRoot)\n" + declarations
                + "\nInvoke-BenchmarkSuite -Settings $settings -OutputRoot $outputRoot\n" + verification).AddArgument(outputRoot);
            var output = ps.Invoke();
            var result = Assert.IsType<BenchmarkRunResult>(Assert.Single(output).BaseObject);
            var sample = Assert.Single(result.Samples);
            if (expectedFailureStage is not null)
            {
                Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
                Assert.Contains(expectedFailureStage + " failed", sample.Reason);
                Assert.Contains("must fail", sample.Reason);
                Assert.DoesNotContain(ps.Streams.Error, error => !error.ToString().Contains("must fail", StringComparison.Ordinal));
                var failedSamplesPath = Assert.Single(Directory.GetFiles(outputRoot, "samples.json", SearchOption.AllDirectories));
                Assert.Equal(BenchmarkSampleStatus.Failed, Assert.Single(BenchmarkJson.Read<BenchmarkSample[]>(failedSamplesPath)).Status);
                return;
            }
            Assert.Empty(ps.Streams.Error);
            Assert.True(sample.Status == BenchmarkSampleStatus.Succeeded, sample.Reason);
            AssertMetricClosureValues(expected, sample.Metrics);
            AssertMetricClosureValues(expected, Assert.Single(result.Summary).Metrics);

            var samplesPath = Assert.Single(Directory.GetFiles(outputRoot, "samples.json", SearchOption.AllDirectories));
            var summaryPath = Assert.Single(Directory.GetFiles(outputRoot, "summary.json", SearchOption.AllDirectories));
            AssertMetricClosureValues(expected, Assert.Single(BenchmarkJson.Read<BenchmarkSample[]>(samplesPath)).Metrics);
            AssertMetricClosureValues(expected, Assert.Single(BenchmarkJson.Read<BenchmarkSummaryRow[]>(summaryPath)).Metrics);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static void AssertMetricClosureValues(IReadOnlyDictionary<string, double> expected, IReadOnlyDictionary<string, double> actual)
    {
        foreach (var entry in expected)
        {
            Assert.True(actual.TryGetValue(entry.Key, out var value), $"Metric '{entry.Key}' was omitted.");
            Assert.Equal(entry.Value, value);
        }
    }
}
