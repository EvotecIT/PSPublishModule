using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeDictionaryIndexAndMutatedMemberExecuteAcrossTargets(string targetFramework)
    {
        const string source = "function Get-IndexedValue {\n" +
                              "  $Values = [ordered] @{ First = 42; Second = 'ready' }\n" +
                              "  return $Values['First']\n" +
                              "}\n" +
                              "function Test-MutatedMember {\n" +
                              "  $Values = [ordered] @{ First = 42; Second = 'ready' }\n" +
                              "  $Values['Count'] = 'key'\n" +
                              "  if ($Values.Count -is [string]) { return 42 } else { return 0 }\n" +
                              "}\n" +
                              "function Get-ReassignedIndex {\n" +
                              "  $Values = [ordered] @{ First = 'old' }\n" +
                              "  $Values = [ordered] @{ First = 2; Second = 'ready' }\n" +
                              "  return $Values['First']\n" +
                              "}\n" +
                              "function Test-ReassignedFallback {\n" +
                              "  $Values = [ordered] @{ Count = 'key' }\n" +
                              "  $Values = [ordered] @{ Name = 1 }\n" +
                              "  if ($Values.Count -is [int]) { return 42 } else { return 0 }\n" +
                              "}";
        using var fixture = OracleFixture.Create(source);
        var profileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "BoundedDictionary" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = profileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(build.ArtifactPath!);
        var methods = assembly.GetTypes().SelectMany(static type => type.GetMethods()).ToArray();
        Assert.Equal(42, methods.Single(static candidate => candidate.Name == "Get_IndexedValue").Invoke(null, null));
        Assert.Equal(42, methods.Single(static candidate => candidate.Name == "Test_MutatedMember").Invoke(null, null));
        Assert.Equal(2, methods.Single(static candidate => candidate.Name == "Get_ReassignedIndex").Invoke(null, null));
        Assert.Equal(42, methods.Single(static candidate => candidate.Name == "Test_ReassignedFallback").Invoke(null, null));
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesDictionaryFlowCaseAgainstPinnedHost()
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get("PowerForge.Semantic/dictionary-flow");
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
            "BoundedDictionaryOracle",
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
