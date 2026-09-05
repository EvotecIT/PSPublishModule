using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeStaticMemberAssignmentExecutesAcrossCoreTargets(string targetFramework)
    {
        const string caseId = "PowerForge.Semantic/assignment-target";
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(caseId));
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "StaticAssignmentTarget" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);
        Assert.Equal("0", Assert.Single(observation.Success).Value);
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeIndexAndMemberAssignmentExecuteAcrossTargets(string targetFramework)
    {
        const string source = "function Get-AssignmentTargetValue {\n" +
                              "  $Values = [string[]] ('value', 'old')\n" +
                              "  $Values[1] = '42'\n" +
                              "  $Holder = [System.UriBuilder]::new('https://example.test')\n" +
                              "  $Holder.Host = [string]::Concat($Values)\n" +
                              "  return [string]::CompareOrdinal($Holder.Host, 'value42')\n" +
                              "}";
        using var fixture = OracleFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "BoundedAssignmentTarget" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
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
        Assert.Equal(0, methods.Single(static method => method.Name == "Get_AssignmentTargetValue").Invoke(null, null));
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesAssignmentTargetCaseAgainstPinnedHost()
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get("PowerForge.Semantic/assignment-target");
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId));
        var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);
        var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
            {
                HostExecutablePath = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH"),
                ExpectedHostArtifactSha256 = pin.HostArtifactIdentitySha256
            });
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict"),
            "BoundedAssignmentTargetOracle",
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
        var strict = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(pin.ProfileId, build);
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
