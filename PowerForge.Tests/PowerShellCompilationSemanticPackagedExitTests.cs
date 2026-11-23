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

        var host = new PowerShellCompiler();
        foreach (var mode in new[] { PowerShellCompilationMode.Package, PowerShellCompilationMode.Hybrid })
        {
            var output = Path.Combine(fixture.RootPath, mode.ToString());
            var check = host.Check(new PowerShellCompilationRequest(fixture.ScriptPath) { Mode = mode, OutputDirectory = output });
            Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
            var built = host.BuildReviewed(new PowerShellCompilationBuildSpec(fixture.ScriptPath, output, "ExitProbe",
                PowerShellCompilationArtifactKind.Executable, mode), check.ProposedReview!);
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var scenario in cases)
            {
                await AssertExitAndConsoleOutput(built.ArtifactPath!, fixture.RootPath, new[] { scenario.Name }, scenario);
            }
        }
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task StrictExecutableExitUnwindsLocalFunctionsWithoutEnteringCatchOnPinnedHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([int] $Code = 37)
            function Stop-Script {
                param([int] $Status)
                try {
                    'before', 'unused' | Select-Object -First 1
                    if ($Status -eq 0) { exit }
                    exit $Status
                } catch {
                    'inner catch', 'unused' | Select-Object -First 1
                } finally {
                    'inner finally', 'unused' | Select-Object -First 1
                }
            }
            try {
                Stop-Script $Code
                'after call', 'unused' | Select-Object -First 1
            } catch {
                'outer catch', 'unused' | Select-Object -First 1
            } finally {
                'outer finally', 'unused' | Select-Object -First 1
            }
            'after try', 'unused' | Select-Object -First 1
            """);
        var output = string.Join(Environment.NewLine, "before", "inner finally", "outer finally", string.Empty);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var build = BuildStrictStreamingFixture(fixture, "StrictScriptExit", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.False(build.Manifest!.RequiresPowerShellRuntime);
            Assert.True(build.Manifest.DependencyClosureVerified);
            foreach (var code in new[] { "37", "0" })
            {
                var expected = (code, code == "37" ? 37 : 0, output, string.Empty);
                await AssertExitAndConsoleOutput(host, fixture.RootPath,
                    new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, code }, expected);
                await AssertExitAndConsoleOutput(build.ArtifactPath!, fixture.RootPath, new[] { code }, expected);
            }
        }
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task StrictExecutableReadHostPreservesTheAssignmentOnStatementErrors()
    {
        using var fixture = OracleFixture.Create("""
            param([switch] $Caught)
            $answer = 'previous'
            if ($Caught) {
                try {
                    $answer = Microsoft.PowerShell.Utility\Read-Host -Prompt 'Input'
                } catch {
                    "caught line=$($_.InvocationInfo.ScriptLineNumber): $($Error[0])"
                }
            } else {
                $answer = Microsoft.PowerShell.Utility\Read-Host -Prompt 'Input'
            }
            if ($null -eq $answer) { return 'missing' }
            return "value=[$answer]"
            """);
        var eol = Environment.NewLine;
        var cases = new (string Input, bool Caught, string? Output, bool ReportsError)[]
        {
            ("line\n", false, $"line\nvalue=[line]{eol}", false),
            ("line\r", false, $"line\rvalue=[line]{eol}", false),
            ("line\r\n", false, $"line\r\nvalue=[line]{eol}", false),
            ("line", false, $"linevalue=[line]{eol}", false),
            ("\r\n", false, $"\r\nvalue=[]{eol}", false),
            (string.Empty, false, $"missing{eol}", false),
            ("xy\bz\r\n", false, $"xy\bz\r\nvalue=[xz]{eol}", false),
            ("żółw 🐢\r\n", false, null, false),
            ("\brest\r\n", false, $"\bvalue=[previous]{eol}", true),
            ("\brest\r\n", true, null, false)
        };
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var build = BuildStrictStreamingFixture(fixture, "StrictReadHost", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.False(build.Manifest!.RequiresPowerShellRuntime);
            Assert.True(build.Manifest.DependencyClosureVerified);
            foreach (var scenario in cases)
            {
                var hostArguments = new[] { "-NoLogo", "-NoProfile", "-File", fixture.ScriptPath };
                if (scenario.Caught) hostArguments = hostArguments.Append("-Caught").ToArray();
                var original = await ObserveExitedConsole(host, fixture.RootPath, hostArguments, scenario.Input);
                Assert.Equal(0, original.Code);
                if (scenario.Output is { } expectedOutput) Assert.Equal(expectedOutput, original.Output);
                else Assert.NotEmpty(original.Output);
                Assert.Equal(scenario.ReportsError, original.Error.Length != 0);
                var expectedError = original.Error.Replace(fixture.ScriptPath, Path.GetFileName(fixture.ScriptPath));
                var compiled = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath,
                    scenario.Caught ? new[] { "--Caught" } : Array.Empty<string>(), scenario.Input);
                Assert.Equal(original.Code, compiled.Code);
                Assert.Equal(original.Output, compiled.Output);
                if (scenario.ReportsError && pin.ProfileId == PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
                {
                    AssertReadHostDesktopDiagnostic(original.Error, fixture.ScriptPath);
                    AssertReadHostDesktopDiagnostic(compiled.Error);
                }
                else
                {
                    Assert.Equal(expectedError, compiled.Error);
                }
            }
        }

        static void AssertReadHostDesktopDiagnostic(string error, string? sourcePath = null)
        {
            // The Desktop formatter can split even the source path at the console's width.
            var fields = System.Text.RegularExpressions.Regex.Replace(error, @"\s+", "");
            if (sourcePath is not null)
                fields = fields.Replace(System.Text.RegularExpressions.Regex.Replace(sourcePath, @"\s+", ""),
                    Path.GetFileName(sourcePath), StringComparison.Ordinal);
            Assert.StartsWith(@"Microsoft.PowerShell.Utility\Read-Host:StartIndexcannotbelessthanzero.", fields);
            Assert.Contains("Parametername:startIndex", fields, StringComparison.Ordinal);
            Assert.Contains("Atcase.ps1:10char:15", fields, StringComparison.Ordinal);
            Assert.Contains(@"+$answer=Microsoft.PowerShell.Utility\Read-Host-Prompt'Input'", fields, StringComparison.Ordinal);
            Assert.Contains("CategoryInfo:NotSpecified:(:)[Read-Host],ArgumentOutOfRangeException", fields, StringComparison.Ordinal);
            Assert.Contains("FullyQualifiedErrorId:System.ArgumentOutOfRangeException,Microsoft.PowerShell.Commands.ReadHostCommand",
                fields, StringComparison.Ordinal);
        }
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task StrictExecutableObservesReturnedRecordBeforeFinallyExitsOnPinnedHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([int] $Status)
            function Stop-Script {
                param([int] $Code)
                exit $Code
            }
            try { return 'before' } finally { Stop-Script $Status }
            """);
        var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver();
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var build = BuildStrictStreamingFixture(fixture, "StrictFinallyExit", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            foreach (var code in new[] { 0, 17 })
            {
                var argument = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await AssertExitAndConsoleOutput(host, fixture.RootPath,
                    new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, argument },
                    (argument, code, "before" + Environment.NewLine, string.Empty));
                var observed = observer.Observe(pin.ProfileId, build, new[] { argument });
                Assert.Equal(code, observed.ExitCode);
                Assert.Equal("before", Assert.Single(observed.Success).Value);
                Assert.Empty(observed.ErrorRecords);
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
        var check = new PowerShellCompiler().Check(new PowerShellCompilationRequest(fixture.ScriptPath)
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
        var check = new PowerShellCompiler().Check(new PowerShellCompilationRequest(fixture.ScriptPath)
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

        var check = new PowerShellCompiler().Check(new PowerShellCompilationRequest(fixture.ScriptPath)
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
        => Assert.Equal((expected.Code, expected.Output, string.Empty),
            await ObserveExitedConsole(executable, root, arguments, expected.Input));

    private static async Task<(int Code, string Output, string Error)> ObserveExitedConsole(
        string executable, string root, string[] arguments, string input)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.Environment["NO_COLOR"] = "1";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout, await stderr);
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
