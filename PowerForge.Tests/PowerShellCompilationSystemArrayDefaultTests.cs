using PowerForge;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Analyze_PreservesStableScalarDefaultsForSystemArrayParameters()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ArrayDefaults { param([array] $Names = 'All', [array] $Numbers = 42) return $Names.Count }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationSpec(
                fixture.ScriptPath,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapabilities.BinaryModule)).Files).Units);

        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Collection(
            unit.Parameters,
            parameter =>
            {
                var value = Assert.IsType<PowerShellCompilationLiteral>(parameter.DefaultValue);
                Assert.Equal(PowerShellCompilationLiteralKind.Array, value.Kind);
                Assert.Equal(typeof(Array).FullName, value.TypeName);
                var element = Assert.Single(value.Elements);
                Assert.Equal(PowerShellCompilationLiteralKind.String, element.Kind);
                Assert.Equal("All", element.Value);
            },
            parameter =>
            {
                var value = Assert.IsType<PowerShellCompilationLiteral>(parameter.DefaultValue);
                Assert.Equal(PowerShellCompilationLiteralKind.Array, value.Kind);
                var element = Assert.Single(value.Elements);
                Assert.Equal(PowerShellCompilationLiteralKind.SignedInteger, element.Kind);
                Assert.Equal("42", element.Value);
            });
        Assert.DoesNotContain(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterDefault);
    }

    [Fact]
    public void Analyze_RejectsSystemArrayDefaultsWhoseAuthoredRuntimeArrayTypeWouldBeLost()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ArrayDefault { param([array] $Values = [int[]](1, 2)) return $Values.Count }",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationSpec(
                fixture.ScriptPath,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapabilities.BinaryModule)).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Null(Assert.Single(unit.Parameters).DefaultValue);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterDefault);
    }

    [Theory]
    [InlineData("@()")]
    [InlineData("@(1, 2)")]
    public void Analyze_RejectsAuthoredSystemArrayCollectionsOutsideTheScalarDefaultContract(string defaultExpression)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-ArrayDefault {{ param([array] $Values = {defaultExpression}) return $Values.Count }}",
            ".psm1");

        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationSpec(
                fixture.ScriptPath,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapabilities.BinaryModule)).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Null(Assert.Single(unit.Parameters).DefaultValue);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == PowerShellCompilationFeatureIds.ParameterDefault);
    }

    [Theory]
    [InlineData("1u")]
    [InlineData("0b1")]
    public void Analyze_RejectsSystemArrayDefaultSyntaxOutsideTheSelectedSemanticProfile(string defaultExpression)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-ArrayDefault {{ param([array] $Values = {defaultExpression}) return $Values.Count }}",
            ".psm1");
        var desktop = new PowerShellCompilationAnalyzer(
                Array.Empty<PowerShellCompilationCommandProviderContract>(),
                PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
            .Analyze(new PowerShellCompilationSpec(
                fixture.ScriptPath,
                targetFramework: "net472",
                capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var core = new PowerShellCompilationAnalyzer(
                Array.Empty<PowerShellCompilationCommandProviderContract>(),
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
            .Analyze(new PowerShellCompilationSpec(
                fixture.ScriptPath,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var desktopFile = Assert.Single(desktop.Files);
        Assert.Empty(desktopFile.Units);
        Assert.Contains(desktopFile.Diagnostics, static diagnostic =>
            diagnostic.Code == PowerShellCompilationDiagnosticCode.ParseError &&
            diagnostic.FeatureId == PowerShellCompilationFeatureIds.Parser);
        var coreUnit = Assert.Single(Assert.Single(core.Files).Units);
        Assert.True(coreUnit.IsCompilable, string.Join(Environment.NewLine, coreUnit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1L")]
    [InlineData("1d")]
    [InlineData("0x1")]
    [InlineData("1kb")]
    public void Analyze_AllowsNumericLiteralSyntaxAvailableToWindowsPowerShell51(string defaultExpression)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Get-ArrayDefault {{ param([array] $Values = {defaultExpression}) return $Values.Count }}",
            ".psm1");
        var plan = new PowerShellCompilationAnalyzer(
                Array.Empty<PowerShellCompilationCommandProviderContract>(),
                PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
            .Analyze(new PowerShellCompilationSpec(
                fixture.ScriptPath,
                targetFramework: "net472",
                capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData("1u")]
    [InlineData("0b1")]
    public void Build_HybridBinaryModuleFailsBeforeRetainingSourceTheSelectedProfileCannotParse(string defaultExpression)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ArrayDefault { [CmdletBinding()] param([array] $Values = " + defaultExpression +
            ") return $Values.Count }; Export-ModuleMember -Function Get-ArrayDefault",
            ".psm1");
        var outputPath = Path.Combine(fixture.OutputPath, "profile-incompatible");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            outputPath,
            "PowerForge.ProfileIncompatibleArrayDefault",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net472",
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId
        });

        Assert.False(result.Succeeded);
        Assert.Null(result.Manifest);
        Assert.Contains("parser errors", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(outputPath) && Directory.EnumerateFiles(outputPath, "*.dll", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void SemanticPipelineRejectsGeneralSystemArrayConversionWithoutReachingTheBackend()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-ArrayValue { return [array]42 }",
            "system-array-conversion.ps1");

        var result = new PowerShellSemanticCompilationPipeline(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
            .Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.StaticRuntimeFacts);

        Assert.Empty(result.Emitted.Methods);
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2202");
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void Build_StrictLibraryPreservesOmittedAndExplicitSystemArrayValuesAcrossTargets(string targetFramework)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ArrayDefaultCount { param(" +
            "[ValidateSet('Good')] [array] $Values = 'Bad') return $Values.Count }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.OutputPath, targetFramework),
            "PowerForge.SystemArrayDefault" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = Assert.Single(assembly.GetTypes().SelectMany(static type => type.GetMethods()),
            static method => method.Name == "Get_ArrayDefaultCount");
        var omitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitlyBound = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Values" };

        Assert.Equal(1, method.Invoke(null, new object?[] { null, omitted }));
        Assert.Equal(0, method.Invoke(null, new object?[] { Array.Empty<object>(), explicitlyBound }));
        var invalid = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            method.Invoke(null, new object?[] { new object[] { "Bad" }, explicitlyBound }));
        Assert.IsType<ArgumentException>(invalid.InnerException);
        var parameter = Assert.Single(result.Manifest.PublicAbi!.Methods.Single().Parameters, static item => !item.CompilerAdded);
        Assert.Equal(PowerShellCompilationLiteralKind.Array, parameter.DefaultValue?.Kind);
        Assert.Equal("Bad", Assert.Single(parameter.DefaultValue!.Elements).Value);
        var persisted = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
            File.ReadAllText(result.ManifestPath!),
            PowerShellCompilationProjectManifestService.JsonOptions)!;
        var persistedParameter = Assert.Single(persisted.PublicAbi!.Methods.Single().Parameters, static item => !item.CompilerAdded);
        Assert.Equal("Bad", Assert.Single(persistedParameter.DefaultValue!.Elements).Value);
    }

    [Theory]
    [InlineData("net472", "powershell.exe")]
    [InlineData("net10.0", "pwsh")]
    public void Build_StrictBinaryModuleMatchesPowerShellForSystemArrayDefaultsAndValidation(
        string targetFramework,
        string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = ArtifactFixture.Create(
            "function Get-ArrayDefault { [CmdletBinding()] param(" +
            "[ValidateSet('Good')] [array] $Scans = 'Bad') return $Scans.Count }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.OutputPath, targetFramework),
            "PowerForge.SystemArrayModule" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string command =
            "Get-ArrayDefault; Get-ArrayDefault -Scans Good; " +
            "try { Get-ArrayDefault -Scans Bad -ErrorAction Stop; 'missed' } catch { 'rejected' }";
        var interpreted = RunModuleProof(fixture.ScriptPath, command, host);
        var compiled = RunModuleProof(result.ArtifactPath!, command, host);

        Assert.Equal(new[] { "1", "1", "rejected" }, interpreted.Split(Environment.NewLine));
        Assert.Equal(interpreted, compiled);
    }
}
