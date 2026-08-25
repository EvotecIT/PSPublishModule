using System.Text;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationCensusTests
{
    [Fact]
    public void Run_RanksStableFeaturesByVisibleCounterfactualImpact()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Frontier Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            source,
            "function Get-One { param([string] $Value) return ($Value -as [string]) }; " +
            "function Get-Two { param([string] $Value, [string] $Other = 'x') return ($Value -as [string]) }; " +
            "function Get-Three { return 1 }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");

            var conversion = Assert.Single(result.Frontier, impact => impact.FeatureId == "operator.as");
            Assert.Equal(2, conversion.AffectedUnits);
            Assert.Equal(2, conversion.VisibleSoleBlockerUnits);
            Assert.Equal(3, conversion.CandidateCompilableUnits);
            Assert.Equal(100d, conversion.CandidateCoveragePercentage, precision: 6);
            Assert.DoesNotContain(result.Frontier, impact => impact.FeatureId == PowerShellCompilationFeatureIds.ParameterDefault);
            Assert.Equal(result.Frontier.Select(static impact => impact.FeatureId).Distinct(StringComparer.Ordinal).Count(), result.Frontier.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_AttributesPostEmissionFailureToExactCapability()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Emission Attribution", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "function Get-Map { [hashtable] $Map = @{ Name = 'value' }; return $Map }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");

            var matches = result.FunctionFrontier
                .Where(impact => impact.FeatureId == PowerShellCompilationFeatureIds.ForSyntax("VariableExpressionAst"))
                .ToArray();
            Assert.True(matches.Length == 1, string.Join(", ", result.FunctionFrontier.Select(static impact => impact.FeatureId)));
            var exact = Assert.Single(matches);
            Assert.Equal(1, exact.VisibleSoleBlockerUnits);
            Assert.DoesNotContain(result.FunctionFrontier, impact => impact.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_ReservesFunctionGraphFeatureForActualRecursiveGraphConstraint()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Recursive Graph", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "function Get-Repeated { param([int] $Number) return Get-Repeated -Number $Number }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");

            var graph = Assert.Single(result.FunctionFrontier, impact => impact.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph);
            Assert.Equal(1, graph.VisibleSoleBlockerUnits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_AttributesSameLineEmissionFailuresToTheirFunctionExtents()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Same Line", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            source,
            "function Get-Map { [hashtable] $Map = @{ Name = 'value' }; return $Map }; " +
            "function Get-Repeated { param([int] $Number) return Get-Repeated -Number $Number }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");

            var variable = Assert.Single(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.ForSyntax("VariableExpressionAst"));
            var graph = Assert.Single(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph);
            Assert.Equal(1, variable.AffectedUnits);
            Assert.Equal(1, graph.AffectedUnits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_AttributesIndentedSameLineBinaryShapeFailureToExactFunction()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Binary Shape", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            source,
            "function Get-Valid { return 1 };    function InvalidName { return 2 }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");

            var shape = Assert.Single(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.BinaryCmdletShape);
            Assert.Equal(1, shape.AffectedUnits);
            Assert.DoesNotContain(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_AttributesMultilineDeclarationBinaryShapeFailureToExactFunction()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Multiline Shape", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "    function InvalidName" + Environment.NewLine + "    {" + Environment.NewLine + "        return 2" + Environment.NewLine + "    }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { source }, "net10.0");

            var shape = Assert.Single(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.BinaryCmdletShape);
            Assert.Equal(1, shape.AffectedUnits);
            Assert.DoesNotContain(result.FunctionFrontier, impact =>
                impact.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_MatchesRepeatedDisplayNamesByNormalizedProductPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Identity Tests", Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "One", "Product.psm1");
        var second = Path.Combine(root, "Two", "Product.psm1");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(first, "function Get-First { return 1 }");
        File.WriteAllText(second, "function Get-Second { return 2 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { first, second }, "net10.0");
            var current = runner.Run(new[] { first, second }, "net10.0", baseline);

            Assert.Equal(new[] { "Product", "Product" }, current.Products.Select(static product => product.Name));
            Assert.Empty(current.Regressions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_MatchesPortableProductIdentitiesAcrossCheckoutRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Portable Identity Tests", Guid.NewGuid().ToString("N"));
        var baselineRoot = Path.Combine(root, "Baseline");
        var currentRoot = Path.Combine(root, "Current");
        var relativePaths = new[] { Path.Combine("One", "Product.psm1"), Path.Combine("Two", "Product.psm1") };
        foreach (var relativePath in relativePaths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(baselineRoot, relativePath))!);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(currentRoot, relativePath))!);
            File.WriteAllText(Path.Combine(baselineRoot, relativePath), "function Get-Value { return 1 }");
            File.WriteAllText(Path.Combine(currentRoot, relativePath), "function Get-Value { return 1 }");
        }
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(relativePaths.Select(path => Path.Combine(baselineRoot, path)), "net10.0");
            var current = runner.Run(relativePaths.Select(path => Path.Combine(currentRoot, path)), "net10.0", baseline);

            Assert.Empty(current.Regressions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_SourceFingerprintNormalizesTextLineEndings()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Line Ending Tests", Guid.NewGuid().ToString("N"));
        var baselineSource = Path.Combine(root, "Baseline", "Product.psm1");
        var currentSource = Path.Combine(root, "Current", "Product.psm1");
        Directory.CreateDirectory(Path.GetDirectoryName(baselineSource)!);
        Directory.CreateDirectory(Path.GetDirectoryName(currentSource)!);
        File.WriteAllText(baselineSource, "function Get-Value {\n    return 1\n}\n");
        File.WriteAllText(currentSource, "function Get-Value {\r\n    return 1\r\n}\r\n");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { baselineSource }, "net10.0");
            var current = runner.Run(new[] { currentSource }, "net10.0", baseline);

            Assert.Equal(Assert.Single(baseline.Products).SourceFingerprint, Assert.Single(current.Products).SourceFingerprint);
            Assert.Empty(current.SourceDrifts);
            Assert.True(current.Passed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_SourceFingerprintPreservesMalformedByteIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Encoding Tests", Guid.NewGuid().ToString("N"));
        var baselineSource = Path.Combine(root, "Baseline", "Product.psm1");
        var equivalentSource = Path.Combine(root, "Equivalent", "Product.psm1");
        var currentSource = Path.Combine(root, "Current", "Product.psm1");
        Directory.CreateDirectory(Path.GetDirectoryName(baselineSource)!);
        Directory.CreateDirectory(Path.GetDirectoryName(equivalentSource)!);
        Directory.CreateDirectory(Path.GetDirectoryName(currentSource)!);
        var prefix = Encoding.ASCII.GetBytes("function Get-Value { return 1 }\r\n# legacy byte: ");
        File.WriteAllBytes(baselineSource, prefix.Concat(new byte[] { 0x80, (byte)'\r', (byte)'\n' }).ToArray());
        File.WriteAllBytes(equivalentSource, prefix.Concat(new byte[] { 0x80, (byte)'\n' }).ToArray());
        File.WriteAllBytes(currentSource, prefix.Concat(new byte[] { 0x81, (byte)'\n' }).ToArray());
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { baselineSource }, "net10.0");
            var equivalent = runner.Run(new[] { equivalentSource }, "net10.0", baseline);
            var current = runner.Run(new[] { currentSource }, "net10.0", baseline);

            Assert.Equal(Assert.Single(baseline.Products).SourceFingerprint, Assert.Single(equivalent.Products).SourceFingerprint);
            Assert.Empty(equivalent.SourceDrifts);
            Assert.True(equivalent.Passed);
            Assert.NotEqual(Assert.Single(baseline.Products).SourceFingerprint, Assert.Single(current.Products).SourceFingerprint);
            Assert.Single(current.SourceDrifts);
            Assert.False(current.Passed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_SourceFingerprintPreservesMalformedMultibyteCodeUnits()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Malformed Multibyte Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cases = new[]
            {
                ("Utf16Le", new byte[] { 0xFF, 0xFE, 0x00, 0x0D, 0x00 }, new byte[] { 0xFF, 0xFE, 0x00, 0x0A, 0x00 }),
                ("Utf16Be", new byte[] { 0xFE, 0xFF, 0x0D, 0x00, 0x00 }, new byte[] { 0xFE, 0xFF, 0x0A, 0x00, 0x00 }),
                ("Utf32Le", new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x00, 0x0D }, new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x00, 0x0A }),
                ("Utf32Be", new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x0D, 0x00 }, new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x0A, 0x00 })
            };
            foreach (var testCase in cases)
            {
                var baselineSource = Path.Combine(root, testCase.Item1, "Baseline", "Product.psm1");
                var currentSource = Path.Combine(root, testCase.Item1, "Current", "Product.psm1");
                Directory.CreateDirectory(Path.GetDirectoryName(baselineSource)!);
                Directory.CreateDirectory(Path.GetDirectoryName(currentSource)!);
                File.WriteAllBytes(baselineSource, testCase.Item2);
                File.WriteAllBytes(currentSource, testCase.Item3);
                var runner = new PowerShellCompilationCensusRunner();
                var baseline = runner.Run(new[] { baselineSource }, "net10.0");
                var current = runner.Run(new[] { currentSource }, "net10.0", baseline);

                Assert.NotEqual(Assert.Single(baseline.Products).SourceFingerprint, Assert.Single(current.Products).SourceFingerprint);
                Assert.Single(current.SourceDrifts);
                Assert.False(current.Passed);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_DisambiguatesRepeatedPortableDirectoryNamesAcrossCheckoutRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Repeated Directory Tests", Guid.NewGuid().ToString("N"));
        var baselineRoot = Path.Combine(root, "Baseline");
        var currentRoot = Path.Combine(root, "Current");
        try
        {
            foreach (var checkout in new[] { baselineRoot, currentRoot })
            foreach (var parent in new[] { "One", "Two" })
                Directory.CreateDirectory(Path.Combine(checkout, parent, "Module"));
            File.WriteAllText(Path.Combine(baselineRoot, "One", "Module", "Module.psm1"), "function Get-One { return 1 }; function Get-Two { return 2 }");
            File.WriteAllText(Path.Combine(baselineRoot, "Two", "Module", "Module.psm1"), "function Get-Other { return 3 }");
            File.WriteAllText(Path.Combine(currentRoot, "One", "Module", "Module.psm1"), "function Get-One { return 1 }");
            File.WriteAllText(Path.Combine(currentRoot, "Two", "Module", "Module.psm1"), "function Get-Other { return 3 }");
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { Path.Combine(baselineRoot, "One", "Module"), Path.Combine(baselineRoot, "Two", "Module") }, "net10.0");
            var current = runner.Run(new[] { Path.Combine(currentRoot, "Two", "Module"), Path.Combine(currentRoot, "One", "Module") }, "net10.0", baseline);

            Assert.Contains(current.Regressions, regression => regression.Metric == "TotalUnits" && regression.Baseline == 2 && regression.Current == 1);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_CountsManifestRuntimeHooksAsFallbackUnits()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Runtime Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Module.psm1"), "function Get-Compiled { return 1 }");
        File.WriteAllText(Path.Combine(root, "Runtime.ps1"), "function Get-Runtime { return 2 }");
        File.WriteAllText(
            Path.Combine(root, "Module.psd1"),
            "@{ RootModule = 'Module.psm1'; ModuleVersion = '1.0.0'; ScriptsToProcess = @('Runtime.ps1') }");
        try
        {
            var result = new PowerShellCompilationCensusRunner().Run(new[] { root }, "net10.0");
            var product = Assert.Single(result.Products);

            Assert.Equal(2, product.SourceFiles);
            Assert.Equal(2, product.TotalUnits);
            Assert.Equal(1, product.CompilableUnits);
            Assert.Equal(1, product.RuntimeFallbackUnits);
            Assert.Contains(product.Blockers, blocker => blocker.Message.Contains("manifest runtime hook", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_ReportsDisappearingUnitsAsCoverageRegression()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Shrink Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "function Get-One { return 1 }; function Get-Two { return 2 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { source }, "net10.0");
            File.WriteAllText(source, "function Get-One { return 1 }");
            var current = runner.Run(new[] { source }, "net10.0", baseline);

            Assert.Contains(current.Regressions, regression => regression.Metric == "TotalUnits");
            Assert.False(current.Passed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_RejectsBaselineFromDifferentTargetFramework()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge Census Framework Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Module.psm1");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "function Get-One { return 1 }");
        try
        {
            var runner = new PowerShellCompilationCensusRunner();
            var baseline = runner.Run(new[] { source }, "net8.0");

            var exception = Assert.Throws<ArgumentException>(() => runner.Run(new[] { source }, "net10.0", baseline));
            Assert.Contains("target framework", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
