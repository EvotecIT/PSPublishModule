using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerRuntimeModuleTests
{
    [Theory]
    [InlineData("Get-RuntimeThing")]
    [InlineData("Runtime.Dependency\\Get-RuntimeThing")]
    public void Analyze_ResolvesExportedRuntimeDependencyWithoutInliningItsFunction(string invocation)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "Runtime.Dependency";
            const string commandName = "Get-RuntimeThing";
            File.WriteAllText(
                Path.Combine(root.FullName, $"{moduleName}.psm1"),
                $"function {commandName} {{ 'runtime' }}{Environment.NewLine}Export-ModuleMember -Function '{commandName}'{Environment.NewLine}");
            File.WriteAllText(
                Path.Combine(root.FullName, $"{moduleName}.psd1"),
                string.Join(Environment.NewLine, new[]
                {
                    "@{",
                    $"    RootModule = '{moduleName}.psm1'",
                    "    ModuleVersion = '1.0.0'",
                    $"    FunctionsToExport = @('{commandName}')",
                    "    CmdletsToExport = @()",
                    "    AliasesToExport = @()",
                    "}"
                }) + Environment.NewLine);

            var analyzer = new MissingFunctionsAnalyzer();
            var options = new MissingFunctionsOptions(
                runtimeModuleSources: new[]
                {
                    new ApprovedModuleSource(moduleName, "1.0.0", root.FullName)
                });

            var report = analyzer.Analyze(filePath: null, code: invocation, options);

            var command = Assert.Single(report.Summary, item =>
                string.Equals(item.Name, commandName, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(moduleName, command.Source, ignoreCase: true);
            Assert.Empty(report.Functions);
            Assert.Empty(report.FunctionsTopLevelOnly);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
