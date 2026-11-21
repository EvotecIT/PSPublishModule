namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task PackagedSourceCallsPreserveChildScopeAndReceiveArgumentsOnBothHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Label, [switch] $Each, [string] $OverridePath, [string] $ResourcePath = "$PSScriptRoot/label.txt")
            $ErrorActionPreference = 'Stop'
            $directory = Split-Path -Parent $MyInvocation.MyCommand.Path
            $value = 'outer'
            if ($Each) {
                $scriptPath = Join-Path $directory 'helper.ps1'
                $results = foreach ($item in 1, 2) {
                    & $scriptPath -Label "$Label$item" 3>$null 4>$null 5>$null
                }
            }
            else {
                $scriptPath = Join-Path $directory 'helper.ps1'
                if ($OverridePath) { $scriptPath = $OverridePath }
                $results = & $scriptPath -Label $Label 3>$null 4>$null 5>$null
            }
            function Get-Tag { return 'parent' }
            $defaultResource = Get-Content -LiteralPath $ResourcePath -Raw
            foreach ($result in $results) {
                "$result|$value|$(Get-Tag)|$defaultResource"
            }
            Write-Warning 'visible warning'
            Write-Verbose 'visible verbose' -Verbose
            $DebugPreference = 'Continue'
            Write-Debug 'visible debug'
            """);
        var helper = Path.Combine(fixture.RootPath, "helper.ps1");
        File.WriteAllText(helper, """
            param([string] $Label)
            $parentVisible = [bool](Get-Command Get-Tag -ErrorAction SilentlyContinue)
            function Get-Tag { return 'child' }
            $value = 'inner'
            Write-Warning 'hidden warning'
            Write-Verbose 'hidden verbose' -Verbose
            $DebugPreference = 'Continue'
            Write-Debug 'hidden debug'
            $resource = Get-Content -LiteralPath "$PSScriptRoot/label.txt" -Raw
            "$parentVisible|$(Get-Tag):${Label}:$resource"
            """);
        File.WriteAllText(Path.Combine(fixture.RootPath, "label.txt"), "bundled");
        var other = Path.Combine(fixture.RootPath, "other.ps1");
        File.WriteAllText(other, "'unexpected source ran'");
        var service = new PowerShellCompilationHostService();
        var output = Path.Combine(fixture.RootPath, "hybrid");
        var check = service.Check(new PowerShellCompilationCheckRequest(fixture.ScriptPath)
        {
            Mode = PowerShellCompilationMode.Hybrid,
            OutputDirectory = output
        });
        Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, output, "SourceCall",
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            CompilationSourcePaths = input.CompilationSourceFiles,
            RuntimeSourcePaths = input.SourceFiles
        };
        var build = service.Compile(spec, check.ProposedReview!);
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.Single(build.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == "Get-Tag" && entry.EmittedClrMethod);

        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            foreach (var (arguments, expected) in new[]
            {
                (new[] { "-Label", "Ada" }, new[] { "False|child:Ada:bundled|outer|parent|bundled",
                    "WARNING: visible warning", "VERBOSE: visible verbose", "DEBUG: visible debug" }),
                (new[] { "-Label", "Ada", "-Each" }, new[] { "False|child:Ada1:bundled|outer|parent|bundled", "False|child:Ada2:bundled|outer|parent|bundled",
                    "WARNING: visible warning", "VERBOSE: visible verbose", "DEBUG: visible debug" })
            })
            {
                var original = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }.Concat(arguments).ToArray(), string.Empty);
                var packaged = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, arguments, string.Empty);
                Assert.Equal(0, original.Code);
                Assert.Equal(expected, original.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                Assert.Empty(original.Error);
                Assert.True(packaged.Code == 0, packaged.Error);
                // ConsoleHost can use LF for diagnostics alongside CRLF success output on Windows.
                Assert.Equal(original.Output.Replace("\r\n", "\n"), packaged.Output.Replace("\r\n", "\n"));
                Assert.Equal(original.Error, packaged.Error);
            }
        }

        var rejected = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath,
            new[] { "-Label", "Ada", "-OverridePath", other }, string.Empty);
        Assert.Equal(1, rejected.Code);
        Assert.Empty(rejected.Output);
        Assert.Contains("selected staged source file", rejected.Error, StringComparison.OrdinalIgnoreCase);
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task PackagedChildCallsFromDotSourcedFilesPreserveCallerDeclarationTiming()
    {
        using var fixture = OracleFixture.Create("""
            . "$PSScriptRoot/Loader.ps1"
            function Get-EntryTag { return 'entry' }
            # Both have executed after their declaration.
            "$(Get-LoaderTag)|$(Get-EntryTag)"
            """);
        File.WriteAllText(Path.Combine(fixture.RootPath, "Loader.ps1"), """
            & "$PSScriptRoot/Child.ps1"
            function Get-LoaderTag { return 'loader' }
            """);
        File.WriteAllText(Path.Combine(fixture.RootPath, "Child.ps1"), """
            $entryVisible = [bool](Get-Command Get-EntryTag -ErrorAction SilentlyContinue)
            $loaderVisible = [bool](Get-Command Get-LoaderTag -ErrorAction SilentlyContinue)
            "$entryVisible|$loaderVisible"
            """);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var output = Path.Combine(fixture.RootPath, "hybrid");
        var service = new PowerShellCompilationHostService();
        var check = service.Check(new PowerShellCompilationCheckRequest(fixture.ScriptPath)
        {
            Mode = input.Mode,
            OutputDirectory = output
        });
        Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(issue => issue.Message)));
        var build = service.Compile(new PowerShellCompilationBuildSpec(fixture.ScriptPath, output, "LoaderChildCall", input.Kind, input.Mode)
        {
            CompilationSourcePaths = input.CompilationSourceFiles,
            RuntimeSourcePaths = input.SourceFiles
        }, check.ProposedReview!);
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.Equal(2, build.Manifest!.UnitDispositionLedger!.Entries.Count(entry =>
            entry.EmittedClrMethod && entry.Name is "Get-EntryTag" or "Get-LoaderTag"));

        var packaged = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var original = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }, string.Empty);
            Assert.Equal(0, original.Code);
            Assert.Empty(original.Error);
            Assert.Equal("False|False\r\nloader|entry", original.Output.TrimEnd());
            Assert.Equal(original.Code, packaged.Code);
            Assert.Equal(original.Error, packaged.Error);
            Assert.Equal(original.Output, packaged.Output);
        }
    }
}
