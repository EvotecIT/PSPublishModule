using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$items = (0..2).ForEach({ New-Object object[] 2 }); $items")]
    [InlineData("[object[]] $items = @(Get-Process); $items")]
    [InlineData("Get-Process | Select-Object -First 1")]
    public void Build_RuntimeFreeLibraryRejectsHostedCommandRegions(string body)
    {
        using var fixture = ArtifactFixture.Create("function Get-HostedValue { " + body + " }");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);
        Assert.Empty(typed.Methods);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NoHostedLibrary",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.False(result.Succeeded);
        Assert.Null(result.ArtifactPath);
    }
}
