using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Analyze_ClassifiesHostTypesAndPreservesParameterSetBindings()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Contract { [CmdletBinding(DefaultParameterSetName='Name', PositionalBinding=$false)] " +
            "param([Parameter(ParameterSetName='Name', Mandatory, Position=2, ValueFromPipelineByPropertyName, HelpMessage='A name')] " +
            "[AllowEmptyString()] [SupportsWildcards()] [string] $Name, " +
            "[Parameter(ParameterSetName='Credential', Mandatory)] [PSCredential] $Credential) return $Name }",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var name = unit.Parameters[0];
        var binding = Assert.Single(name.Bindings);
        Assert.Equal("Name", binding.ParameterSetName);
        Assert.True(binding.Mandatory);
        Assert.Equal(2, binding.Position);
        Assert.True(binding.ValueFromPipelineByPropertyName);
        Assert.Equal("A name", binding.HelpMessage);
        Assert.True(name.AllowEmptyString);
        Assert.True(name.SupportsWildcards);
        var credential = unit.Parameters[1];
        Assert.True(credential.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.PowerShellHost));
        Assert.False(credential.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ProcessArgument));
    }

    [Fact]
    public void Analyze_RejectsPowerShellOnlyBindingAndHostTypesWithoutHostCapability()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Contract { param([Parameter(ValueFromPipeline)][PSCredential] $Credential) return $Credential }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0"));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterType);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterMetadata);
    }

    [Fact]
    public void Build_BinaryModulePreservesParameterSetsPositionsAndImplicitEndPipelineSemantics()
    {
        using var fixture = ArtifactFixture.Create(
            "function Select-ContractValue { [CmdletBinding(DefaultParameterSetName='Value', PositionalBinding=$false)] " +
            "param([Parameter(ParameterSetName='Value', Mandatory, Position=2, ValueFromPipeline)] " +
            "[AllowEmptyString()] [string] $Value) return $Value }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ParameterContractV2",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(
            result.ArtifactPath!,
            "$command = Get-Command Select-ContractValue; " +
            "$parameter = $command.Parameters['Value']; " +
            "$attribute = $parameter.Attributes | Where-Object { $_ -is [Management.Automation.ParameterAttribute] }; " +
            "$attribute.ParameterSetName; $attribute.Position; $attribute.ValueFromPipeline; @('first','last') | Select-ContractValue");
        Assert.Equal(new[] { "Value", "2", "True", "last" }, output.Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledCmdlets.cs"));
        Assert.Contains("protected override void EndProcessing()", generated, StringComparison.Ordinal);
        Assert.Contains("[AllowEmptyString]", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_BinaryModuleAcceptsConservativePowerShellHostParameterTypes()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedCredential { param([PSCredential] $Credential) return $Credential }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HostParameterType",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(
            "user",
            RunModuleProof(
                result.ArtifactPath!,
                "$secret = ConvertTo-SecureString x -AsPlainText -Force; " +
                "$credential = [PSCredential]::new('user', $secret); " +
                "(Get-TypedCredential -Credential $credential).UserName"));
    }

    [Fact]
    public void Build_StrictExecutableBindsExplicitPositionAndGuid()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding(PositionalBinding=$false)] param([Parameter(Position=2)][Guid] $Id) return $Id.ToString()");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.GuidArgument",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string id = "2f7d93d8-8723-4ced-8f5f-86b155df3a10";
        var accepted = RunProcess(result.ArtifactPath!, id);
        Assert.Equal((0, id), (accepted.ExitCode, accepted.StandardOutput.Trim()));
    }

    [Fact]
    public void Build_StrictExecutableKeepsUnpositionedParametersNamedOnlyBesideExplicitPositions()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding()] param([string] $First, [Parameter(Position=1)][string] $Second); return \"$First|$Second\"");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MixedPositionContract",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var accepted = RunProcess(result.ArtifactPath!, "Ada");
        var surplus = RunProcess(result.ArtifactPath!, "Ada", "Grace");

        Assert.Equal((0, "|Ada", string.Empty),
            (accepted.ExitCode, accepted.StandardOutput.Trim(), accepted.StandardError.Trim()));
        Assert.NotEqual(0, surplus.ExitCode);
        Assert.Contains("Unexpected positional argument", surplus.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transpile_KeepsNamedParameterSetSelectionOutOfTypedLocalCalls()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ByContract { [CmdletBinding(DefaultParameterSetName='Name')] " +
            "param([Parameter(ParameterSetName='Name', Mandatory)][string] $Name) return $Name }; " +
            "function Get-ContractCaller { return Get-ByContract -Name 'value' }",
            ".psm1");

        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.ParameterSetGraph",
            "CompiledPowerShell",
            "net10.0");

        Assert.Contains(result.Methods, static method => method.SourceName == "Get-ByContract");
        Assert.DoesNotContain(result.Methods, static method => method.SourceName == "Get-ContractCaller");
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("parameter sets", StringComparison.OrdinalIgnoreCase));
    }
}
