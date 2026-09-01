using PowerForge;
using System.Diagnostics;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void RuntimeFreeComposedGenericListMembersExecuteAcrossTargets(string targetFramework)
    {
        const string source = "function Get-ItemCount { " +
                              "$items = [System.Collections.Generic.List[string]]::new(); " +
                              "$items.AddRange([string[]] ('alpha', 'beta')); " +
                              "$copy = $items.ToArray(); " +
                              "return $items.Count -eq 2 -and $copy.Length -eq 2 }";
        using var fixture = OracleFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "GenericListMembers" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
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
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static method => method.Name == "Get_ItemCount");
        Assert.Equal(true, method.Invoke(null, null));
    }

    [Theory]
    [InlineData("PowerForge.Semantic/expandable-scalar", "value=42; flag=True")]
    [InlineData("PowerForge.Semantic/member-null-propagation", "True")]
    [InlineData("PowerForge.Semantic/typed-generic-list-invocation", "True")]
    [InlineData("PowerForge.Semantic/try-terminal-output", "True")]
    public void SemanticExpansionCasesBuildAndExecuteWithoutPowerShellRuntime(string caseId, string expected)
    {
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(caseId));
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict"),
            "SemanticExpansionArtifact",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        using var process = Process.Start(new ProcessStartInfo(build.ArtifactPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        })!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000));
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(expected, standardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesMemberStringAndTerminalFlowCasesAgainstPinnedHost()
    {
        foreach (var caseId in new[]
                 {
                     "PowerForge.Semantic/expandable-scalar",
                     "PowerForge.Semantic/member-null-propagation",
                     "PowerForge.Semantic/typed-generic-list-invocation",
                     "PowerForge.Semantic/try-terminal-output"
                 })
        {
            var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get(caseId);
            using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(caseId));
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
                "SemanticExpansionOracle",
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
}
