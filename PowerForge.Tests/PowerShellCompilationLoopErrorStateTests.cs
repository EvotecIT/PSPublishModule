using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void LoopStopping_InterruptOnlyLoopsPreserveReadOnlyErrorState(string framework, string host)
    {
        const string source = """
            function Invoke-Items { [CmdletBinding()] param([int[]]$Values) foreach($item in $Values) {} return 1 }
            function Read-Direct { [CmdletBinding()] param([int[]]$Values) foreach($item in $Values) {} return $Error.Count }
            function Read-Indirect { [CmdletBinding()] param([int[]]$Values) $unused=Invoke-Items -Values $Values; return $Error.Count }
            function Read-Transitive { [CmdletBinding()] param([int[]]$Values) return Read-Indirect -Values $Values }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.LoopErrorState", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            $Error.Clear()
            try { throw 'seed error' } catch {}
            foreach($name in 'Read-Direct','Read-Indirect','Read-Transitive') {
                foreach($shape in 'Null','Empty','Values') {
                    $values=if($shape -eq 'Null') { $null } elseif($shape -eq 'Empty') { ,([int[]]@()) } else { ,([int[]]@(1,2)) }
                    $records=@(& $name -Values $values -ErrorAction Stop)
                    [pscustomobject]@{name=$name;shape=$shape;records=$records;errors=$Error.Count} | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-loop-error-state");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-loop-error-state");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("\"errors\":1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
