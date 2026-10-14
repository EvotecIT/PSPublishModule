using System.Management.Automation.Language;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [WindowsOrLinuxX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_PackageConsolePreservesFormattingStreamsAndAuthoredErrors()
        => AssertPackagedConsolePresentation(PowerShellCompilationMode.Package);

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_NativeHybridConsolePreservesFormattingStreamsAndAuthoredErrors()
        => AssertPackagedConsolePresentation(PowerShellCompilationMode.Hybrid);

    private static void AssertPackagedConsolePresentation(PowerShellCompilationMode mode)
    {
        // Native script-root admission currently excludes authored redirections and explicit exit. Package keeps
        // those controls; both routes still build one artifact and require their actual contract.
        var packageOnlyBranches = mode == PowerShellCompilationMode.Package ? """
            }elseif($Case -eq 'Redirection'){
                Write-Error 'owned hidden failure' -ErrorAction Continue 2>$null
                Write-Error 'owned merged failure' -ErrorAction Continue 2>&1 | ForEach-Object { "merged:$($_.Exception.Message)" }
                Write-Output 'after-redirection'
            }elseif($Case -eq 'Exit'){
                Write-Output 'before-exit'
                exit 7
                Write-Output 'after-exit'
            """ + "\n" : string.Empty;
        using var fixture = ArtifactFixture.Create($$"""
            param([string]$Case='Table',[Alias('n')][string]$Name='default',[Alias('b')][switch]$Bare,[string]$MissingPath)
            function Invoke-OwnedFailure { throw 'owned console failure' }
            if($Case -eq 'Table'){
                [pscustomobject]@{MatchKind='missing';Count=2},[pscustomobject]@{MatchKind='root-to-blog';Count=1}|Format-Table -AutoSize|Out-Host
            }elseif($Case -eq 'List'){
                [pscustomobject]@{Name='sample';LongValue=('long-value-'*25);Count=2;Status='good';More='last'}
            }elseif($Case -eq 'Streams'){
                Write-Host 'host-' -NoNewline
                Write-Output 'success'
                Write-Host 'host-line'
                Write-Host "embedded`nline"
                Write-Warning 'owned-warning'
                Write-Verbose 'owned-verbose' -Verbose
                Write-Debug 'owned-debug' -Debug
                Write-Information 'owned-hidden'
                Write-Information 'owned-visible' -InformationAction Continue
                Write-Output 'after'
            }elseif($Case -eq 'Binding'){
                "$Name|$Bare"
            }elseif($Case -eq 'NonTerm'){
                Write-Output 'before-error'
                Write-Error 'owned nonterminating failure' -ErrorAction Continue
                Write-Output 'after-error'
            }elseif($Case -eq 'NonTermCommand'){
                Write-Output 'before-command-error'
                Get-Content -LiteralPath $MissingPath -ErrorAction Continue
                Write-Output 'after-command-error'
            {{packageOnlyBranches}}}elseif($Case -eq 'Throw'){
                Invoke-OwnedFailure
            }else{
                $ErrorActionPreference='Stop'
                Get-Content -LiteralPath ([IO.Path]::Combine($PSScriptRoot,'owned-absent.txt'))
            }
            """, compactPath: true);
        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "Generated.Console" + mode, PowerShellCompilationArtifactKind.Executable, mode)
        {
            SingleFile = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, mode, "net10.0", rid,
                false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var built = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        if (mode == PowerShellCompilationMode.Hybrid)
        {
            var root = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,
                static entry => entry.Kind == PowerShellCompilationUnitKind.Script);
            Assert.True(root.Emitted);
            Assert.True(root.UsesNativeFunctionBinding);
            Assert.False(root.RetainedHostedSource);
        }
        var host = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH") ?? "pwsh";
        var environment = new Dictionary<string, string> { ["NO_COLOR"] = "1" };
        var missingPath = Path.Combine(fixture.RootPath, "owned-absent.txt");
        Assert.False(File.Exists(missingPath));
        var scenarios = mode == PowerShellCompilationMode.Package
            ? new[] { "Table", "List", "Streams", "NonTerm", "NonTermCommand", "Redirection", "Exit", "Throw", "Command" }
            : new[] { "Table", "List", "Streams", "NonTerm", "NonTermCommand", "Throw", "Command" };
        foreach (var scenario in scenarios)
        {
            var arguments = new[] { "-Case", scenario, "-MissingPath", missingPath };
            var original = RunProcess(host, environment,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }.Concat(arguments).ToArray());
            var generated = RunProcess(built.ArtifactPath!, environment, arguments);
            var detail = $"Mode={mode}; Case={scenario}; Original stdout: {original.StandardOutput}; stderr: {original.StandardError}; Generated stdout: {generated.StandardOutput}; stderr: {generated.StandardError}";
            Assert.True(original.ExitCode == generated.ExitCode, detail);
            if (scenario is "Table" or "List" or "Streams")
            {
                Assert.Equal(0, original.ExitCode);
                Assert.True(original.StandardOutput == generated.StandardOutput, detail);
                Assert.Empty(original.StandardError);
                Assert.Empty(generated.StandardError);
                Assert.Contains(scenario == "Table" ? "root-to-blog" : scenario == "List" ? "LongValue" : "host-success", generated.StandardOutput);
            }
            else if (scenario is "NonTerm" or "NonTermCommand")
            {
                Assert.Equal(0, original.ExitCode);
                var expectedOutput = scenario == "NonTerm"
                    ? "before-error" + Environment.NewLine + "after-error" + Environment.NewLine
                    : "before-command-error" + Environment.NewLine + "after-command-error" + Environment.NewLine;
                Assert.Equal(expectedOutput, original.StandardOutput);
                Assert.True(original.StandardOutput == generated.StandardOutput, detail);
                Assert.NotEmpty(original.StandardError);
                Assert.True(original.StandardError == generated.StandardError, detail);
                Assert.Contains(scenario == "NonTerm" ? "owned nonterminating failure" : "Cannot find path", generated.StandardError);
                if (scenario == "NonTermCommand")
                {
                    var ast = Parser.ParseFile(fixture.ScriptPath, out _, out _);
                    var authoredLine = ast.Find(static node => node is CommandAst command &&
                        command.GetCommandName() == "Get-Content" &&
                        command.CommandElements.OfType<VariableExpressionAst>().Any(static variable =>
                            variable.VariablePath.UserPath == "MissingPath"), true)!.Extent.StartLineNumber;
                    Assert.Contains(Path.GetFileName(fixture.ScriptPath) + ":" + authoredLine, original.StandardError);
                }
            }
            else if (scenario is "Redirection" or "Exit")
            {
                Assert.Equal(scenario == "Exit" ? 7 : 0, original.ExitCode);
                var expectedOutput = scenario == "Exit"
                    ? "before-exit" + Environment.NewLine
                    : "merged:owned merged failure" + Environment.NewLine + "after-redirection" + Environment.NewLine;
                Assert.Equal(expectedOutput, original.StandardOutput);
                Assert.True(original.StandardOutput == generated.StandardOutput, detail);
                Assert.Empty(original.StandardError);
                Assert.Empty(generated.StandardError);
            }
            else
            {
                Assert.Equal(1, original.ExitCode);
                Assert.Empty(original.StandardOutput);
                Assert.Empty(generated.StandardOutput);
                var ast = Parser.ParseFile(fixture.ScriptPath, out _, out _);
                var authoredLine = scenario == "Throw"
                    ? ast.Find(static node => node is ThrowStatementAst, true)!.Extent.StartLineNumber
                    : ast.Find(static node => node is CommandAst command && command.GetCommandName() == "Get-Content" &&
                        command.Extent.Text.Contains("'owned-absent.txt'", StringComparison.Ordinal), true)!.Extent.StartLineNumber;
                var location = Path.GetFileName(fixture.ScriptPath) + ":" + authoredLine;
                Assert.Contains(location, original.StandardError);
                Assert.True(generated.StandardError.Contains(location, StringComparison.Ordinal), detail);
                Assert.Contains("Line |", generated.StandardError);
                var originalPosition = original.StandardError[original.StandardError.IndexOf("Line |", StringComparison.Ordinal)..]
                    .Split(Environment.NewLine).Take(3);
                var generatedPosition = generated.StandardError[generated.StandardError.IndexOf("Line |", StringComparison.Ordinal)..]
                    .Split(Environment.NewLine).Take(3);
                Assert.Equal(originalPosition, generatedPosition);
                Assert.Contains(scenario == "Throw" ? "owned console failure" : "Cannot find path", generated.StandardError);
                Assert.DoesNotContain("PowerForge.Compiled.Program", generated.StandardError);
            }
        }
        foreach (var arguments in new[]
                 {
                     new[] { "-Case", "Binding" },
                     new[] { "-Case", "Binding", "-n", "Ada", "-b" },
                     new[] { "-Case", "Binding", "-Name", "Ada", "-Bare:false" }
                 })
        {
            var original = RunProcess(host, environment,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }.Concat(arguments).ToArray());
            var generated = RunProcess(built.ArtifactPath!, environment, arguments);
            Assert.Equal(0, original.ExitCode);
            Assert.Empty(original.StandardError);
            Assert.Equal((0, original.StandardOutput, string.Empty),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
        }
    }
}
