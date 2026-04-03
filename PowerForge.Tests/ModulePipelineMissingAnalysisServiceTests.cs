using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineMissingAnalysisServiceTests
{
    [Fact]
    public void AnalyzeMissingFunctions_UsesInjectedMissingAnalysisService()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName);

            var expected = new MissingFunctionAnalysisResult(
                summary: new[] { new MissingCommandReference("Get-ADUser", string.Empty, string.Empty, isAlias: false, isPrivate: false, error: string.Empty) },
                summaryFiltered: Array.Empty<MissingCommandReference>(),
                functions: new[] { "function Get-ADUser { }" },
                functionsTopLevelOnly: new[] { "function Get-ADUser { }" });

            var fake = new FakeMissingFunctionAnalysisService(expected);
            var runner = new ModulePipelineRunner(new NullLogger(), missingFunctionAnalysisService: fake);
            var plan = runner.Plan(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null
                },
                Install = new ModulePipelineInstallOptions { Enabled = false }
            });

            var method = typeof(ModulePipelineRunner).GetMethod("AnalyzeMissingFunctions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(method is not null, "AnalyzeMissingFunctions method signature may have changed.");

            var result = method!.Invoke(runner, new object?[] { null, "Get-ADUser", plan });

            var typed = Assert.IsType<MissingFunctionAnalysisResult>(result);
            Assert.Same(expected, typed);
            Assert.Equal(1, fake.CallCount);
            Assert.Equal("Get-ADUser", fake.LastCode);
            Assert.Null(fake.LastFilePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteMinimalModule(string moduleRoot, string moduleName)
    {
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, $"{moduleName}.psm1"), string.Empty);
        File.WriteAllText(
            Path.Combine(moduleRoot, $"{moduleName}.psd1"),
            "@{" + Environment.NewLine +
            $"    RootModule = '{moduleName}.psm1'" + Environment.NewLine +
            "    ModuleVersion = '1.0.0'" + Environment.NewLine +
            "    FunctionsToExport = @()" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    AliasesToExport = @()" + Environment.NewLine +
            "}" + Environment.NewLine);
    }

    private sealed class FakeMissingFunctionAnalysisService : IMissingFunctionAnalysisService
    {
        private readonly MissingFunctionAnalysisResult _result;

        public FakeMissingFunctionAnalysisService(MissingFunctionAnalysisResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }
        public string? LastFilePath { get; private set; }
        public string? LastCode { get; private set; }

        public MissingFunctionAnalysisResult Analyze(string? filePath, string? code, MissingFunctionsOptions? options = null)
        {
            CallCount++;
            LastFilePath = filePath;
            LastCode = code;
            return _result;
        }
    }
}
