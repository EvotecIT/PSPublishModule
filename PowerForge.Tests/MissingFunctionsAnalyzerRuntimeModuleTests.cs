using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerRuntimeModuleTests
{
    [Fact]
    public void Analyze_RecursiveDonorAnalysisPrefersPrivateDonorHelperOverRuntimeExport()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string donorName = "Approved.Donor";
            const string runtimeName = "Runtime.Dependency";
            const string helperName = "Get-CollidingHelper";
            var donorPath = Directory.CreateDirectory(Path.Combine(root.FullName, donorName));
            var runtimePath = Directory.CreateDirectory(Path.Combine(root.FullName, runtimeName));
            File.WriteAllText(
                Path.Combine(donorPath.FullName, $"{donorName}.psm1"),
                $"function Get-DonorThing {{ {helperName} }}{Environment.NewLine}" +
                $"function {helperName} {{ 'donor-helper' }}{Environment.NewLine}" +
                "Export-ModuleMember -Function 'Get-DonorThing'" + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(donorPath.FullName, $"{donorName}.psd1"),
                $"@{{ RootModule = '{donorName}.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-DonorThing'); CmdletsToExport = @(); AliasesToExport = @() }}{Environment.NewLine}");
            File.WriteAllText(
                Path.Combine(runtimePath.FullName, $"{runtimeName}.psm1"),
                $"function {helperName} {{ 'runtime-helper' }}{Environment.NewLine}" +
                $"Export-ModuleMember -Function '{helperName}'{Environment.NewLine}");
            File.WriteAllText(
                Path.Combine(runtimePath.FullName, $"{runtimeName}.psd1"),
                $"@{{ RootModule = '{runtimeName}.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('{helperName}'); CmdletsToExport = @(); AliasesToExport = @() }}{Environment.NewLine}");

            var analyzer = new MissingFunctionsAnalyzer();
            var options = new MissingFunctionsOptions(
                approvedModules: new[] { donorName },
                includeFunctionsRecursively: true,
                approvedModuleSources: new[]
                {
                    new ApprovedModuleSource(donorName, "1.0.0", donorPath.FullName)
                },
                runtimeModuleSources: new[]
                {
                    new ApprovedModuleSource(runtimeName, "1.0.0", runtimePath.FullName)
                });

            var report = analyzer.Analyze(filePath: null, code: "Get-DonorThing", options);

            Assert.Equal(2, report.Functions.Length);
            Assert.Contains(report.Functions, function => function.Contains("donor-helper", StringComparison.Ordinal));
            Assert.DoesNotContain(report.Functions, function => function.Contains("runtime-helper", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

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
