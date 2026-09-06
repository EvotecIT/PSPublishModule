using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCensusTests
{
    [Fact]
    public void Run_StrictExecutableCensusUsesEntryPointShaping()
    {
        using var fixture = new CensusInputFixture();
        var script = fixture.Write("Increment.ps1", "param([int] $Value = 41); $Value += 1; return $Value");
        var result = new PowerShellCompilationCensusRunner().RunWithOptions(new[] { script }, new PowerShellCompilationCensusOptions
        {
            TargetFramework = "net10.0", Mode = PowerShellCompilationMode.Strict,
            ArtifactKind = PowerShellCompilationArtifactKind.Executable
        });
        Assert.True(result.Passed, string.Join(Environment.NewLine, result.InputFailures.Select(failure => failure.Message)));
        var product = Assert.Single(result.Products);
        Assert.Equal(PowerShellCompilationArtifactKind.Executable, product.ArtifactKind);
        Assert.True(result.PostEmissionEvaluated);
        Assert.Equal(1, product.TotalUnits);
        Assert.Equal(1, product.CompilableUnits);
        Assert.Equal(0, product.RuntimeFallbackUnits);
        Assert.Equal(0, product.Coverage.TotalFunctions);
        Assert.Equal(1, product.Coverage.TotalScriptUnits);
    }

    [Fact]
    public void Run_PreservesSuccessfulInputsAndReportsDiscoveryFailure()
    {
        using var fixture = new CensusInputFixture();
        var failing = fixture.Write("Dynamic.ps1", "param([string] $Source); . $Source; return 1");
        var good = fixture.Write("Good.psm1", "function Get-Answer { return 42 }");
        var result = new PowerShellCompilationCensusRunner().Run(new[] { failing, good }, "net10.0");
        Assert.False(result.Passed);
        Assert.False(result.Complete);
        Assert.Equal(failing, Assert.Single(result.InputFailures).Path);
        Assert.Contains("dot", result.InputFailures[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(good, Assert.Single(result.Products).Path);
        Assert.Equal(1, result.EmittedFunctions);
        var roundTrip = JsonSerializer.Deserialize<PowerShellCompilationCensusResult>(JsonSerializer.Serialize(result))!;
        Assert.False(roundTrip.Passed);
        Assert.Single(roundTrip.Products);
        Assert.Equal(result.InputFailures[0].Message, Assert.Single(roundTrip.InputFailures).Message);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("artifact")]
    [InlineData("profile")]
    [InlineData("recursion")]
    public void Run_RejectsIncomparableCensusContracts(string difference)
    {
        using var fixture = new CensusInputFixture();
        var source = fixture.Write("Good.psm1", "function Get-Answer { return 42 }");
        var runner = new PowerShellCompilationCensusRunner();
        var baseline = runner.Run(new[] { source }, "net10.0");
        var options = new PowerShellCompilationCensusOptions { TargetFramework = "net10.0" };
        switch (difference)
        {
            case "mode": options.Mode = PowerShellCompilationMode.Strict; break;
            case "artifact": options.ArtifactKind = PowerShellCompilationArtifactKind.Library; break;
            case "profile": options.SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId; break;
            case "recursion": options.Recurse = false; break;
        }
        Assert.Throws<ArgumentException>(() => runner.RunWithOptions(new[] { source }, options, baseline));
    }

    [Fact]
    public void Run_PreservesGoodInputsWhenAnotherPathIsMalformed()
    {
        using var fixture = new CensusInputFixture();
        var good = fixture.Write("Good.psm1", "function Get-Answer { return 42 }");
        var invalid = "bad\0path.ps1";
        var result = new PowerShellCompilationCensusRunner().Run(new[] { invalid, good }, targetFramework: "net10.0");
        Assert.False(result.Passed);
        Assert.Equal(invalid, Assert.Single(result.InputFailures).Path);
        Assert.Equal(good, Assert.Single(result.Products).Path);
    }

    [Fact]
    public void Run_AcceptsLegacyDefaultFrameworkBaselineAndRejectsDifferentExplicitTarget()
    {
        using var fixture = new CensusInputFixture();
        var good = fixture.Write("Good.psm1", "function Get-Answer { return 42 }");
        var runner = new PowerShellCompilationCensusRunner();
        var current = runner.Run(new[] { good });
        var legacy = new PowerShellCompilationCensusResult(null, current.Products, current.Regressions, sourceDrifts: null);
        Assert.True(runner.Run(new[] { good }, baseline: legacy).Passed);
        Assert.Throws<ArgumentException>(() => runner.Run(new[] { good }, "net10.0", legacy));
    }

    private sealed class CensusInputFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge Census Contracts", Guid.NewGuid().ToString("N"));
        internal CensusInputFixture() => Directory.CreateDirectory(_root);
        internal string Write(string name, string source)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, source);
            return path;
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
