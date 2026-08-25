using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationCoverageV2Tests
{
    [Fact]
    public void Run_SeparatesEmittedFunctionsFromModuleInitialization()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "Example.psm1");
        File.WriteAllText(
            source,
            "function Get-One { return 1 }; Register-ArgumentCompleter -CommandName Get-One -ParameterName Name -ScriptBlock { 'one' }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");

            var product = Assert.Single(result.Products);
            Assert.True(product.Coverage.PostEmissionEvaluated);
            Assert.Equal(1, product.Coverage.TotalFunctions);
            Assert.Equal(1, product.Coverage.AnalyzerEligibleFunctions);
            Assert.Equal(1, product.Coverage.EmittedFunctions);
            Assert.Equal(0, product.Coverage.DroppedEligibleFunctions);
            Assert.Equal(0, product.Coverage.FallbackFunctions);
            Assert.Equal(1, product.Coverage.TotalScriptUnits);
            Assert.Equal(1, product.Coverage.StructurallyEligibleScriptUnits);
            Assert.Equal(1, product.Coverage.FallbackScriptUnits);
            Assert.Equal(100d, product.Coverage.EmittedFunctionCoveragePercentage);
            Assert.Equal(1, result.EmittedFunctions);
            Assert.Equal(1, result.TotalFunctions);
            Assert.DoesNotContain(
                result.FunctionFrontier,
                impact => impact.FeatureId == "command.register-argumentcompleter");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_ReportsAnalyzerEligibleFunctionsDroppedByGraphShaping()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "Recursive.psm1");
        File.WriteAllText(
            source,
            "function Get-Even { param([int] $Value) if ($Value -le 0) { return $true }; return Get-Odd -Value ($Value - 1) }; " +
            "function Get-Odd { param([int] $Value) if ($Value -le 0) { return $false }; return Get-Even -Value ($Value - 1) }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");
            var product = Assert.Single(result.Products);

            Assert.Equal(2, product.Coverage.TotalFunctions);
            Assert.Equal(2, product.Coverage.AnalyzerEligibleFunctions);
            Assert.Equal(0, product.Coverage.EmittedFunctions);
            Assert.Equal(2, product.Coverage.DroppedEligibleFunctions);
            Assert.Equal(2, product.Coverage.FallbackFunctions);
            var graph = Assert.Single(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph);
            Assert.Equal(2, graph.VisibleSoleBlockerUnits);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_FailsBaselineWhenSourceContentChangesWithoutMetricChanges()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "StableShape.psm1");
        File.WriteAllText(source, "function Get-Value { return 1 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { source }, "net10.0");
            File.WriteAllText(source, "function Get-Value { return 2 }");

            var current = runner.Run(new[] { source }, "net10.0", baseline);

            var drift = Assert.Single(current.SourceDrifts);
            Assert.Equal("StableShape", drift.Product);
            Assert.NotEqual(drift.BaselineFingerprint, drift.CurrentFingerprint);
            Assert.Empty(current.Regressions);
            Assert.False(current.Passed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_UsesPortableContentFingerprintAcrossCheckoutRoots()
    {
        var root = CreateRoot();
        var first = Path.Combine(root, "First", "Module");
        var second = Path.Combine(root, "Second", "Module");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "Module.psm1"), "function Get-Value { return 1 }");
        File.WriteAllText(Path.Combine(second, "Module.psm1"), "function Get-Value { return 1 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { first }, "net10.0");
            var current = runner.Run(new[] { second }, "net10.0", baseline);

            Assert.Equal(baseline.Products[0].SourceFingerprint, current.Products[0].SourceFingerprint);
            Assert.Empty(current.SourceDrifts);
            Assert.True(current.Passed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_NoRecurseDoesNotPresentAnalyzerEligibilityAsEmittedCoverage()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "AnalyzeOnly.ps1");
        File.WriteAllText(source, "function Get-Value { return 1 }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(
                new[] { source },
                "net10.0",
                recurse: false);

            Assert.False(result.PostEmissionEvaluated);
            Assert.False(result.Products[0].Coverage.PostEmissionEvaluated);
            Assert.Equal(1, result.CompilableUnits);
            Assert.Equal(0, result.EmittedFunctions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Coverage V2 Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
