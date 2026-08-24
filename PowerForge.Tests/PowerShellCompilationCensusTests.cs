using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationCensusTests
{
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
