using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineScriptExportDetectorTests
{
    [Fact]
    public void UpdateManifestForGeneratedDeliveryCommands_UsesInjectedScriptFunctionExportDetector()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var manifestPath = Path.Combine(root.FullName, $"{moduleName}.psd1");
            var manifestMutator = new ModulePipelineMissingAnalysisServiceTests.FakeManifestMutator();
            var scriptDetector = new RecordingScriptFunctionExportDetector("Install-TestModule");
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(),
                new ModulePipelineMissingAnalysisServiceTests.FakeDependencyMetadataProvider(),
                new ModulePipelineMissingAnalysisServiceTests.FakeHostedOperations(),
                manifestMutator,
                new ModulePipelineMissingAnalysisServiceTests.RecordingMissingFunctionAnalysisService(new MissingFunctionAnalysisResult(
                    Array.Empty<MissingCommandReference>(),
                    Array.Empty<MissingCommandReference>(),
                    Array.Empty<string>(),
                    Array.Empty<string>())),
                scriptDetector);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                GenerateInstallCommand = true
                            }
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(root.FullName, manifestPath, new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            runner.UpdateManifestForGeneratedDeliveryCommands(plan, buildResult, mergedScripts: false);

            Assert.Equal(1, scriptDetector.Calls);
            var write = Assert.Single(manifestMutator.ManifestExportWrites);
            Assert.Equal(new[] { "Install-TestModule" }, write.Functions);
            Assert.Empty(write.Cmdlets);
            Assert.Empty(write.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class RecordingScriptFunctionExportDetector : IScriptFunctionExportDetector
    {
        private readonly string[] _functions;

        public RecordingScriptFunctionExportDetector(params string[] functions)
        {
            _functions = functions;
        }

        public int Calls { get; private set; }

        public IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
        {
            Calls++;
            return _functions;
        }
    }
}
