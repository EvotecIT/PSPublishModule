using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(InterpolationScalarHosts))]
    public void Interpolation_PreservesClosedNumericPromotion(string framework, string host, bool library)
    {
        const string source = """
            function Expand-Promoted {
                [CmdletBinding()] param([int]$Start,[int]$Count)
                $value=$Start
                for([int]$index=0;$index -lt $Count;$index++) { $value++ }
                return "number=$value;again=${value}"
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.InterpolationPromotion",
            library ? PowerShellCompilationArtifactKind.Library : PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        if (library)
        {
            Assert.False(result.Manifest.RequiresPowerShellRuntime);
            Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
        }
        const string probe = """
            foreach($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=$culture
                foreach($start in 0,-1,[int]::MinValue,[int]::MaxValue) {
                    foreach($count in 0,1,3) { Invoke-Case $start $count }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case([int]$start,[int]$count) { Expand-Promoted $start $count }; " + probe,
            fixture.RootPath, "original-interpolation-promotion");
        var setup = library
            ? "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) + "'); " +
              "$type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Expand_Promoted') }; " +
              "function Invoke-Case([int]$start,[int]$count) { $type.GetMethod('Expand_Promoted').Invoke($null,[object[]]@($start,$count)) }; "
            : "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; function Invoke-Case([int]$start,[int]$count) { Expand-Promoted $start $count }; ";
        var compiled = RunStatementErrorProbe(host, setup + probe, fixture.RootPath, "compiled-interpolation-promotion");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Empty(compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
