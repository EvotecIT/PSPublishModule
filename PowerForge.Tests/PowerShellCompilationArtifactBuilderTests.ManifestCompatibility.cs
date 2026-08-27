using System;
using System.IO;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net472", "5.1", "Desktop")]
    [InlineData("net8.0", "7.4", "Core")]
    [InlineData("net10.0", "7.6", "Core")]
    public void Build_BinaryModuleAlignsManifestCompatibilityWithTargetFramework(
        string targetFramework,
        string expectedPowerShellVersion,
        string expectedEdition)
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; PowerShellVersion = '5.1'; CompatiblePSEditions = @('Desktop', 'Core'); FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @() }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ManifestCompatibility",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(expectedPowerShellVersion, ModuleManifestValueReader.ReadTopLevelString(result.ArtifactPath!, "PowerShellVersion"));
        Assert.Equal(new[] { expectedEdition }, ModuleManifestValueReader.ReadTopLevelStringOrArray(result.ArtifactPath!, "CompatiblePSEditions"));
    }

    [Fact]
    public void Build_BinaryModuleRejectsSourceEditionIncompatibleWithTargetFramework()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; PowerShellVersion = '5.1'; CompatiblePSEditions = @('Desktop'); FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @() }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.IncompatibleManifestEdition",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("CompatiblePSEditions", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_CoreBinaryModulePreservesHigherSourcePowerShellRequirement()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; PowerShellVersion = '7.5'; CompatiblePSEditions = @('Core'); FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @() }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HigherSourcePowerShellVersion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("7.5", ModuleManifestValueReader.ReadTopLevelString(result.ArtifactPath!, "PowerShellVersion"));
        Assert.Equal(new[] { "Core" }, ModuleManifestValueReader.ReadTopLevelStringOrArray(result.ArtifactPath!, "CompatiblePSEditions"));
    }
}
