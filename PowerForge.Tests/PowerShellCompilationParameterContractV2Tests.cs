using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void Analyze_BinaryModuleClassifiesUntypedParameterAsHostObjectContract(string targetFramework)
    {
        using var fixture = ArtifactFixture.Create(
            "function Copy-Value { param([AllowNull()] $Value) return $Value }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: targetFramework,
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var parameter = Assert.Single(unit.Parameters);
        Assert.Equal(typeof(object).FullName, parameter.TypeName);
        Assert.True(parameter.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ClrMethod));
        Assert.True(parameter.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.PowerShellHost));
        Assert.False(parameter.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ProcessArgument));
        Assert.True(PowerShellCompilationCapabilities.BinaryModule.HasFlag(PowerShellCompilationCapability.UntypedObjectParameters));
        Assert.False(PowerShellCompilationCapabilities.TypedExecutable.HasFlag(PowerShellCompilationCapability.UntypedObjectParameters));
    }

    [Fact]
    public void Analyze_TypedExecutableRejectsUntypedObjectParameter()
    {
        using var fixture = ArtifactFixture.Create("param($Value) return $Value");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterType);
    }

    [Fact]
    public void Analyze_BinaryModuleDoesNotTreatUntypedObjectAsDynamicMemberContract()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Name { param($Value) return $Value.Name }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.DoesNotContain(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterType);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ForSyntax("MemberExpressionAst"));
    }

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

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesUntypedObjectParameterBehavior(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(
            "function Copy-UntypedValue { [CmdletBinding()] param([Parameter(Position=0)][AllowNull()] $Value) return $Value }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.UntypedObjectParameter",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true,
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string proof =
            "$parameter = (Get-Command Copy-UntypedValue).Parameters['Value']; $parameter.ParameterType.FullName; " +
            "$scalar = Copy-UntypedValue -Value 42; $scalar.GetType().FullName; $scalar; " +
            "@(Copy-UntypedValue -Value $null).Count; " +
            "@(Copy-UntypedValue -Value @('a','b')).Count; " +
            "(Copy-UntypedValue -Value ([pscustomobject]@{ Name = 'Ada' })).Name";
        var original = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(original, compiled);
        Assert.Equal(
            new[] { "System.Object", "System.Int32", "42", "1", "2", "Ada" },
            compiled.Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("Copy_UntypedValue(object Value)", generated, StringComparison.Ordinal);
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
    public void Analyze_LocalCallBindsExplicitPositionsInNumericOrder()
    {
        var document = PowerShellSourceParser.Parse(
            "function Join-Position { [CmdletBinding(PositionalBinding=$false)] " +
            "param([Parameter(Position=5)][string] $Five, [Parameter(Position=2)][string] $Two, [string] $NamedOnly) " +
            "return \"$Two|$Five|$NamedOnly\" }; " +
            "function Get-Position { return Join-Position 'two' -NamedOnly 'named' 'five' }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "explicit-position-local-call.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var caller = Assert.Single(result.Analyzed.Functions, static function => function.Symbol.Name == "Get-Position");
        var invocation = Assert.IsType<PowerShellBoundInvocationExpression>(
            Assert.IsType<PowerShellBoundReturnStatement>(Assert.Single(caller.Body.Statements)).Expression);
        Assert.Equal(new[] { 1, 2, 0 }, invocation.AuthoredEvaluationOrder);
        Assert.Equal(new[] { "Five", "NamedOnly", "Two" }, invocation.BoundParameterNames);
    }

    [Fact]
    public void Analyze_LocalCallRejectsUnpositionedParameterBesideExplicitPositions()
    {
        var document = PowerShellSourceParser.Parse(
            "function Join-Position { param([string] $NamedOnly, [Parameter(Position=1)][string] $Second) return \"$NamedOnly|$Second\" }; " +
            "function Get-Position { return Join-Position 'second' 'surplus' }",
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "unpositioned-local-call.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Get_Position");
        Assert.Contains(result.Emitted.Diagnostics, static diagnostic =>
            diagnostic.Code == "PSB2806" &&
            diagnostic.Message.Contains("too many positional arguments", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_StrictLibraryExecutesExplicitPositionLocalCallWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "function Join-Position { [CmdletBinding(PositionalBinding=$false)] " +
            "param([Parameter(Position=5)][string] $Five, [Parameter(Position=2)][string] $Two, [string] $NamedOnly) " +
            "return \"$Two|$Five|$NamedOnly\" }; " +
            "function Get-Position { return Join-Position 'two' -NamedOnly 'named' 'five' }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ExplicitPositionLocalCall",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        Assert.DoesNotContain(result.Manifest.Dependencies, static dependency =>
            dependency.Name.Contains("System.Management.Automation", StringComparison.OrdinalIgnoreCase));
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Get_Position");
        Assert.Equal("two|five|named", method.Invoke(null, null));
    }

    [Fact]
    public void Build_StrictLibraryEmitsReorderedVoidLocalCallWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-Position { param([Parameter(Position=1)][string] $Second, [Parameter(Position=0)][string] $First) }; " +
            "function Get-Position { Set-Position 'first' 'second'; return 42 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ExplicitPositionVoidLocalCall",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Get_Position");
        Assert.Equal(42, method.Invoke(null, null));
    }

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridModulePreservesExplicitPositionLocalCallAcrossHosts(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(
            "function Join-Position { [CmdletBinding(PositionalBinding=$false)] " +
            "param([Parameter(Position=5)][string] $Five, [Parameter(Position=2)][string] $Two, [string] $NamedOnly) " +
            "return \"$Two|$Five|$NamedOnly\" }; " +
            "function Get-Position { return Join-Position 'two' -NamedOnly 'named' 'five' }; " +
            "function Get-FallbackValue { return [int](Get-Date -Format yyyy) }; " +
            "Export-ModuleMember -Function Get-Position, Get-FallbackValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ExplicitPositionHybrid",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(2, result.Manifest.RuntimeFallbackUnits);
        const string proof = "Get-Position; [int](Get-FallbackValue) -gt 2000";
        var original = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(original, compiled);
        Assert.Equal(new[] { "two|five|named", "True" }, compiled.Split(Environment.NewLine));
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
