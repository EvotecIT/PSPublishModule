using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("Get-ChildItem")]
    [InlineData("1..3 | Group-Object -AsHashTable")]
    public void Explain_HybridNativeScriptRootRoutesLiteralCommandRegionsWithoutNameWhitelist(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        var root = Assert.Single(Assert.Single(PowerShellCompilationExplainShaper
            .CreateFinalExplanation(input, plan, "net10.0").Files).Units);

        Assert.True(root.Emitted);
        Assert.False(root.RetainedHostedSource);
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridNativeScriptRootPreservesGenericCommandRegionsAndCallerBoundary()
    {
        using var fixture = ArtifactFixture.Create("""
            param([string] $Case = 'Lookup')
            function Select-Object {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][int] $Value, [int] $First)
                process { "shadow:$($Value):$First" }
            }
            $ErrorActionPreference = 'Stop'
            if ($Case -eq 'Lookup') {
                1..3 | Select-Object -First 2
            } elseif ($Case -eq 'Lifecycle') {
                $script:sum = 0
                1..3 | ForEach-Object -Begin { 'begin' } -Process { $script:sum += $_; "process:$_" } -End { "end:$script:sum" }
                Write-Output "after:$script:sum"
            } elseif ($Case -eq 'Capture') {
                $outer = 10
                $script:seen = 0
                $values = 1..3 | Microsoft.PowerShell.Utility\Select-Object -Property @{Name='Value';Expression={ $script:seen++; [int]($_ + $outer) }} -OutVariable selected
                foreach ($item in $values) { Write-Output "value:$($item.Value):$($item.Value.GetType().FullName)" }
                Write-Output "selected:$($selected.Count):seen:$script:seen"
            } elseif ($Case -eq 'Return') {
                1..3 | ForEach-Object { if ($_ -eq 2) { return }; $_ }
                Write-Output 'after:return'
            } elseif ($Case -eq 'Break') {
                foreach ($iteration in 1..2) {
                    1..3 | ForEach-Object { "break:$_"; if ($_ -eq 2) { break } }
                    Write-Output "outer:$iteration"
                }
                Write-Output 'after:break'
            } elseif ($Case -eq 'Throw') {
                try { 1..3 | ForEach-Object { if ($_ -eq 2) { throw [InvalidOperationException]::new('owned stop') }; "throw:$_" } }
                catch { Write-Output "caught:$($_.Exception.Message)" }
                finally { Write-Output 'finally:throw' }
                Write-Output 'after:throw'
            } elseif ($Case -eq 'Parallel') {
                $parallel = 1..3 | ForEach-Object -Parallel { $_ * 2 } -ThrottleLimit 2 -TimeoutSeconds 10
                Write-Output "parallel:$(($parallel | Sort-Object) -join ',')"
            } elseif ($Case -eq 'Caller') {
                try {
                    $visible = Get-Variable -Scope 1 -Name Entry,Values,Invocation -ErrorAction SilentlyContinue | Microsoft.PowerShell.Utility\Select-Object -ExpandProperty Name | Sort-Object
                    Write-Output "caller:$($visible -join ',')"
                } catch {
                    Write-Output ('caller-error:{0}:{1}:{2}:{3}' -f $_.Exception.GetType().FullName,$_.FullyQualifiedErrorId,$_.CategoryInfo.Category,$_.InvocationInfo.ScriptLineNumber)
                    throw
                }
            }
            """, compactPath: true);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.NativeCommandFamilies", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            EmitSource = true, SingleFile = false, SelfContained = false,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.All(result.Manifest!.UnitDispositionLedger!.Entries, static unit =>
        {
            Assert.True(unit.Emitted);
            Assert.False(unit.RetainedHostedSource);
        });
        Assert.Contains("InvokeCommandRegion", File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellEntry.cs")));

        foreach (var (name, expected) in new (string Name, string[]? Expected)[]
        {
            ("Lookup", new[] { "shadow:1:2", "shadow:2:2", "shadow:3:2" }),
            ("Lifecycle", new[] { "begin", "process:1", "process:2", "process:3", "end:6", "after:6" }),
            ("Capture", new[] { "value:11:System.Int32", "value:12:System.Int32", "value:13:System.Int32", "selected:3:seen:3" }),
            ("Return", new[] { "1", "3", "after:return" }),
            ("Break", null),
            ("Throw", new[] { "throw:1", "caught:owned stop", "finally:throw", "after:throw" }),
            ("Parallel", new[] { "parallel:2,4,6" }),
            ("Caller", null)
        })
        {
            var original = RunProcess("pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, "-Case", name);
            var generated = RunProcess(result.ArtifactPath!, "-Case", name);
            if (name == "Caller")
            {
                Assert.Equal(1, original.ExitCode);
                Assert.Equal(original.ExitCode, generated.ExitCode);
                Assert.Equal(original.StandardOutput, generated.StandardOutput);
                Assert.Contains("caller-error:", original.StandardOutput, StringComparison.Ordinal);
                Assert.Contains("scope", original.StandardError, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("scope", generated.StandardError, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("caller:Entry", generated.StandardOutput, StringComparison.Ordinal);
                continue;
            }
            Assert.True(original.ExitCode == 0, name + Environment.NewLine + original.StandardError);
            Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
            if (expected is not null)
                Assert.Equal(expected, generated.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }
}
