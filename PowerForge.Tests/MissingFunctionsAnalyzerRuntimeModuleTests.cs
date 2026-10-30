using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerRuntimeModuleTests
{
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.0", false)]
    public void Analyze_ApprovedDonorHonorsSelectedRuntimeDependency(string donorMinimum, bool compatible)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var previousModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            const string runtimeName = "Selected.Runtime";
            const string donorName = "Selected.Donor";
            foreach (var version in new[] { "1.0.0", "2.0.0" })
            {
                var runtime = Directory.CreateDirectory(Path.Combine(root.FullName, runtimeName, version));
                var value = version == "1.0.0" ? "selected" : "unselected";
                File.WriteAllText(Path.Combine(runtime.FullName, runtimeName + ".psm1"),
                    $"function Get-SelectedRuntime {{ '{value}' }}\nExport-ModuleMember -Function Get-SelectedRuntime\n");
                File.WriteAllText(Path.Combine(runtime.FullName, runtimeName + ".psd1"),
                    $"@{{ RootModule = '{runtimeName}.psm1'; ModuleVersion = '{version}'; FunctionsToExport = @('Get-SelectedRuntime') }}");
            }
            var donor = Directory.CreateDirectory(Path.Combine(root.FullName, donorName));
            File.WriteAllText(Path.Combine(donor.FullName, donorName + ".psm1"),
                "if ((Get-SelectedRuntime) -eq 'selected') { function Get-SelectedDonor { 'selected-runtime' } }\n" +
                "else { function Get-SelectedDonor { 'unselected-runtime' } }\nExport-ModuleMember -Function Get-SelectedDonor\n");
            File.WriteAllText(Path.Combine(donor.FullName, donorName + ".psd1"),
                $"@{{ RootModule = '{donorName}.psm1'; ModuleVersion = '1.0.0'; RequiredModules = @(@{{ ModuleName = '{runtimeName}'; ModuleVersion = '{donorMinimum}' }}); FunctionsToExport = @('Get-SelectedDonor') }}");
            Environment.SetEnvironmentVariable("PSModulePath", root.FullName + Path.PathSeparator + previousModulePath);
            var options = new MissingFunctionsOptions(
                approvedModules: new[] { donorName },
                approvedModuleSources: new[] { new ApprovedModuleSource(donorName, "1.0.0", donor.FullName) },
                requireApprovedModuleSources: true,
                runtimeModuleSources: new[]
                {
                    new ApprovedModuleSource(runtimeName, "1.0.0", Path.Combine(root.FullName, runtimeName, "1.0.0"))
                });

            var report = new MissingFunctionsAnalyzer().Analyze(null, "Get-SelectedDonor", options);

            if (compatible)
                Assert.Contains(report.Functions, function => function.Contains("'selected-runtime'", StringComparison.Ordinal));
            else
            {
                Assert.Empty(report.Functions);
                var command = Assert.Single(report.Summary);
                Assert.Contains(runtimeName, command.Error, StringComparison.Ordinal);
                Assert.Contains("2.0.0", command.Error, StringComparison.Ordinal);
                Assert.Contains("1.0.0", command.Error, StringComparison.Ordinal);
            }
            Assert.DoesNotContain(report.Functions, function => function.Contains("'unselected-runtime'", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previousModulePath);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Analyze_ReportsRuntimeModuleImportErrorInsteadOfOnlyMissingCommand(bool unrelatedUnboundDonor)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "Runtime.Dependency";
            File.WriteAllText(Path.Combine(root.FullName, moduleName + ".psm1"),
                "Write-Error 'Required assembly fixture could not be loaded.' -ErrorAction Continue\n" +
                "function Get-RuntimeThing { 'runtime' }\nExport-ModuleMember -Function Get-RuntimeThing\n");
            File.WriteAllText(Path.Combine(root.FullName, moduleName + ".psd1"),
                "@{ RootModule = 'Runtime.Dependency.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-RuntimeThing') }");
            var options = new MissingFunctionsOptions(
                approvedModules: unrelatedUnboundDonor ? new[] { "Optional.Unbound" } : Array.Empty<string>(),
                requireApprovedModuleSources: true,
                runtimeModuleSources: new[]
                {
                    new ApprovedModuleSource(moduleName, "1.0.0", root.FullName)
                });

            var report = new MissingFunctionsAnalyzer().Analyze(null, "Get-RuntimeThing", options);

            var command = Assert.Single(report.Summary);
            Assert.Empty(command.CommandType);
            Assert.Contains(moduleName, command.Error, StringComparison.Ordinal);
            Assert.Contains("Required assembly fixture could not be loaded.", command.Error, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

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
