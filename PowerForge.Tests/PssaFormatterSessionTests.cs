using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed class PssaFormatterSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_ReusesHostWithDistinctPhaseSettingsAndDisposesIt(bool desktop)
    {
        if (desktop && !OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(desktop);
        var formatter = fixture.CreateFormatter();
        int processId;
        using (formatter.BeginSession())
        {
            var first = fixture.Input("first.ps1", "first");
            var second = fixture.Input("second ü.ps1", "second");
            Assert.True(Assert.Single(formatter.FormatBatches(Batch(first, "one"))).Changed);
            Assert.True(Assert.Single(formatter.FormatBatches(Batch(second, "two"))).Changed);
            Assert.Equal("first-one", File.ReadAllText(first));
            Assert.Equal("second-two", File.ReadAllText(second));
            var rows = fixture.Rows();
            Assert.Equal(2, rows.Length);
            Assert.Equal(rows[0][0], rows[1][0]);
            Assert.Equal("one", rows[0][2]);
            Assert.Equal("two", rows[1][2]);
            Assert.All(rows, row => Assert.Equal(Environment.CurrentDirectory, row[3]));
            processId = int.Parse(rows[0][0]);
        }
        AssertExited(processId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_TimeoutRetainsCompletedErrorsTerminatesHostAndRestartsNextPhase(bool desktop)
    {
        if (desktop && !OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(desktop);
        var formatter = fixture.CreateFormatter();
        using var session = formatter.BeginSession();
        formatter.FormatBatches(Batch(fixture.Input("warm.ps1", "warm"), "warm"));
        var processId = int.Parse(fixture.Rows()[0][0]);
        var failed = fixture.Input("failed.ps1", "fail");
        var hanging = fixture.Input("hanging.ps1", "hang");
        var unfinished = fixture.Input("unfinished.ps1", "unfinished");

        var results = formatter.FormatBatches(new[] { new FormattingBatch(
            new[] { failed, hanging, unfinished }, new FormatOptions { TimeoutSeconds = 1 }) });

        Assert.Equal(3, results.Count);
        Assert.Equal("Error: phase formatting failed", results.Single(item => item.Path == failed).Message);
        Assert.All(results.Where(item => item.Path != failed), item => Assert.Equal("Skipped: Timeout", item.Message));
        Assert.Equal(CheckStatus.Fail, FormattingSummary.FromResults(results).Status);
        AssertExited(processId);
        var next = fixture.Input("next.ps1", "next");
        Assert.True(Assert.Single(formatter.FormatBatches(Batch(next, "restarted"))).Changed);
        Assert.Equal("next-restarted", File.ReadAllText(next));
        Assert.NotEqual(processId, int.Parse(fixture.Rows().Last()[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_PhaseFailureUnwindsHostOwnershipAndAllowsAnotherScope(bool desktop)
    {
        if (desktop && !OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(desktop);
        var formatter = fixture.CreateFormatter();
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var session = formatter.BeginSession();
            var results = formatter.FormatBatches(Batch(fixture.Input("failed.ps1", "fail"), "failure"));
            Assert.Equal(CheckStatus.Fail, FormattingSummary.FromResults(results).Status);
            throw new InvalidOperationException("Stop after staging failure.");
        }));
        var processId = int.Parse(fixture.Rows()[0][0]);
        AssertExited(processId);
        using var nextScope = formatter.BeginSession();
        Assert.True(Assert.Single(formatter.FormatBatches(Batch(fixture.Input("next.ps1", "next"), "next"))).Changed);
        Assert.NotEqual(processId, int.Parse(fixture.Rows().Last()[0]));
    }

    private static FormattingBatch[] Batch(string path, string tag) => new[]
    {
        new FormattingBatch(new[] { path }, new FormatOptions { PssaSettingsJson = "{\"Tag\":\"" + tag + "\"}" })
    };

    private static void AssertExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.True(process.HasExited, $"Formatter process {processId} survived its scope.");
        }
        catch (ArgumentException) { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _desktop;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        private string Log => Path.Combine(_root, "requests.log");

        internal Fixture(bool desktop)
        {
            _desktop = desktop;
            var module = Path.Combine(_root, "modules", "PSScriptAnalyzer", "999.0.0");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "PSScriptAnalyzer.psd1"),
                "@{RootModule='PSScriptAnalyzer.psm1';ModuleVersion='999.0.0';GUID='f34922f7-e271-4c02-abd2-6f987e6411c2';FunctionsToExport=@('Invoke-Formatter')}");
            File.WriteAllText(Path.Combine(module, "PSScriptAnalyzer.psm1"), """
                function Invoke-Formatter {
                    [CmdletBinding()]
                    param([string]$ScriptDefinition, [hashtable]$Settings)
                    $code = $ScriptDefinition.Trim()
                    $tag = if ($Settings) { $Settings.Tag } else { '' }
                    [IO.File]::AppendAllText($env:PSSA_SESSION_LOG, "$PID|$code|$tag|$([Environment]::CurrentDirectory)`n")
                    if ($code -eq 'fail') { Write-Error 'phase formatting failed' }
                    if ($code -eq 'hang') { Start-Sleep -Seconds 30 }
                    return "$code-$tag"
                }
                Export-ModuleMember -Function Invoke-Formatter
                """, new UTF8Encoding(true));
        }

        internal string Input(string name, string content)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, content, new UTF8Encoding(true));
            return path;
        }

        internal string[][] Rows() => File.ReadAllLines(Log).Select(line => line.Split('|')).ToArray();

        internal PssaFormatter CreateFormatter() => new(new EnvironmentRunner(_desktop, new Dictionary<string, string?>
        {
            ["PSModulePath"] = Path.Combine(_root, "modules"),
            ["PSSA_SESSION_LOG"] = Log
        }), new NullLogger());

        public void Dispose() { Directory.Delete(_root, recursive: true); }
    }

    private sealed class EnvironmentRunner(bool desktop, IReadOnlyDictionary<string, string?> environment)
        : IPowerShellRunner, ICancellablePowerShellRunner
    {
        private readonly PowerShellRunner _runner = new();

        public PowerShellRunResult Run(PowerShellRunRequest request) => _runner.Run(Map(request));

        public Task<PowerShellRunResult> RunAsync(PowerShellRunRequest request, CancellationToken cancellationToken)
            => ((ICancellablePowerShellRunner)_runner).RunAsync(Map(request), cancellationToken);

        private PowerShellRunRequest Map(PowerShellRunRequest request) => new(
            request.ScriptPath!, request.Arguments, request.Timeout,
            preferPwsh: !desktop,
            workingDirectory: request.WorkingDirectory,
            environmentVariables: environment,
            executableOverride: desktop ? "powershell.exe" : null,
            captureOutput: request.CaptureOutput,
            captureError: request.CaptureError,
            outputLineReceived: request.OutputLineReceived,
            errorLineReceived: request.ErrorLineReceived);
    }
}
