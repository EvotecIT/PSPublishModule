using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void NativeIndexedConditional_DoesNotWidenRuntimeFreeAdmission()
    {
        var document = PowerShellSourceParser.Parse(
            "function Set-Conditional { param([object] $Map) $Map['value'] = if ($true) { 1 } }",
            Path.Combine(Path.GetTempPath(), "indexed-conditional-strict.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeIndexedConditional_PreservesCaptureAndTargetEvaluation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-IndexedConditional {
                [CmdletBinding()] param([string] $Mode)
                $map = [ordered]@{before='before';after='after'}
                $key = 'before'
                try {
                    $map[$key] = if ($Mode -eq 'empty') { }
                        elseif ($Mode -eq 'null') { $null }
                        elseif ($Mode -eq 'many') { 'one'; 'two' }
                        elseif ($Mode -eq 'key') { $key='after'; 'changed-key' }
                        elseif ($Mode -eq 'receiver') { $map=[ordered]@{before='new-before';after='new-after'}; 'changed-map' }
                        elseif ($Mode -eq 'fail') { 'pending'; throw 'rhs-failed' }
                        elseif ($Mode -eq 'return') { 'pending'; return 'returned' }
                        else { 'one' }
                } catch { 'caught:' + $_.Exception.Message }
                foreach($entry in $map.GetEnumerator()) {
                    [pscustomobject]@{key=$entry.Key;values=@($entry.Value);null=$null -eq $entry.Value;type=if($null -eq $entry.Value){$null}else{$entry.Value.GetType().FullName}}
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.IndexedConditional",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($mode in 'empty','null','one','many','key','receiver','fail','return') {
                [pscustomobject]@{mode=$mode;values=@(Get-IndexedConditional -Mode $mode)} | ConvertTo-Json -Compress -Depth 8
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("changed-key", generated);
        Assert.Contains("rhs-failed", generated);
    }
}
