using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task PackagedExecutablesPreserveNativeRootExitOnPinnedHosts()
    {
        const string source = """
            param([string] $Case)
            function Get-ExitMarker { return 'before' }
            function Stop-Root { exit 31 }
            switch ($Case) {
                'try' {
                    try { Get-ExitMarker; exit '17' }
                    catch { 'unexpected catch'; exit 91 }
                    finally { 'cleanup' }
                }
                'catch' {
                    try { throw 'authored failure' }
                    catch { 'caught'; exit 23 }
                    finally { 'cleanup' }
                }
                'function' { Get-ExitMarker; Stop-Root }
                'nested' { exit $(exit 41) }
                'trap' { trap { 'unexpected trap'; continue }; exit 43 }
                'native' { & cmd.exe /d /c 'exit 37' }
                'prompt' { $answer = Microsoft.PowerShell.Utility\Read-Host -Prompt 'Input'; "value=[$answer]" }
                'plain' {
                    $answer = Read-Host
                    if ($null -eq $answer) { 'missing' } else { "value=[$answer]" }
                }
            }
            'after'
            """;
        var cases = new (string Name, int Code, string Output, string Input)[]
        {
            ("try", 17, "before\ncleanup", string.Empty),
            ("catch", 23, "caught\ncleanup", string.Empty),
            ("function", 31, "before", string.Empty),
            ("nested", 41, string.Empty, string.Empty),
            ("trap", 43, string.Empty, string.Empty),
            ("native", 0, "after", string.Empty),
            ("prompt", 0, "example line\nvalue=[example line]\nafter", "example line\n"),
            ("plain", 0, "\nvalue=[]\nafter", "\n"),
            ("plain", 0, "missing\nafter", string.Empty)
        };
        var modern = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modern), "The exact PowerShell 7.6.6 host is required.");
        var desktop = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        using var pinFixture = OracleFixture.Create("'qualified host'");
        using var fixture = OracleFixture.Create(source);
        foreach (var (frameworkProfile, executable) in new[]
        {
            (PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modern!),
            (PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, desktop)
        })
        {
            var pin = new PowerShellCompilationSemanticOracleRunner().Observe(
                new PowerShellCompilationSemanticOracleRequest(frameworkProfile, pinFixture.ScriptPath)
                {
                    HostExecutablePath = executable,
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                        PowerShellCompilationSemanticHostArtifactPinCatalog.Get(frameworkProfile))
                });
            Assert.Equal("qualified host", Assert.Single(pin.Success).Value);
            foreach (var scenario in cases)
            {
                await AssertExitAndConsoleOutput(executable, fixture.RootPath,
                    new[] { "-NoLogo", "-NoProfile", "-File", fixture.ScriptPath, scenario.Name }, scenario);
            }
        }

        var host = new PowerShellCompilationHostService();
        foreach (var mode in new[] { PowerShellCompilationMode.Package, PowerShellCompilationMode.Hybrid })
        {
            var output = Path.Combine(fixture.RootPath, mode.ToString());
            var check = host.Check(new PowerShellCompilationCheckRequest(fixture.ScriptPath) { Mode = mode, OutputDirectory = output });
            Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
            var built = host.Compile(new PowerShellCompilationBuildSpec(fixture.ScriptPath, output, "ExitProbe",
                PowerShellCompilationArtifactKind.Executable, mode), check.ProposedReview!);
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var scenario in cases)
            {
                await AssertExitAndConsoleOutput(built.ArtifactPath!, fixture.RootPath, new[] { scenario.Name }, scenario);
            }
        }
    }

    private static async Task AssertExitAndConsoleOutput(string executable, string root, string[] arguments,
        (string Name, int Code, string Output, string Input) expected)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteAsync(expected.Input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var observed = (process.ExitCode, (await stdout).ReplaceLineEndings("\n").TrimEnd('\n'), await stderr);
            Assert.Equal((expected.Code, expected.Output, string.Empty), observed);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
