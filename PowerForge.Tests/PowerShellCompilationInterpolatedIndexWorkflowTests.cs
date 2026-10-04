using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void PinnedFlatObject_PreservesRecursiveInterpolatedIndexAssignments(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "ConvertTo-FlatObject.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.FlatObject", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries).Emitted);
        const string probe = """
            $samples=@(
                [pscustomobject]@{Name='one';Nested=[pscustomobject]@{Value=3}},
                [ordered]@{Items=@([pscustomobject]@{Name='first'},[pscustomobject]@{Name='second'});Empty=@{}},
                [pscustomobject]@{Items=@(1,2);Skip='hidden';Null=$null}
            )
            foreach($sample in $samples) {
                foreach($depth in 0,1,5) {
                    $value=@(ConvertTo-FlatObject -Objects $sample -Depth $depth -ExcludeProperty Skip)
                    [pscustomobject]@{depth=$depth;count=$value.Count;values=$value} | ConvertTo-Json -Compress -Depth 12
                }
            }
            @( $samples | ConvertTo-FlatObject -Separator '/' -Base 0 ) | ConvertTo-Json -Compress -Depth 12
            """;
        Assert.Equal(RunModuleProof(fixture.ScriptPath, probe, host), RunModuleProof(result.ArtifactPath!, probe, host));
    }
}
