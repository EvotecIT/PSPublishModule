using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkResultRowScriptTests
{
    [Fact]
    public void FailedRowsExposeReasonAndError()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $script:ModuleName = 'Company.Tools'
            New-FailedRow -OperationName SaveNoOp -EngineName Managed -Iteration 1 -Reason 'setup failed' -OutputRoot 'C:\Temp\out'
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("Failed", Property(row, "Status"));
        Assert.Equal("setup failed", Property(row, "Reason"));
        Assert.Equal("setup failed", Property(row, "Error"));
    }

    [Fact]
    public void SkippedRowsExposeReasonAndError()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $script:ModuleName = 'Company.Tools'
            New-SkippedRow -OperationName Save -EngineName ModuleFast -Iteration 1 -Reason 'not supported'
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var row = Assert.Single(results);
        Assert.Equal("Skipped", Property(row, "Status"));
        Assert.Equal("not supported", Property(row, "Reason"));
        Assert.Equal("not supported", Property(row, "Error"));
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var resultRowScript = Path.Combine(RepoRootLocator.Find(), "Benchmarks", "ManagedModules", "ManagedModuleBenchmark.ResultRows.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(resultRowScript) + Environment.NewLine + script);
        return ps;
    }

    private static string Property(PSObject value, string name)
        => value.Properties[name].Value?.ToString() ?? string.Empty;

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        Assert.Fail(message);
    }
}
