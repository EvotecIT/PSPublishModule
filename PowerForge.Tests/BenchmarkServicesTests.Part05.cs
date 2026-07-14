using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void DslRuntime_PreservesSpecLocalHelperFunctionsInCapturedBlocks()
    {
        var root = CreateTempRoot();
        var fixture = Path.Combine(root, "fixture.txt");
        File.WriteAllText(fixture, "ok");
        var script = ScriptBlock.Create(@"
function Read-FixtureText {
    param([string] $Path)
    Get-Content -LiteralPath $Path -Raw
}
$fixture = Join-Path $PSScriptRoot 'fixture.txt'
benchmark 'helpers' {
    axis Operation Run
    axis Engine Managed
    setup { param($case, $run) $run.FixtureText = Read-FixtureText -Path $fixture }
    engine Managed { operation Run { param($case, $run) if ($run.FixtureText -ne 'ok') { throw 'helper missing' } } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_RestoresCapturedHelperFunctionsAfterUse()
    {
        var previousRunspace = System.Management.Automation.Runspaces.Runspace.DefaultRunspace;
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace();
        runspace.Open();
        System.Management.Automation.Runspaces.Runspace.DefaultRunspace = runspace;
        try
        {
            using (var ps = System.Management.Automation.PowerShell.Create(runspace))
            {
                ps.AddScript("function Get-PowerForgeBenchmarkProbe { 'original' }");
                ps.Invoke();
            }

            var script = ScriptBlock.Create(@"
function Get-PowerForgeBenchmarkProbe { 'captured' }
benchmark 'helpers' {
    axis Operation Run
    axis Engine Managed
    setup { param($case, $run) $run.Probe = Get-PowerForgeBenchmarkProbe }
    engine Managed { operation Run { param($case, $run) if ($run.Probe -ne 'captured') { throw 'helper missing' } } }
}
");

            var suite = Assert.Single(EvaluateBenchmarkDsl(script));
            suite.WarmupCount = 0;
            suite.IterationCount = 1;
            var result = new PowerShellBenchmarkRunner().Run(suite);

            using var verify = System.Management.Automation.PowerShell.Create(runspace);
            verify.AddScript("Get-PowerForgeBenchmarkProbe");
            var restored = Assert.Single(verify.Invoke()).BaseObject;
            Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
            Assert.Equal("original", restored);
        }
        finally
        {
            System.Management.Automation.Runspaces.Runspace.DefaultRunspace = previousRunspace;
        }
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateEngineNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
    engine managed { operation Other { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("already defines engine", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateAxisNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup-axis' {
    axis Rows 1
    axis rows 2
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("already defines axis", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateOperationNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup-operation' {
    axis Operation Run
    axis Engine Managed
    engine Managed {
        operation Run { param($case, $run) }
        operation run { param($case, $run) }
    }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("already defines operation", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsDuplicateMetricNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'dup-metric' {
    axis Operation Run
    axis Engine Managed
    metric RowsPerSecond { 1 }
    metric rowspersecond { 2 }
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("already defines metric", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsReservedPrimaryMetricNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'reserved-metric' {
    axis Operation Run
    axis Engine Managed
    metric MedianMs { 123 }
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("reserved", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MedianMs", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_RejectsReservedArtifactMetricNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'reserved-artifact-metric' {
    axis Operation Run
    axis Engine Managed
    metric Status { 'custom' }
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("reserved", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Status", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("P95")]
    [InlineData("P99")]
    [InlineData("StdDev")]
    [InlineData("StdErr")]
    public void DslRuntime_RejectsReservedTimingMetricAliases(string metricName)
    {
        var script = ScriptBlock.Create($$"""
benchmark 'reserved-timing-alias' {
    axis Operation Run
    axis Engine Managed
    metric {{metricName}} { 123 }
    engine Managed { operation Run { param($case, $run) } }
}
""");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("reserved", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(metricName, ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_ParsesTemporaryLocalUserProfileAndCleanup()
    {
        var script = ScriptBlock.Create(@"
benchmark 'temp-user' {
    profile TemporaryLocalUser -Cleanup KeepOnFailure
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));

        Assert.Equal(PowerShellBenchmarkProfileKind.TemporaryLocalUser, suite.Profile);
        Assert.Equal(PowerShellBenchmarkCleanupMode.KeepOnFailure, suite.Cleanup);
    }

    [Fact]
    public void DslRuntime_ParsesBenchmarkPolicy()
    {
        var script = ScriptBlock.Create(@"
benchmark 'policy-suite' {
    policy -Warmup 2 -Iterations 5 -RunMode publish -Order Sequential -CooldownMilliseconds 10 -OutlierMode ExcludeMinMax
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));

        Assert.Equal(2, suite.WarmupCount);
        Assert.Equal(5, suite.IterationCount);
        Assert.Equal("publish", suite.RunMode);
        Assert.Equal(PowerShellBenchmarkRunOrder.Sequential, suite.RunOrder);
        Assert.Equal(10, suite.CooldownMilliseconds);
        Assert.Equal(PowerShellBenchmarkOutlierMode.ExcludeMinMax, suite.OutlierMode);
    }

    [Fact]
    public void DslRuntime_ExposesBenchmarkVariablesToSpecs()
    {
        var script = ScriptBlock.Create(@"
$caseName = input CaseName DefaultCase
$rows = inputInt Rows 1, 2
$keep = inputBool KeepTables
benchmark 'variables' {
    caseSource { [pscustomobject]@{ Name = $caseName; Rows = ($rows -join ','); KeepTables = $keep } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CaseName"] = "FromVariable",
            ["Rows"] = "42"
        };

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, scriptRoot: null, benchmarkVariables: variables));
        var item = Assert.Single(new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Equal("FromVariable", item.Scenario);
        Assert.Equal("42", item.Values["Rows"]);
        Assert.Equal(false, item.Values["KeepTables"]);
    }

    [Fact]
    public void DslRuntime_BenchmarkInputHelpersUseDefaultsAndRejectMissingRequiredValues()
    {
        var script = ScriptBlock.Create(@"
$caseName = input CaseName DefaultCase
$rows = inputInt Rows 5, 10
$enabled = inputBool Enabled $true
benchmark 'defaults' {
    caseSource { [pscustomobject]@{ Name = $caseName; Rows = ($rows -join ','); Enabled = $enabled } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var item = Assert.Single(new PowerShellBenchmarkRunner().Plan(Assert.Single(EvaluateBenchmarkDsl(script))));

        Assert.Equal("DefaultCase", item.Scenario);
        Assert.Equal("5,10", item.Values["Rows"]);
        Assert.Equal(true, item.Values["Enabled"]);

        var required = ScriptBlock.Create(@"
$caseName = input CaseName -Required
benchmark 'required' {
    caseSource { [pscustomobject]@{ Name = $caseName } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(required));

        Assert.Contains("CaseName", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_GetBenchmarkInputSupportsTypedParameterSets()
    {
        var script = ScriptBlock.Create(@"
$caseName = Get-BenchmarkInput CaseName DefaultCase
$rows = Get-BenchmarkInput Rows @(5, 10) -Int
$enabled = Get-BenchmarkInput Enabled $true -Bool
benchmark 'typed-inputs' {
    caseSource { [pscustomobject]@{ Name = $caseName; Rows = ($rows -join ','); Enabled = $enabled } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CaseName"] = "FromVariable",
            ["Rows"] = "42,84",
            ["Enabled"] = "off"
        };

        var item = Assert.Single(new PowerShellBenchmarkRunner().Plan(Assert.Single(EvaluateBenchmarkDsl(script, benchmarkVariables: variables))));

        Assert.Equal("FromVariable", item.Scenario);
        Assert.Equal("42,84", item.Values["Rows"]);
        Assert.Equal(false, item.Values["Enabled"]);
    }

    [Fact]
    public void DslRuntime_BenchmarkBoolInputsParseStringDefaults()
    {
        var script = ScriptBlock.Create(@"
$shortFalse = inputBool ShortFalse false
$shortOff = inputBool ShortOff off
$cmdletFalse = Get-BenchmarkInput CmdletFalse false -Bool
$cmdletOn = Get-BenchmarkInput CmdletOn on -Bool
benchmark 'bool-defaults' {
    caseSource { [pscustomobject]@{ Name = 'Default'; ShortFalse = $shortFalse; ShortOff = $shortOff; CmdletFalse = $cmdletFalse; CmdletOn = $cmdletOn } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var item = Assert.Single(new PowerShellBenchmarkRunner().Plan(Assert.Single(EvaluateBenchmarkDsl(script))));

        Assert.Equal(false, item.Values["ShortFalse"]);
        Assert.Equal(false, item.Values["ShortOff"]);
        Assert.Equal(false, item.Values["CmdletFalse"]);
        Assert.Equal(true, item.Values["CmdletOn"]);
    }

    [Fact]
    public void DslRuntime_RejectsUnsupportedProfileNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'bad-profile' {
    profile LocalAdmin
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("LocalAdmin", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profile", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DslRuntime_PreservesNearestCapturedScopeAndUserPathVariables()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "outer.txt"), "outer");
        File.WriteAllText(Path.Combine(root, "inner.txt"), "inner");
        var script = ScriptBlock.Create(@"
$Path = Join-Path $PSScriptRoot 'outer.txt'
$fixture = 'outer'
benchmark 'closure' {
    $Path = Join-Path $PSScriptRoot 'inner.txt'
    $fixture = 'inner'
    axis Operation Run
    axis Engine Managed
    setup {
        param($case, $run)
        $run.FixtureText = Get-Content -LiteralPath $Path -Raw
        $run.FixtureName = $fixture
    }
    engine Managed {
        operation Run {
            param($case, $run)
            if ($run.FixtureText -ne 'inner' -or $run.FixtureName -ne 'inner') {
                throw 'wrong captured scope'
            }
        }
    }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_RewritesScriptRootInCapturedHelperFunctions()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "helper.txt"), "from-helper");
        var script = ScriptBlock.Create(@"
function Get-BenchmarkFixturePath {
    Join-Path $PSScriptRoot 'helper.txt'
}

benchmark 'helper-root' {
    axis Operation Run
    axis Engine Managed
    setup {
        param($case, $run)
        $run.FixtureText = Get-Content -LiteralPath (Get-BenchmarkFixturePath) -Raw
    }
    engine Managed {
        operation Run {
            param($case, $run)
            if ($run.FixtureText -ne 'from-helper') {
                throw 'helper root was not rewritten'
            }
        }
    }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script, root));
        suite.WarmupCount = 0;
        suite.IterationCount = 1;
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
    }

    [Fact]
    public void DslRuntime_HandlesOrderedDictionaryCaseSources()
    {
        var script = ScriptBlock.Create(@"
benchmark 'ordered' {
    cases { Add-BenchmarkCaseSource { [ordered]@{ Name = 'A'; Rows = 10 } } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));
        var benchmarkCase = Assert.Single(suite.Cases);

        Assert.Equal("A", benchmarkCase.Name);
        Assert.Equal(10, benchmarkCase.Values["Rows"]);
        Assert.DoesNotContain("Name", benchmarkCase.Values.Keys);
    }

    [Fact]
    public void DslRuntime_HandlesDirectHashtableCaseSourceAsSingleCase()
    {
        var script = ScriptBlock.Create(@"
benchmark 'hashtable-source' {
    caseSource @{ Name = 'Small'; Rows = 1 }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDslWithoutImportedCommands(script));
        var benchmarkCase = Assert.Single(suite.Cases);

        Assert.Equal("Small", benchmarkCase.Name);
        Assert.Equal(1, benchmarkCase.Values["Rows"]);
        Assert.DoesNotContain("Name", benchmarkCase.Values.Keys);
    }

    [Fact]
    public void DslRuntime_EvaluatesShortDslWithoutImportedCmdlets()
    {
        var script = ScriptBlock.Create(@"
benchmark 'self-contained' {
    policy -Warmup 0 -Iterations 1 -Order Sequential
    caseSource @{ Name = 'Default'; Rows = 1 }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
    compare Engine -Baseline Managed
}
");

        var suite = Assert.Single(EvaluateBenchmarkDslWithoutImportedCommands(script));

        Assert.Equal(0, suite.WarmupCount);
        Assert.Equal(1, suite.IterationCount);
        Assert.Equal(PowerShellBenchmarkRunOrder.Sequential, suite.RunOrder);
        Assert.Equal("Default", Assert.Single(suite.Cases).Name);
        Assert.Equal("Managed", Assert.Single(suite.Comparisons).Baseline);
    }

    [Fact]
    public void DslRuntime_PrefersRuntimeDslHelpersOverAliases()
    {
        var previousRunspace = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        Runspace.DefaultRunspace = runspace;
        try
        {
            using (var setup = PowerShell.Create(runspace))
            {
                setup.AddScript("""
function New-BenchmarkSuite { throw 'benchmark alias was used' }
function Add-BenchmarkAxis { throw 'axis alias was used' }
function Add-BenchmarkEngine { throw 'engine alias was used' }
Set-Alias -Name benchmark -Value New-BenchmarkSuite
Set-Alias -Name axis -Value Add-BenchmarkAxis
Set-Alias -Name engine -Value Add-BenchmarkEngine
""");
                setup.Invoke();
                Assert.Empty(setup.Streams.Error);
            }

            var script = ScriptBlock.Create("""
    benchmark 'alias-shadow' {
        policy -Warmup 0 -Iterations 1
        caseSource @{ Name = 'Default' }
        axis Operation Run
        axis Engine Managed
        engine Managed { operation Run { param($case, $run) } }
        compare Engine -Baseline Managed
    }
""");

            var suite = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(script));
            var item = Assert.Single(new PowerShellBenchmarkRunner().Plan(suite));

            Assert.Equal("Default", item.Scenario);

            using var verify = PowerShell.Create(runspace);
            verify.AddCommand("Get-Alias").AddArgument("benchmark");
            var benchmarkAlias = Assert.IsType<AliasInfo>(Assert.Single(verify.Invoke()).BaseObject);
            Assert.Equal("New-BenchmarkSuite", benchmarkAlias.Definition);

            verify.Commands.Clear();
            verify.AddCommand("Get-Alias").AddArgument("engine");
            var engineAlias = Assert.IsType<AliasInfo>(Assert.Single(verify.Invoke()).BaseObject);
            Assert.Equal("Add-BenchmarkEngine", engineAlias.Definition);
        }
        finally
        {
            Runspace.DefaultRunspace = previousRunspace;
        }
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_RefreshesCapturedHelperFunctionsBetweenSuites()
    {
        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = PowerShell.Create(runspace);
        ps.AddScript("""
Invoke-BenchmarkSuite -Settings {
    function Get-Probe { 'first' }
    benchmark 'first' {
        policy -Warmup 0 -Iterations 1
        caseSource @{ Name = 'Default' }
        axis Operation Run
        axis Engine Managed
        setup { param($case, $run) $run.Probe = Get-Probe }
        engine Managed { operation Run { param($case, $run) if ($run.Probe -ne 'first') { throw "expected first, got $($run.Probe)" } } }
    }

    function Get-Probe { 'second' }
    benchmark 'second' {
        policy -Warmup 0 -Iterations 1
        caseSource @{ Name = 'Default' }
        axis Operation Run
        axis Engine Managed
        setup { param($case, $run) $run.Probe = Get-Probe }
        engine Managed { operation Run { param($case, $run) if ($run.Probe -ne 'second') { throw "expected second, got $($run.Probe)" } } }
    }
}
""");

        var output = ps.Invoke();

        Assert.Empty(ps.Streams.Error);
        Assert.Equal(2, output.Count);
        Assert.All(output, item =>
        {
            var result = Assert.IsType<BenchmarkRunResult>(item.BaseObject);
            Assert.Equal(BenchmarkSampleStatus.Succeeded, Assert.Single(result.Samples).Status);
        });
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_PrefersRuntimeAssertionsOverImportedAliases()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "expected.txt"), "ok");
        var escapedRoot = root.Replace("'", "''");
        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = PowerShell.Create(runspace);
        ps.AddScript($$"""
Invoke-BenchmarkSuite -Settings {
    benchmark 'assert-alias' -out '{{escapedRoot}}' {
        policy -Warmup 0 -Iterations 1
        caseSource @{ Name = 'Default' }
        axis Operation Run
        axis Engine Managed
        engine Managed { operation Run { param($case, $run) } }
        validate { param($case, $run) assertPath (Join-Path '{{escapedRoot}}' 'expected.txt') }
    }
}
""");

        var output = ps.Invoke();

        Assert.Empty(ps.Streams.Error);
        var result = Assert.IsType<BenchmarkRunResult>(Assert.Single(output).BaseObject);
        Assert.All(result.Samples, sample => Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status));

        ps.Commands.Clear();
        ps.AddCommand("Get-Alias").AddArgument("assertPath");
        var assertPathAlias = Assert.IsType<AliasInfo>(Assert.Single(ps.Invoke()).BaseObject);
        Assert.Equal("Assert-BenchmarkPath", assertPathAlias.Definition);
    }

    [Fact]
    public void DslRuntime_CapturesAssertionHelpersWithoutImportedCmdlets()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "expected.txt"), "ok");
        var escapedRoot = root.Replace("'", "''");
        var script = ScriptBlock.Create($$"""
benchmark 'runtime-assert' -out '{{escapedRoot}}' {
    policy -Warmup 0 -Iterations 1
    caseSource @{ Name = 'Default' }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
    validate { param($case, $run) assertPath (Join-Path '{{escapedRoot}}' 'expected.txt') }
}
""");

        var suite = Assert.Single(EvaluateBenchmarkDslWithoutImportedCommands(script));
        var result = new PowerShellBenchmarkRunner().Run(suite);

        Assert.All(result.Samples, sample => Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status));
    }

    [Fact]
    public void AddBenchmarkComparisonCommand_ExportsOnlyImportSafeAlias()
    {
        var alias = typeof(PSPublishModule.AddBenchmarkComparisonCommand)
            .GetCustomAttributes(inherit: false)
            .OfType<AliasAttribute>()
            .Single();

        Assert.Contains("comparison", alias.AliasNames);
        Assert.DoesNotContain("compare", alias.AliasNames);
    }

    [Fact]
    public void AddBenchmarkComparisonCommand_DefaultsDimensionToEngine()
    {
        var script = ScriptBlock.Create(@"
benchmark 'comparison-default' {
    Add-BenchmarkComparison -Baseline 'Managed'
}
");

        var comparison = Assert.Single(Assert.Single(EvaluateBenchmarkDsl(script)).Comparisons);

        Assert.Equal("Engine", comparison.Dimension);
        Assert.Equal("Managed", comparison.Baseline);
        Assert.Equal(0, comparison.TieTolerance);
    }

    [Fact]
    public void DslRuntime_StripsGeneratedScenarioCaseMetadata()
    {
        var script = ScriptBlock.Create(@"
benchmark 'scenario-case' {
    cases { Add-BenchmarkCaseSource { [pscustomobject]@{ Scenario = 'Generated'; Rows = 20 } } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));
        var benchmarkCase = Assert.Single(suite.Cases);

        Assert.Equal("Generated", benchmarkCase.Name);
        Assert.Equal(20, benchmarkCase.Values["Rows"]);
        Assert.DoesNotContain("Scenario", benchmarkCase.Values.Keys);
    }

    [Fact]
    public void DslRuntime_HonorsArtifactsNone()
    {
        var script = ScriptBlock.Create(@"
benchmark 'none' {
    artifacts None
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var suite = Assert.Single(EvaluateBenchmarkDsl(script));

        Assert.Equal(BenchmarkArtifactKind.None, suite.Artifacts);
    }

    [Fact]
    public void DslRuntime_RejectsUnknownArtifactNames()
    {
        var script = ScriptBlock.Create(@"
benchmark 'bad' {
    artifacts Json, MarkDownn
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");

        var ex = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDsl(script));

        Assert.Contains("MarkDownn", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_ExposesSuiteOverrideParameter()
    {
        var property = typeof(PSPublishModule.InvokeBenchmarkSuiteCommand).GetProperty("Suite");

        Assert.NotNull(property);
        Assert.Contains(property!.GetCustomAttributes(inherit: true), attribute => attribute is ParameterAttribute);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_ExposesBenchmarkVariableParameter()
    {
        var property = typeof(PSPublishModule.InvokeBenchmarkSuiteCommand).GetProperty("Variable");

        Assert.NotNull(property);
        Assert.Contains(property!.GetCustomAttributes(inherit: true), attribute => attribute is ParameterAttribute);
    }

    private static PowerShellBenchmarkSuite[] EvaluateBenchmarkDslWithoutImportedCommands(ScriptBlock scriptBlock)
    {
        var previousRunspace = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        Runspace.DefaultRunspace = runspace;
        try
        {
            return PowerShellBenchmarkDslRuntime.Evaluate(scriptBlock);
        }
        finally
        {
            Runspace.DefaultRunspace = previousRunspace;
        }
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_PassesBenchmarkVariablesToFileBackedSpec()
    {
        var root = CreateTempRoot();
        var spec = Path.Combine(root, "variables.benchmark.ps1");
        File.WriteAllText(spec, @"
$caseName = input CaseName
$rows = inputInt Rows
benchmark 'variables' {
    caseSource { [pscustomobject]@{ Name = $caseName; Rows = ($rows -join ',') } }
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");
        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = PowerShell.Create(runspace);
        ps.AddCommand("Invoke-BenchmarkSuite")
            .AddParameter("Path", spec)
            .AddParameter("Variable", new System.Collections.Hashtable
            {
                ["CaseName"] = "FromVariable",
                ["Rows"] = new[] { "10", "20" }
            })
            .AddParameter("Plan");

        var output = ps.Invoke();

        Assert.Empty(ps.Streams.Error);
        var item = Assert.IsType<PowerShellBenchmarkWorkItem>(Assert.Single(output).BaseObject);
        Assert.Equal("FromVariable", item.Scenario);
        Assert.Equal("10,20", item.Values["Rows"]);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_ResolvesRelativeOutputRootFromPowerShellLocation()
    {
        var processRoot = CreateTempRoot();
        var shellRoot = CreateTempRoot();
        var previousCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = processRoot;
            var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
            initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
            using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
            runspace.Open();
            ImportBenchmarkDslCommands(runspace);
            using var ps = System.Management.Automation.PowerShell.Create(runspace);
            ps.AddCommand("Set-Location").AddParameter("Path", shellRoot);
            ps.Invoke();
            ps.Commands.Clear();

            var settings = ScriptBlock.Create(@"
benchmark 'path' -out 'relative-out' {
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");
            ps.AddCommand("Invoke-BenchmarkSuite")
                .AddParameter("Settings", settings)
                .AddParameter("WarmupCount", 0)
                .AddParameter("IterationCount", 1);

            var result = Assert.IsType<BenchmarkRunResult>(Assert.Single(ps.Invoke()).BaseObject);

            Assert.StartsWith(Path.Combine(shellRoot, "relative-out"), result.Artifacts["run-report.json"], StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(processRoot, "relative-out")));
        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;
        }
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_PreservesInlineSettingsClosureWhenScriptRootIsUsed()
    {
        var shellRoot = CreateTempRoot();
        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = PowerShell.Create(runspace);
        ps.AddCommand("Set-Location").AddParameter("Path", shellRoot);
        ps.Invoke();
        ps.Commands.Clear();
        ps.AddScript("""
$moduleName = 'FromClosure'
Invoke-BenchmarkSuite -Settings {
    $rootProbe = $PSScriptRoot
    benchmark 'inline-closure' {
        cases { case A @{ ModuleName = $moduleName; Root = $PSScriptRoot } }
        axis Operation Run
        axis Engine Managed
        engine Managed { operation Run { param($case, $run) } }
    }
} -Plan
""");

        var plan = ps.Invoke();

        Assert.Empty(ps.Streams.Error);
        var item = Assert.IsType<PowerShellBenchmarkWorkItem>(Assert.Single(plan).BaseObject);
        Assert.Equal("FromClosure", item.Values["ModuleName"]);
        Assert.Equal(shellRoot, item.Values["Root"]);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_WhatIfSkipsReadmeRewrite()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        File.WriteAllText(readme, "<!-- BENCHMARK:results:START -->\nold\n<!-- BENCHMARK:results:END -->\n");
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = System.Management.Automation.PowerShell.Create(runspace);
        var escapedRoot = root.Replace("'", "''");
        var settings = ScriptBlock.Create($@"
benchmark 'whatif' -out '{escapedRoot}' {{
    axis Operation Run
    axis Engine Managed
    readme '{readme.Replace("'", "''")}' -block results -renderer SummaryTable
    engine Managed {{ operation Run {{ param($case, $run) }} }}
}}
");
        ps.AddCommand("Invoke-BenchmarkSuite")
            .AddParameter("Settings", settings)
            .AddParameter("WhatIf");

        var output = ps.Invoke();

        Assert.Empty(output);
        Assert.Empty(ps.Streams.Error);
        Assert.Contains("old", File.ReadAllText(readme), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_RejectsInlineTemporaryLocalUserSettings()
    {
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = System.Management.Automation.PowerShell.Create(runspace);
        var settings = ScriptBlock.Create(@"
benchmark 'inline-temp-user' {
    profile TemporaryLocalUser
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
");
        ps.AddCommand("Invoke-BenchmarkSuite")
            .AddParameter("Settings", settings);

        var ex = Assert.Throws<System.Management.Automation.CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("-Path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeBenchmarkSuiteCommand_WhatIfSkipsTemporaryLocalUserExecution()
    {
        var root = CreateTempRoot();
        var spec = Path.Combine(root, "temp-user.benchmark.ps1");
        File.WriteAllText(spec, """
benchmark 'path-temp-user' -out 'out' {
    profile TemporaryLocalUser
    axis Operation Run
    axis Engine Managed
    engine Managed { operation Run { param($case, $run) } }
}
""");
        var initialSessionState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new System.Management.Automation.Runspaces.SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = System.Management.Automation.Runspaces.RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = System.Management.Automation.PowerShell.Create(runspace);
        ps.AddCommand("Set-Location").AddParameter("Path", root);
        ps.Invoke();
        ps.Commands.Clear();
        ps.AddCommand("Invoke-BenchmarkSuite")
            .AddParameter("Path", spec)
            .AddParameter("WhatIf");

        var output = ps.Invoke();

        Assert.Empty(output);
        Assert.Empty(ps.Streams.Error);
        Assert.False(Directory.Exists(Path.Combine(root, "out")));
    }

    [Fact]
    public void Runner_PlanKeepsExternalHostAxisVisible()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Host", Values = { "Core", "Desktop" } });

        var plan = new PowerShellBenchmarkRunner().Plan(suite);

        Assert.Contains(plan, item => item.Host.StartsWith("Core-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan, item => item.Host == "Desktop");
    }

    [Fact]
    public void Runner_RejectsExternalHostAxisDuringInProcessRun()
    {
        var suite = CreateRunnableSuite();
        var externalHost = PowerShellBenchmarkHostRuntime.GetCurrentHostLabel().StartsWith("Desktop-", StringComparison.OrdinalIgnoreCase)
            ? "Core"
            : "Desktop";
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Host", Values = { externalHost } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("only supports the current PowerShell host", ex.Message);
    }

    [Fact]
    public void HostExecutor_RecordsFailedSamplesWhenHostCannotBeResolved()
    {
        var root = CreateTempRoot();
        var spec = Path.Combine(root, "host-failure.benchmark.ps1");
        File.WriteAllText(spec, "# child process is not started for this regression test");
        var suite = CreateRunnableSuite();
        suite.OutputRoot = Path.Combine(root, "out");
        suite.RunMode = "test";
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Host", Values = { "PowerForgeMissingHost" } });

        var result = new PowerShellBenchmarkHostExecutor().Run(suite, new PowerShellBenchmarkHostRunRequest
        {
            SpecPath = spec,
            WorkingDirectory = root,
            OutputRoot = suite.OutputRoot,
            WarmupCount = 0,
            IterationCount = 1,
            RunMode = suite.RunMode,
            SuiteName = suite.Name,
            Hosts = new[] { "PowerForgeMissingHost" },
            ExternalHostTimeoutSeconds = 1
        });

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal("PowerForgeMissingHost", sample.Host);
        Assert.Contains("External host 'PowerForgeMissingHost' failed", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Failed", Assert.Single(result.Summary).Status);
    }

    [Fact]
    public void HostExecutor_DoesNotUseParentSkipRulesToSuppressExternalHost()
    {
        var root = CreateTempRoot();
        var spec = Path.Combine(root, "host-skipped.benchmark.ps1");
        File.WriteAllText(spec, "# child process is not started because the host cannot be resolved");
        var suite = CreateRunnableSuite();
        suite.OutputRoot = Path.Combine(root, "out");
        suite.Skip = ScriptBlock.Create("$true");
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Host", Values = { "PowerForgeMissingHost" } });

        var result = new PowerShellBenchmarkHostExecutor().Run(suite, new PowerShellBenchmarkHostRunRequest
        {
            SpecPath = spec,
            WorkingDirectory = root,
            OutputRoot = suite.OutputRoot,
            WarmupCount = 0,
            IterationCount = 1,
            RunMode = suite.RunMode,
            SuiteName = suite.Name,
            Hosts = new[] { "PowerForgeMissingHost" },
            ExternalHostTimeoutSeconds = 1
        });

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal("PowerForgeMissingHost", sample.Host);
        Assert.Contains("External host 'PowerForgeMissingHost' failed", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Failed", Assert.Single(result.Summary).Status);
    }

    [Fact]
    public void HostExecutor_NormalizesFullExecutablePathToCurrentForChildSelection()
    {
        var root = CreateTempRoot();
        var executable = Path.Combine(root, "pwsh");
        File.WriteAllText(executable, string.Empty);

        Assert.Equal("Current", PowerShellBenchmarkHostExecutor.NormalizeChildHostSelection(executable, executable));
        Assert.Equal("Core", PowerShellBenchmarkHostExecutor.NormalizeChildHostSelection("Core", executable));
    }

    [Fact]
    public void HostExecutor_DeduplicatesResolvedExecutableHostSelections()
    {
        var root = CreateTempRoot();
        var executable = Path.Combine(root, "pwsh");
        File.WriteAllText(executable, string.Empty);

        var selections = PowerShellBenchmarkHostExecutor.ResolveHostSelections(new[] { executable, executable });

        var selection = Assert.Single(selections);
        Assert.Equal(executable, selection.Host);
        Assert.Equal(executable, selection.Executable);
    }

    [Fact]
    public void HostExecutor_PreflightsReadmeBlocksBeforeLaunchingHost()
    {
        var root = CreateTempRoot();
        var spec = Path.Combine(root, "host-readme-preflight.benchmark.ps1");
        File.WriteAllText(spec, "# host should not launch before README validation");
        var readme = Path.Combine(root, "README.md");
        File.WriteAllText(readme, "<!-- BENCHMARK:other:START -->\nold\n<!-- BENCHMARK:other:END -->\n");
        var suite = CreateRunnableSuite();
        suite.OutputRoot = Path.Combine(root, "out");
        suite.ReadmeBlocks.Add(new PowerShellBenchmarkReadmeBlock
        {
            Path = readme,
            BlockId = "results",
            Renderer = "SummaryTable"
        });
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Host", Values = { "PowerForgeMissingHost" } });

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkHostExecutor().Run(suite, new PowerShellBenchmarkHostRunRequest
        {
            SpecPath = spec,
            WorkingDirectory = root,
            OutputRoot = suite.OutputRoot,
            WarmupCount = 0,
            IterationCount = 1,
            RunMode = suite.RunMode,
            SuiteName = suite.Name,
            Hosts = new[] { "PowerForgeMissingHost" },
            ExternalHostTimeoutSeconds = 1
        }));

        Assert.Contains("results", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("External host", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsTemporaryLocalUserProfileWithoutFileBackedExecutor()
    {
        var suite = CreateRunnableSuite();
        suite.Profile = PowerShellBenchmarkProfileKind.TemporaryLocalUser;

        var ex = Assert.Throws<InvalidOperationException>(() => new PowerShellBenchmarkRunner().Run(suite));

        Assert.Contains("TemporaryLocalUser", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file-backed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsDuplicateProgrammaticEngineNames()
    {
        var suite = CreateRunnableSuite();
        var engine = new PowerShellBenchmarkEngine { Name = "managed" };
        engine.Operations["Run"] = ScriptBlock.Create("param($case, $run)");
        suite.Engines.Add(engine);

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("duplicate engine", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsDuplicateProgrammaticAxisNames()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 1 } });
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "rows", Values = { 2 } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("duplicate matrix axis", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsUnsupportedOsAxis()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "OS", Values = { "Windows", "Linux" } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("OS axis", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsRunModeAxis()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "RunMode", Values = { "quick", "publish" } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("RunMode axis", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsDuplicateMatrixAxisValues()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { 10, 10 } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("duplicate value", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rows", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsCaseOnlyMatrixAxisValues()
    {
        var suite = CreateRunnableSuite();
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { "abc", "ABC" } });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("duplicate value", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ABC", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_RejectsDuplicateExpandedCaseLanes()
    {
        var suite = CreateRunnableSuite();
        suite.Cases.Add(new PowerShellBenchmarkCase { Name = "Same" });
        suite.Cases.Add(new PowerShellBenchmarkCase { Name = "Same" });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("duplicate case lane", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runner_RejectsCaseOnlyExpandedCaseLanePathCollisions()
    {
        var suite = CreateRunnableSuite();
        suite.Cases.Add(new PowerShellBenchmarkCase
        {
            Name = "Same",
            Values = { ["Input"] = "abc" }
        });
        suite.Cases.Add(new PowerShellBenchmarkCase
        {
            Name = "Same",
            Values = { ["Input"] = "ABC" }
        });

        var ex = Assert.Throws<NotSupportedException>(() => new PowerShellBenchmarkRunner().Plan(suite));

        Assert.Contains("duplicate case lane", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

}
