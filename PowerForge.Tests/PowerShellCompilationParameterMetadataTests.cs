using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Analyze_PreservesSwitchAliasAllowNullAndValidationMetadata()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Proof { param([Alias('n')] [AllowNull()] [ValidateSet('Alpha', 'Beta')] [string] $Name, [switch] $Force) if ($Force) { return $Name + '!' }; return $Name }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var name = unit.Parameters[0];
        Assert.Equal(new[] { "n" }, name.Aliases);
        Assert.True(name.AllowNull);
        Assert.Equal(PowerShellCompilationValidationKind.Set, Assert.Single(name.Validations).Kind);
        var force = unit.Parameters[1];
        Assert.True(force.IsSwitch);
        Assert.Equal(typeof(bool).FullName, force.TypeName);
    }

    [Fact]
    public void Build_StrictTypedExecutableBindsAliasAndSwitchAndEnforcesValidateSet()
    {
        using var fixture = ArtifactFixture.Create(
            "param([Alias('n')] [ValidateSet('Alpha', 'Beta')] [string] $Name, [switch] $Force) if ($Force) { return $Name + '!' }; return $Name");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedMetadataProof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var accepted = RunProcess(result.ArtifactPath!, "-n", "Alpha", "-Force");
        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal("Alpha!", accepted.StandardOutput.Trim());

        var rejected = RunProcess(result.ArtifactPath!, "-n", "Gamma");
        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("Allowed values", rejected.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StrictTypedExecutableAcceptsPowerShellColonSwitchValues()
    {
        using var fixture = ArtifactFixture.Create(
            "param([switch] $Force); if ($Force) { return $true }; return $false");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedColonSwitch",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var disabled = RunProcess(result.ArtifactPath!, "-Force:$false");
        var enabled = RunProcess(result.ArtifactPath!, "-Force:$true");

        Assert.Equal((0, "False", string.Empty),
            (disabled.ExitCode, disabled.StandardOutput.Trim(), disabled.StandardError.Trim()));
        Assert.Equal((0, "True", string.Empty),
            (enabled.ExitCode, enabled.StandardOutput.Trim(), enabled.StandardError.Trim()));
    }

    [Fact]
    public void Build_BinaryModuleEmitsPowerShellParameterAttributesAndSwitchAdapter()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-MetadataProof { [Alias('gmp')] param([Alias('n')] [AllowNull()] [ValidateNotNullOrEmpty()] [ValidatePattern('^[A-Z]')] [string] $Name, [switch] $Force) if ($Force) { return $Name + '!' }; return $Name }",
            ".psm1");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.BinaryMetadataProof",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var cmdletSourcePath = Directory.EnumerateFiles(result.GeneratedSourcePath!, "CompiledCmdlets.cs", SearchOption.AllDirectories).Single();
        var source = File.ReadAllText(cmdletSourcePath);
        Assert.Contains("[Alias(\"gmp\")]", source, StringComparison.Ordinal);
        Assert.Contains("[Alias(\"n\")]", source, StringComparison.Ordinal);
        Assert.Contains("[AllowNull]", source, StringComparison.Ordinal);
        Assert.Contains("[ValidateNotNullOrEmpty]", source, StringComparison.Ordinal);
        Assert.Contains("[ValidatePattern(\"^[A-Z]\")]", source, StringComparison.Ordinal);
        Assert.Contains("public SwitchParameter Force", source, StringComparison.Ordinal);
        Assert.Contains("Force.IsPresent", source, StringComparison.Ordinal);
    }
}
