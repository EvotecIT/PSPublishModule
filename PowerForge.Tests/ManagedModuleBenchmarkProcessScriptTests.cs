using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkProcessScriptTests
{
    [Fact]
    public void ProcessHelper_CapturesStdoutAndLargeStderrWithoutDeadlock()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $hostCommand = Get-Command -Name pwsh -ErrorAction SilentlyContinue
            if (-not $hostCommand) {
                [pscustomobject]@{ Skipped = $true }
                return
            }

            $script = "[Console]::Out.WriteLine('out-ok'); [Console]::Error.WriteLine(('err-' * 20000))"
            $result = Invoke-ManagedBenchmarkProcess -FileName $hostCommand.Source -Arguments @('-NoLogo', '-NoProfile', '-Command', $script) -TimeoutSeconds 15
            [pscustomobject]@{
                Skipped = $false
                ExitCode = $result.ExitCode
                TimedOut = $result.TimedOut
                HasStdout = $result.StandardOutput.Contains('out-ok')
                HasStderr = $result.StandardError.Contains('err-err-err')
            }
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var output = Assert.Single(results);
        if (BooleanProperty(output, "Skipped"))
            return;

        Assert.Equal(0.0, NumericProperty(output, "ExitCode"));
        Assert.False(BooleanProperty(output, "TimedOut"));
        Assert.True(BooleanProperty(output, "HasStdout"));
        Assert.True(BooleanProperty(output, "HasStderr"));
    }

    [Fact]
    public void ProcessHelper_TimesOutAndReportsChildProcess()
    {
        using var ps = CreateBenchmarkPowerShell("""
            $hostCommand = Get-Command -Name pwsh -ErrorAction SilentlyContinue
            if (-not $hostCommand) {
                [pscustomobject]@{ Skipped = $true }
                return
            }

            $result = Invoke-ManagedBenchmarkProcess -FileName $hostCommand.Source -Arguments @('-NoLogo', '-NoProfile', '-Command', 'Start-Sleep -Seconds 10') -TimeoutSeconds 1 -TimeoutMessage 'test child timed out'
            [pscustomobject]@{
                Skipped = $false
                ExitCode = $result.ExitCode
                TimedOut = $result.TimedOut
                TimeoutSeconds = $result.TimeoutSeconds
                TimeoutMessage = $result.TimeoutMessage
            }
            """);

        var results = ps.Invoke();

        AssertNoErrors(ps);
        var output = Assert.Single(results);
        if (BooleanProperty(output, "Skipped"))
            return;

        Assert.Equal(-1.0, NumericProperty(output, "ExitCode"));
        Assert.True(BooleanProperty(output, "TimedOut"));
        Assert.Equal(1.0, NumericProperty(output, "TimeoutSeconds"));
        Assert.Equal("test child timed out", Property(output, "TimeoutMessage"));
    }

    private static PowerShell CreateBenchmarkPowerShell(string script)
    {
        var processScript = Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "ManagedModuleBenchmark.Process.ps1");
        var ps = PowerShell.Create();
        ps.AddScript(File.ReadAllText(processScript) + Environment.NewLine + script);
        return ps;
    }

    private static string Property(PSObject value, string name)
        => (string)value.Properties[name].Value;

    private static double NumericProperty(PSObject value, string name)
        => Convert.ToDouble(value.Properties[name].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static bool BooleanProperty(PSObject value, string name)
        => Convert.ToBoolean(value.Properties[name].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static void AssertNoErrors(PowerShell ps)
    {
        if (ps.Streams.Error.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString()));
        Assert.Fail(message);
    }
}
