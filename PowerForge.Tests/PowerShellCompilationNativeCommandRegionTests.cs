using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandRegions_CompileInterleavedPipelinesAndPrivateLocalCalls(string framework, string host)
    {
        const string source = """
            function Invoke-NativeRegionWorkflow {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Values)
                $Seen='before'
                Write-Output "begin:$Seed"
                $Seen='middle'
                for($index=0;$index -lt $Values.Length;$index++) {
                    Read-NativeRegionHelper -Number $Values[$index]
                }
                Write-Output "end:$Seen"
                "state:$Seen"
            }
            function Read-NativeRegionHelper {
                param([int]$Number)
                "helper:$Seen"
                Read-NativeRegionLeaf -Number $Number
            }
            function Read-NativeRegionLeaf {
                param([int]$Number)
                "leaf:$Seen"
                return $Number * 2
            }
            Export-ModuleMember -Function Invoke-NativeRegionWorkflow
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeCommandRegions", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(item => item.DiagnosticChain.Select(cause => item.Name + ": " + cause.Message))));
        var unit = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, item => item.Name == "Invoke-NativeRegionWorkflow");
        Assert.True(unit.EmittedClrMethod);
        Assert.False(unit.RetainedHostedSource);
        Assert.Equal(3, unit.RuntimeCommandRegions);
        var helper = Assert.Single(result.Manifest.UnitDispositionLedger.Entries, item => item.Name == "Read-NativeRegionHelper");
        Assert.True(helper.EmittedClrMethod);
        Assert.False(helper.RetainedHostedSource);
        var leaf = Assert.Single(result.Manifest.UnitDispositionLedger.Entries, item => item.Name == "Read-NativeRegionLeaf");
        Assert.True(leaf.EmittedClrMethod);
        Assert.False(leaf.RetainedHostedSource);
        const string probe = """
            foreach($values in @(@{value=@()},@{value=@(3)},@{value=@(1,2,3)},@{value=@(1,'bad',3)})) {
                foreach($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                    foreach($first in $false,$true) {
                        $Error.Clear(); $records=@(); $emitted=@(); $caught=$null
                        try {
                            if($first) { $records=@(Invoke-NativeRegionWorkflow -Values $values.value -ErrorAction $preference -OutVariable emitted 2>$null | Select-Object -First 2) }
                            else { $records=@(Invoke-NativeRegionWorkflow -Values $values.value -ErrorAction $preference -OutVariable emitted 2>$null) }
                        } catch { $caught=$_.FullyQualifiedErrorId }
                        [pscustomobject]@{records=$records;emitted=@($emitted);caught=$caught;errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId })} | ConvertTo-Json -Compress -Depth 12
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-region-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-region-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(32, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(6).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
