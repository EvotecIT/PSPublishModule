using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    private const string ConditionalLifecycleSource =
        "function Select-Value { [CmdletBinding()] [OutputType([int])] " +
        "param([Parameter(ValueFromPipeline)][int] $Value) " +
        "begin { [int] $Final = 10 } " +
        "process { if ($Value -gt 0) { if ($Value -eq 2) { $Value } else { 100 } } } " +
        "end { $Final } } " +
        "function Invoke-Select { param([int[]] $Values) $Values | Select-Value } " +
        "-1, 1, 2 | Select-Value";

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeConditionalPipelineLifecyclePreservesOrderAndCardinality(string targetFramework)
    {
        using var fixture = OracleFixture.Create(ConditionalLifecycleSource);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "ConditionalPipelineLifecycle" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
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
        var typedAssembly = Assert.Single(build.Manifest.Files, static file => file.Role == "GeneratedAssembly");
        var assembly = System.Reflection.Assembly.LoadFrom(typedAssembly.Path);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Invoke_Select");
        Assert.Equal(new[] { 100, 2, 10 }, Assert.IsType<int[]>(method.Invoke(null, new object?[] { new[] { -1, 1, 2 } })));
        Assert.Equal(new[] { 10 }, Assert.IsType<int[]>(method.Invoke(null, new object?[] { null })));
        Assert.Equal(new[] { 10 }, Assert.IsType<int[]>(method.Invoke(null, new object?[] { Array.Empty<int>() })));
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeConditionalPipelineLifecycleMatchesConfiguredExactPowerShellProfiles()
    {
        using var fixture = OracleFixture.Create(ConditionalLifecycleSource);
        var profiles = new[]
        {
            (PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId, Environment.GetEnvironmentVariable("POWERFORGE_PWSH74_PATH")),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH"))
        };
        Assert.All(profiles.Skip(1), static profile => Assert.False(string.IsNullOrWhiteSpace(profile.Item2)));
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict"),
            "ConditionalPipelineLifecycleOracle",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var strict = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);
        Assert.Equal(new[] { "100", "2", "10" }, strict.Success.Select(static item => item.Value));

        var runner = new PowerShellCompilationSemanticOracleRunner();
        foreach (var profile in profiles)
        {
            var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile.Item1);
            var interpreted = runner.Observe(new PowerShellCompilationSemanticOracleRequest(profile.Item1, fixture.ScriptPath)
            {
                HostExecutablePath = profile.Item2,
                ExpectedHostArtifactSha256 = pin.HostArtifactIdentitySha256
            });
            Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(
                interpreted,
                strict,
                new[] { "Encoding", "ExitCode", "ProfileId" }));
        }
    }
}
