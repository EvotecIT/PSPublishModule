using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
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
    public void Run_ReportsSemanticGraphFallbackWithoutDroppedEligibleFunctions()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "Recursive.psm1");
        File.WriteAllText(
            source,
            "function Get-Even { param([int] $Value) if ($Value -le 0) { return $true }; $Value -= 1; return Get-Odd -Value $Value }; " +
            "function Get-Odd { param([int] $Value) if ($Value -le 0) { return $false }; $Value -= 1; return Get-Even -Value $Value }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");
            var product = Assert.Single(result.Products);

            Assert.Equal(2, product.Coverage.TotalFunctions);
            Assert.Equal(0, product.Coverage.AnalyzerEligibleFunctions);
            Assert.Equal(0, product.Coverage.EmittedFunctions);
            Assert.Equal(0, product.Coverage.DroppedEligibleFunctions);
            Assert.Equal(2, product.Coverage.FallbackFunctions);
            var graph = Assert.Single(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph);
            Assert.Equal(2, graph.AffectedUnits);
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

    [Fact]
    public void Run_NoRecurseUsesHybridModuleCapabilitiesForScriptModules()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "AnalyzeOnly.psm1");
        File.WriteAllText(source, "function Get-State { return $script:State }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(
                new[] { source },
                "net10.0",
                recurse: false);

            Assert.False(result.PostEmissionEvaluated);
            Assert.Equal(1, result.CompilableUnits);
            Assert.Equal(1, result.Products[0].Coverage.AnalyzerEligibleFunctions);
            Assert.Equal(0, result.EmittedFunctions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_FailsBaselineWhenPostEmissionEvaluationIsSkipped()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "Evaluation.ps1");
        File.WriteAllText(source, "function Get-Value { return 1 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { source }, "net10.0", recurse: true);
            var current = runner.Run(new[] { source }, "net10.0", baseline, recurse: false);

            Assert.Contains(current.Regressions, static regression => regression.Metric == "PostEmissionEvaluated");
            Assert.False(current.Passed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_AllowsNewEligibilityToReachAttributedArtifactShaping()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "AttributedShaping.ps1");
        File.WriteAllText(
            source,
            "function Get-Value { param([int] $Verbose) return $Verbose }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var observed = runner.Run(new[] { source }, "net10.0");
            var currentProduct = Assert.Single(observed.Products);
            Assert.Equal(1, currentProduct.Coverage.AnalyzerEligibleFunctions);
            Assert.Equal(0, currentProduct.Coverage.EmittedFunctions);
            Assert.Equal(1, currentProduct.Coverage.DroppedEligibleFunctions);
            Assert.Equal(1, currentProduct.Coverage.FallbackFunctions);
            var currentDisposition = Assert.Single(currentProduct.FunctionDispositions);

            var baselineCoverage = new PowerShellCompilationCoverageBreakdown(
                postEmissionEvaluated: true,
                totalFunctions: currentProduct.Coverage.TotalFunctions,
                analyzerEligibleFunctions: 0,
                emittedFunctions: currentProduct.Coverage.EmittedFunctions,
                droppedEligibleFunctions: 0,
                fallbackFunctions: currentProduct.Coverage.FallbackFunctions,
                totalScriptUnits: currentProduct.Coverage.TotalScriptUnits,
                structurallyEligibleScriptUnits: currentProduct.Coverage.StructurallyEligibleScriptUnits,
                fallbackScriptUnits: currentProduct.Coverage.FallbackScriptUnits,
                runtimeOnlyFunctions: currentProduct.Coverage.RuntimeOnlyFunctions,
                runtimeOnlyScriptUnits: currentProduct.Coverage.RuntimeOnlyScriptUnits);
            var baselineDisposition = new PowerShellCompilationFunctionDisposition(
                currentDisposition.UnitId,
                currentDisposition.RelativePath,
                currentDisposition.Name,
                currentDisposition.StartLine,
                semanticEligible: false,
                emitted: false,
                runtimeRouted: true,
                shapingFallback: false);
            var baseline = CreateBaseline(CreateBaselineProduct(
                currentProduct,
                baselineCoverage,
                new[] { baselineDisposition }));

            var compared = runner.Run(new[] { source }, "net10.0", baseline);

            Assert.DoesNotContain(compared.Regressions, static regression =>
                regression.Metric == "DroppedEligibleFunctions");
            Assert.True(compared.Passed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_FailsBaselineWhenEqualCountsReplaceAnEmittedFunction()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "DispositionReplacement.ps1");
        File.WriteAllText(
            source,
            "function Get-Shaped { param([int] $Verbose) return $Verbose }; function Get-Emitted { return 1 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var observed = runner.Run(new[] { source }, "net10.0");
            var currentProduct = Assert.Single(observed.Products);
            Assert.Equal((2, 1, 1, 1), (
                currentProduct.Coverage.AnalyzerEligibleFunctions,
                currentProduct.Coverage.EmittedFunctions,
                currentProduct.Coverage.DroppedEligibleFunctions,
                currentProduct.Coverage.FallbackFunctions));
            var shaped = Assert.Single(currentProduct.FunctionDispositions, static disposition => disposition.Name == "Get-Shaped");
            var emitted = Assert.Single(currentProduct.FunctionDispositions, static disposition => disposition.Name == "Get-Emitted");

            var baselineCoverage = new PowerShellCompilationCoverageBreakdown(
                postEmissionEvaluated: true,
                totalFunctions: 2,
                analyzerEligibleFunctions: 1,
                emittedFunctions: 1,
                droppedEligibleFunctions: 0,
                fallbackFunctions: 1,
                totalScriptUnits: currentProduct.Coverage.TotalScriptUnits,
                structurallyEligibleScriptUnits: currentProduct.Coverage.StructurallyEligibleScriptUnits,
                fallbackScriptUnits: currentProduct.Coverage.FallbackScriptUnits);
            var baselineDispositions = new[]
            {
                new PowerShellCompilationFunctionDisposition(
                    shaped.UnitId, shaped.RelativePath, shaped.Name, shaped.StartLine,
                    semanticEligible: true, emitted: true, runtimeRouted: false, shapingFallback: false),
                new PowerShellCompilationFunctionDisposition(
                    emitted.UnitId, emitted.RelativePath, emitted.Name, emitted.StartLine,
                    semanticEligible: false, emitted: false, runtimeRouted: true, shapingFallback: false)
            };
            var baseline = CreateBaseline(CreateBaselineProduct(currentProduct, baselineCoverage, baselineDispositions));

            var compared = runner.Run(new[] { source }, "net10.0", baseline);

            Assert.Contains(compared.Regressions, regression => regression.Metric == "EmittedFunction:" + shaped.UnitId);
            Assert.Contains(compared.Regressions, regression => regression.Metric == "RuntimeRoutedFunction:" + shaped.UnitId);
            Assert.False(compared.Passed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_FailsNonEmptyBaselineWithoutFunctionDispositions()
    {
        var root = CreateRoot();
        var source = Path.Combine(root, "IdentityRequired.ps1");
        File.WriteAllText(source, "function Get-Value { return 1 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var current = Assert.Single(runner.Run(new[] { source }, "net10.0").Products);
            var identitylessProduct = new PowerShellCompilationCensusProduct(
                current.Name,
                current.Path,
                current.SourceFiles,
                current.TotalUnits,
                current.CompilableUnits,
                current.RuntimeFallbackUnits,
                current.ParseErrorFiles,
                current.AnalysisMilliseconds,
                current.Blockers,
                current.FeatureImpacts,
                current.DependencySummary,
                current.ResourceSummary,
                current.Coverage,
                current.SourceFingerprint,
                current.FunctionImpacts);

            var compared = runner.Run(new[] { source }, "net10.0", CreateBaseline(identitylessProduct));

            Assert.Contains(compared.Regressions, static regression =>
                regression.Metric == "BaselineFunctionDispositionCount" &&
                regression.Baseline == 1 &&
                regression.Current == 0);
            Assert.False(compared.Passed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PowerShellCompilationCensusProduct CreateBaselineProduct(
        PowerShellCompilationCensusProduct current,
        PowerShellCompilationCoverageBreakdown coverage,
        PowerShellCompilationFunctionDisposition[] dispositions)
        => new(
            current.Name,
            current.Path,
            current.SourceFiles,
            current.TotalUnits,
            current.CompilableUnits,
            current.RuntimeFallbackUnits,
            current.ParseErrorFiles,
            current.AnalysisMilliseconds,
            current.Blockers,
            current.FeatureImpacts,
            current.DependencySummary,
            current.ResourceSummary,
            coverage,
            current.SourceFingerprint,
            current.FunctionImpacts,
            dispositions);

    private static PowerShellCompilationCensusResult CreateBaseline(PowerShellCompilationCensusProduct product)
        => new(
            "net10.0",
            new[] { product },
            Array.Empty<PowerShellCompilationCensusRegression>(),
            Array.Empty<PowerShellCompilationFeatureImpact>(),
            Array.Empty<PowerShellCompilationFeaturePair>(),
            Array.Empty<PowerShellCompilationCensusSourceDrift>(),
            Array.Empty<PowerShellCompilationFeatureImpact>(),
            Array.Empty<PowerShellCompilationFeaturePair>());

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Coverage V2 Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
