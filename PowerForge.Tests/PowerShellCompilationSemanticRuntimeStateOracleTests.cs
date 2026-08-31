using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Theory]
    [InlineData("[7.4,7.5)", 7)]
    [InlineData("[7.4,8.0)", 7)]
    [InlineData("[7.4,8.1)", 0)]
    [InlineData("custom", 0)]
    public void SemanticProfileReportsMajorOnlyWhenTheCompleteRangeFixesIt(string versionRange, int expectedMajor)
    {
        var profile = new PowerShellCompilationSemanticOracleProfile(
            "PowerForge.Oracle.Custom/test",
            PowerShellCompilationSemanticHostFamily.PowerShell7,
            "pwsh",
            "Core",
            versionRange,
            "Any",
            "Any",
            "invariant-per-case",
            Array.Empty<string>(),
            "source",
            "commit",
            "https://example.test/profile");

        Assert.Equal(expectedMajor, profile.PowerShellMajorVersion);
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesReadOnlyRuntimeStateCaseAgainstPinnedHost()
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get("PowerForge.Semantic/runtime-read-only-state");
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
            "ReadOnlyRuntimeStateOracle",
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
