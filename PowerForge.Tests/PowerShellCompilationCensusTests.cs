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
}
