using System.Management.Automation.Language;

namespace PowerForge.Tests;

public sealed class ManagedModuleMaterializationStrategyBenchmarkScriptTests
{
    [Fact]
    public void MaterializationStrategyBenchmarkScript_ParsesAndDeclaresComparedStrategies()
    {
        var scriptPath = Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "Measure-ManagedModuleMaterializationStrategies.ps1");

        var ast = Parser.ParseFile(scriptPath, out _, out var errors);

        Assert.Empty(errors);
        var text = ast.Extent.Text;
        Assert.Contains("ExtractedPayload", text, StringComparison.Ordinal);
        Assert.Contains("PackageArchive", text, StringComparison.Ordinal);
        Assert.Contains("Save-ManagedModule", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyConcurrencyBenchmarkScript_ParsesAndCallsComparisonHarness()
    {
        var scriptPath = Path.Combine(
            RepoRootLocator.Find(),
            "Benchmarks",
            "ManagedModules",
            "Measure-ManagedModuleDependencyConcurrency.ps1");

        var ast = Parser.ParseFile(scriptPath, out _, out var errors);

        Assert.Empty(errors);
        var text = ast.Extent.Text;
        Assert.Contains("DependencyConcurrency", text, StringComparison.Ordinal);
        Assert.Contains("Compare-ManagedModuleEngines.ps1", text, StringComparison.Ordinal);
        Assert.Contains("ModuleFast", text, StringComparison.Ordinal);
    }
}
