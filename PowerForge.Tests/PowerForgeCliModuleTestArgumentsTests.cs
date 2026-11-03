using System.Reflection;

namespace PowerForge.Tests;

public sealed class PowerForgeCliModuleTestArgumentsTests
{
    [Theory]
    [InlineData("--results-path")]
    [InlineData("--junit-results-path")]
    [InlineData("--summary-path")]
    [InlineData("--pester-version")]
    public void Missing_values_cannot_consume_the_no_tests_flag(string option)
    {
        var parse = ProgramType.GetMethod("ParseTestArgs", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Null(parse.Invoke(null, new object[] { new[] { option, "--fail-if-no-tests" } }));
        var parsed = (ModuleTestSuiteSpec)parse.Invoke(null, new object[] { new[] { option, "valid-value", "--fail-if-no-tests" } })!;
        Assert.True(parsed.FailIfNoTests);
    }

    [Fact]
    public void Config_reports_resolve_against_the_declared_project_root()
    {
        var resolve = ProgramType.GetMethod("ResolveTestSpecPaths", BindingFlags.NonPublic | BindingFlags.Static)!;
        var root = Path.Combine(Path.GetTempPath(), "pf-report-path-contract");
        var spec = new ModuleTestSuiteSpec { ProjectPath = "../module", ResultsPath = "results/nunit.xml",
            JUnitResultsPath = "results/junit.xml", SummaryPath = "results/summary.md" };
        resolve.Invoke(null, new object[] { spec, Path.Combine(root, "config", "test.json") });
        Assert.Equal(Path.Combine(root, "module", "results", "nunit.xml"), spec.ResultsPath);
        Assert.Equal(Path.Combine(root, "module", "results", "junit.xml"), spec.JUnitResultsPath);
        Assert.Equal(Path.Combine(root, "module", "results", "summary.md"), spec.SummaryPath);
    }

    private static Type ProgramType => Assembly.LoadFrom(PowerForgeCliTestHost.ResolveAssemblyPath(
        PowerForgeCliTestHost.FindRepositoryRoot(), Environment.GetEnvironmentVariable(PowerForgeCliTestHost.AssemblyPathEnvironmentVariable)))
        .GetType("Program")!;
}
