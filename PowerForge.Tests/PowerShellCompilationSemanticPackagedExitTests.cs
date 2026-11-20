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
            $PSDefaultParameterValues = @{ 'Write-Output:NoEnumerate' = $false }
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
                'native' { & cmd.exe /d /c 'exit 37'; "child=$LASTEXITCODE" }
                'prompt' {
                    $answer = Microsoft.PowerShell.Utility\Read-Host -Prompt 'Input'
                    if ($null -eq $answer) { 'missing' } else { "value=[$answer]" }
                }
                'plain' {
                    $answer = Read-Host
                    if ($null -eq $answer) { 'missing' } else { "value=[$answer]" }
                }
            }
            'after'
            """;
        var eol = Environment.NewLine;
        var cases = new (string Name, int Code, string Output, string Input)[]
        {
            ("try", 17, $"before{eol}cleanup{eol}", string.Empty),
            ("catch", 23, $"caught{eol}cleanup{eol}", string.Empty),
            ("function", 31, $"before{eol}", string.Empty),
            ("nested", 41, string.Empty, string.Empty),
            ("trap", 43, string.Empty, string.Empty),
            ("native", 0, $"child=37{eol}after{eol}", string.Empty),
            ("prompt", 0, $"line\nvalue=[line]{eol}after{eol}", "line\n"),
            ("prompt", 0, $"linevalue=[line]{eol}after{eol}", "line"),
            ("prompt", 0, $"line\rvalue=[line]{eol}after{eol}", "line\r"),
            ("prompt", 0, $"missing{eol}after{eol}", string.Empty),
            ("plain", 0, $"\r\nvalue=[]{eol}after{eol}", "\r\n"),
            ("plain", 0, $"xy\bz\r\nvalue=[xz]{eol}after{eol}", "xy\bz\r\n"),
            ("plain", 0, $"missing{eol}after{eol}", string.Empty)
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

    [Theory]
    [InlineData(PowerShellCompilationMode.Package, "$PSDefaultParameterValues = @{'Read-Host : MaskInput'=$true}; Read-Host")]
    [InlineData(PowerShellCompilationMode.Hybrid, "$PSDefaultParameterValues = @{'Read-Host : MaskInput'=$true}; Read-Host")]
    [InlineData(PowerShellCompilationMode.Package, "Set-Alias Set-Defaults Set-Variable; Set-Defaults -N script:PSDefaultParameterValues @{'Read-Host:AsSecureString'=$true}; Read-Host")]
    [InlineData(PowerShellCompilationMode.Hybrid, "Set-Alias Set-Defaults Set-Variable; Set-Defaults -N script:PSDefaultParameterValues @{'Read-Host:AsSecureString'=$true}; Read-Host")]
    [InlineData(PowerShellCompilationMode.Package, "function Set-Defaults { [CmdletBinding()] param() $PSCmdlet.SessionState.PSVariable.Set('PSDefaultParameterValues', @{'Read-Host:AsSecureString'=$true}) }; Set-Defaults; Read-Host")]
    [InlineData(PowerShellCompilationMode.Hybrid, "function Set-Defaults { [CmdletBinding()] param($Name) $PSCmdlet.SessionState.PSVariable.Set($Name, @{'Read-Host:AsSecureString'=$true}) }; Set-Defaults PSDefaultParameterValues; Read-Host")]
    [InlineData(PowerShellCompilationMode.Package, "Set-Item -LiteralPath Variable:PSDefaultParameterValues -Value @{'Read-Host:MaskInput'=$true}; Read-Host")]
    [InlineData(PowerShellCompilationMode.Package, "Set-Item -Force -Value @{'Read-Host:MaskInput'=$true} Variable:PSDefaultParameterValues; Read-Host")]
    [InlineData(PowerShellCompilationMode.Package, "New-Item -Path Variable: -Name PSDefaultParameterValues -Value @{'Read-Host:AsSecureString'=$true} -Force; Read-Host")]
    [InlineData(PowerShellCompilationMode.Hybrid, "Set-Alias Set-Defaults Set-Item; Set-Defaults Variable:PSDefaultParameterValues @{'Read-Host:AsSecureString'=$true}; Read-Host")]
    public void CheckRejectsProtectedReadHostParameterDefaults(PowerShellCompilationMode mode, string source)
    {
        using var fixture = OracleFixture.Create(source);
        var check = new PowerShellCompilationHostService().Check(new PowerShellCompilationCheckRequest(fixture.ScriptPath)
        {
            Mode = mode,
            OutputDirectory = Path.Combine(fixture.RootPath, "app")
        });

        Assert.False(check.CanProceed);
        Assert.Null(check.ProposedReview);
        Assert.Contains(check.Diagnostics, item => item.Severity == PowerShellCompilationHostSeverity.Error &&
            item.Message.Contains("cannot verify plain Read-Host input with parameter defaults", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PowerShellCompilationMode.Package)]
    [InlineData(PowerShellCompilationMode.Hybrid)]
    public void CheckAllowsWriteToUnrelatedVariablesBeforeReadHost(PowerShellCompilationMode mode)
    {
        using var fixture = OracleFixture.Create("""
            function Set-OtherValue {
                [CmdletBinding()] param()
                $PSCmdlet.SessionState.PSVariable.Set('OtherValue', 21)
            }
            Set-OtherValue
            Set-Item -LiteralPath Variable:OtherValue -Value 42
            New-Item -Path Variable: -Name ItemValue -Value 19
            Set-Item -LiteralPath C:\\output.txt -Value 'ordinary filesystem path'
            Read-Host -Prompt 'Name'
            """);
        var check = new PowerShellCompilationHostService().Check(new PowerShellCompilationCheckRequest(fixture.ScriptPath)
        {
            Mode = mode,
            OutputDirectory = Path.Combine(fixture.RootPath, "app")
        });

        Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(static item => item.Message)));
        Assert.NotNull(check.ProposedReview);
    }

    [Theory]
    [InlineData(PowerShellCompilationMode.Package)]
    [InlineData(PowerShellCompilationMode.Hybrid)]
    public void CheckRejectsReadHostDefaultsFromAStagedDependency(PowerShellCompilationMode mode)
    {
        using var fixture = OracleFixture.Create(". \"$PSScriptRoot/Setup.ps1\"; Read-Host");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Setup.ps1"),
            "$PSDefaultParameterValues['Read-*:MaskInput'] = $true");

        var check = new PowerShellCompilationHostService().Check(new PowerShellCompilationCheckRequest(fixture.ScriptPath)
        {
            Mode = mode,
            OutputDirectory = Path.Combine(fixture.RootPath, "app")
        });

        Assert.False(check.CanProceed);
        Assert.Contains(check.Diagnostics, item => item.Severity == PowerShellCompilationHostSeverity.Error &&
            item.Message.Contains("cannot verify plain Read-Host input with parameter defaults", StringComparison.Ordinal));
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
            var observed = (process.ExitCode, await stdout, await stderr);
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
