using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void InvokeBenchmarkSuiteCommand_EvaluatesGetNewClosureSettingsWithLocalDslFunctions()
    {
        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.Commands.Add(new SessionStateCmdletEntry("Invoke-BenchmarkSuite", typeof(PSPublishModule.InvokeBenchmarkSuiteCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.Open();
        ImportBenchmarkDslCommands(runspace);
        using var ps = PowerShell.Create(runspace);
        ps.AddScript("""
$caseName = 'FromClosure'
$settings = {
    New-BenchmarkSuite 'inline-closure' {
        Set-BenchmarkPolicy -Warmup 0 -Iterations 1
        Add-BenchmarkCases { Add-BenchmarkCase A @{ Probe = $caseName } }
        Add-BenchmarkAxis Operation Run
        Add-BenchmarkAxis Engine Managed
        Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { param($case, $run) } }
    }
}.GetNewClosure()
Invoke-BenchmarkSuite -Settings $settings -Plan
""");

        var plan = ps.Invoke();

        Assert.Empty(ps.Streams.Error);
        var item = Assert.IsType<PowerShellBenchmarkWorkItem>(Assert.Single(plan).BaseObject);
        Assert.Equal("FromClosure", item.Values["Probe"]);
    }
}
