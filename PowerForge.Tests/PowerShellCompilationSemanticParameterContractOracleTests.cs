using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeTypedAndDefaultedParametersExecuteAcrossTargets(string targetFramework)
    {
        const string source = "function Get-TypedValue { param([int] $Value) return $Value }\n" +
                              "function Get-DefaultValue { param([int] $Value = 42) return $Value }\n" +
                              "function Get-DefaultTarget { param([EnvironmentVariableTarget] $Target = ([EnvironmentVariableTarget]::User)) return $Target }";
        using var fixture = OracleFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "BoundedParameterContract" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(build.ArtifactPath!);
        var methods = assembly.GetTypes().SelectMany(static type => type.GetMethods()).ToArray();
        Assert.Equal(42, methods.Single(static method => method.Name == "Get_TypedValue").Invoke(null, new object?[] { 42 }));
        var generatedDefault = methods.Single(static method => method.Name == "Get_DefaultValue");
        var omitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitlyBound = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Value" };
        Assert.Equal(42, generatedDefault.Invoke(null, new object?[] { 0, omitted }));
        Assert.Equal(0, generatedDefault.Invoke(null, new object?[] { 0, explicitlyBound }));
        var defaultMethod = build.Manifest.PublicAbi!.Methods.Single(static method => method.ClrName == "Get_DefaultValue");
        var parameter = Assert.Single(defaultMethod.Parameters, static item => !item.CompilerAdded);
        var boundState = Assert.Single(defaultMethod.Parameters, static item => item.CompilerAdded);
        Assert.True(parameter.HasDefaultValue);
        Assert.Equal("42", parameter.DefaultValue?.Value);
        Assert.Equal("BoundParameterNames", boundState.CompilerPurpose);
        var enumDefault = methods.Single(static method => method.Name == "Get_DefaultTarget");
        Assert.Equal(
            EnvironmentVariableTarget.User,
            enumDefault.Invoke(null, new object?[] { default(EnvironmentVariableTarget), omitted }));
        var enumDefaultMethod = build.Manifest.PublicAbi.Methods.Single(static method => method.ClrName == "Get_DefaultTarget");
        Assert.Equal(
            ((int)EnvironmentVariableTarget.User).ToString(),
            Assert.Single(enumDefaultMethod.Parameters, static item => !item.CompilerAdded).DefaultValue?.Value);
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesTypedParameterCaseAgainstPinnedHost()
        => QualifyRuntimeFreeParameterCase("PowerForge.Semantic/parameter-type", "BoundedParameterTypeOracle");

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesParameterDefaultCaseAgainstPinnedHost()
        => QualifyRuntimeFreeParameterCase("PowerForge.Semantic/parameter-default", "BoundedParameterDefaultOracle");

    private static void QualifyRuntimeFreeParameterCase(string caseId, string artifactName)
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get(caseId);
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId));
        var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);
        var arguments = semanticCase.Arguments.ToArray();
        var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
            {
                Arguments = arguments,
                HostExecutablePath = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH"),
                ExpectedHostArtifactSha256 = pin.HostArtifactIdentitySha256
            });
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict"),
            artifactName,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = pin.ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var strict = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(pin.ProfileId, build, arguments);
        var allowed = new[] { "Encoding", "ExitCode" };
        Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(interpreted, strict, allowed));
        var differences = PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            semanticCase.FeatureId,
            new[] { interpreted, strict },
            allowed,
            "The interpreted script has no enclosing process exit contract and host encoding differs from the Strict UTF-8 executable contract.");
        Assert.Equal(
            new[] { "Encoding", "ExitCode" },
            differences.Select(static difference => difference.Path).OrderBy(static path => path, StringComparer.Ordinal));
    }
}
