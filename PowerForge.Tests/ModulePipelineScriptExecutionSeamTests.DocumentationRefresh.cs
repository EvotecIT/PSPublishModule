namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(ConfigurationGateMode.Documentation)]
    [InlineData(ConfigurationGateMode.Build)]
    public void Run_ProjectDocumentationSyncFailureFailsDocumentationGate(ConfigurationGateMode mode)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var helpPath = Path.Combine(root.FullName, "en-US", moduleName + "-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath, "<oldHelpItems />");
            var hostedOperations = new FakeHostedOperations
            {
                ActionStarted = (_, context) =>
                {
                    if (context.Stage == ModulePipelineActionStage.BeforeDocumentation)
                    {
                        // A conflicting destination can appear after staging. Generation still
                        // succeeds, but copying its Readme back must not report a fresh source.
                        var conflictingDirectory = Path.Combine(root.FullName, "Docs", "Readme.md");
                        Directory.CreateDirectory(conflictingDirectory);
                        File.WriteAllText(Path.Combine(conflictingDirectory, "asset.txt"), "Existing asset");
                    }
                }
            };
            var runner = new ModulePipelineRunner(
                new NullLogger(), new ThrowingPowerShellRunner(), new FakeMetadataProvider(), hostedOperations);
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationGateSegment { Configuration = new GateConfiguration { Mode = mode } },
                    new ConfigurationDocumentationSegment
                    {
                        Configuration = new DocumentationConfiguration { Path = "Docs", PathReadme = "Docs/Readme.md" }
                    },
                    new ConfigurationBuildDocumentationSegment
                    {
                        Configuration = new BuildDocumentationConfiguration { Enable = true, GenerateExternalHelp = false }
                    },
                    new ConfigurationActionSegment
                    {
                        Configuration = new ModulePipelineActionConfiguration
                        {
                            Name = "Destination changes after staging",
                            At = ModulePipelineActionStage.BeforeDocumentation,
                            InlineScript = "Write-Output 'filesystem boundary'"
                        }
                    }
                }
            };

            if (mode == ConfigurationGateMode.Documentation)
            {
                var error = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
                Assert.Contains("Failed to update project documentation", error.Message);
                Assert.NotNull(error.InnerException);
            }
            else
            {
                // Build mode's package output remains usable even when the source copy fails.
                var result = runner.Run(spec);
                Assert.True(result.DocumentationResult!.Succeeded);
            }
            Assert.Contains("Docs", hostedOperations.OperationOrder);
            Assert.Equal("<oldHelpItems />", File.ReadAllText(helpPath));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
