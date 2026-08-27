using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictBinaryModuleRecordsExactCommandProviderAndAdapterContracts()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-SelectedValue { [CmdletBinding()] param([object[]] $InputObject) " +
            "$InputObject | Where-Object { $_ -ne $null } | Select-Object -First 1 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "CommandProviderProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var manifest = Assert.IsType<PowerShellCompilationArtifactManifest>(result.Manifest);
        Assert.True(manifest.RequiresPowerShellRuntime);
        Assert.Equal(
            new[]
            {
                "powerforge.command.filtering.where-object",
                "powerforge.command.projection.select-object"
            },
            manifest.CommandProviders.Select(static provider => provider.ProviderId));
        Assert.All(manifest.CommandProviders, provider =>
        {
            Assert.Equal(1, provider.SchemaVersion);
            Assert.True(provider.CompileTimeOnly);
            Assert.False(provider.Adapter.RuntimeFree);
            Assert.Contains("System.Management.Automation", provider.Adapter.Dependencies);
        });
    }
}
