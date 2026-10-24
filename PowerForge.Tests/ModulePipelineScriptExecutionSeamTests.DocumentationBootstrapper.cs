using System;
using System.IO;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(ModuleDevelopmentBinaryMode.Auto)]
    [InlineData(ModuleDevelopmentBinaryMode.Environment)]
    [InlineData(ModuleDevelopmentBinaryMode.Off)]
    public void Run_DocumentationGate_PreservesSourceDevelopmentBootstrapper(ModuleDevelopmentBinaryMode mode)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "Module")).FullName;
            WriteMinimalModule(source, moduleName, "1.0.0");
            var binaryRoot = Path.Combine(root.FullName, "Binaries");
            ModuleBootstrapperGenerator.Generate(
                source, moduleName,
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "TestModule.Binary.dll" }, handleRuntimes: false, useAssemblyLoadContext: true,
                developmentBinaries: new ModuleDevelopmentBinaryBootstrapperOptions(
                    ModuleDevelopmentBinaryMode.Auto, binaryRoot, "TEST_MODULE_DEV", "TEST_MODULE_CONFIGURATION",
                    new[] { "net8.0" }, new[] { "net472" }));
            var psm1Path = Path.Combine(source, moduleName + ".psm1");
            // A committed loader can contain fixes from a newer generator than the docs tool.
            // Keep its bytes, including host contract guards, encoding and line endings.
            var content = File.ReadAllText(psm1Path).Replace("\r\n", "\n", StringComparison.Ordinal);
            File.WriteAllText(psm1Path, content + "# Retained source loader revision.\n", new UTF8Encoding(false));
            var original = File.ReadAllBytes(psm1Path);
            Assert.Contains("\"System.Management.Automation\"", content, StringComparison.Ordinal);

            var hostedOperations = new FakeHostedOperations();
            var runner = new ModulePipelineRunner(
                new NullLogger(), new ThrowingPowerShellRunner(), new FakeMetadataProvider(), hostedOperations);
            var result = runner.Run(new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = source,
                    Version = "1.0.0",
                    StagingPath = Path.Combine(root.FullName, "Staging"),
                    DevelopmentBinariesMode = mode,
                    DevelopmentBinariesPath = binaryRoot,
                    DevelopmentSourceBootstrapperMode = ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile,
                    ExportAssemblies = Array.Empty<string>()
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationGateSegment
                    {
                        Configuration = new GateConfiguration { Mode = ConfigurationGateMode.Documentation }
                    },
                    new ConfigurationDocumentationSegment
                    {
                        Configuration = new DocumentationConfiguration { Path = "Docs", PathReadme = "Docs/Readme.md" }
                    },
                    new ConfigurationBuildDocumentationSegment
                    {
                        Configuration = new BuildDocumentationConfiguration { Enable = true, GenerateExternalHelp = false }
                    }
                }
            });

            Assert.Equal(ConfigurationGateMode.Documentation, result.Plan.GateMode);
            Assert.Contains("Docs", hostedOperations.OperationOrder);
            Assert.True(File.Exists(Path.Combine(source, "Docs", "Readme.md")));
            Assert.Equal(original, File.ReadAllBytes(psm1Path));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
