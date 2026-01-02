using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineCleanupOnFailureTests
{
    [Fact]
    public void PipelineFailure_CleansGeneratedStagingDirectory()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var moduleName = "TestModule";

            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "src"));
            var publicDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Public"));
            File.WriteAllText(Path.Combine(publicDir.FullName, "Get-Test.ps1"), "function Get-Test { 'ok' }");

            // Make the artefact output path invalid by creating a file where a directory is expected.
            var badArtefactRoot = Path.Combine(tempRoot.FullName, "artefacts");
            File.WriteAllText(badArtefactRoot, "this is a file, not a folder");

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot.FullName,
                    Version = "1.0.0",
                    ExcludeDirectories = Array.Empty<string>(),
                    ExcludeFiles = Array.Empty<string>(),
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = false
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = badArtefactRoot
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = runner.Plan(spec);
            Assert.True(plan.DeleteGeneratedStagingAfterRun);

            var ex = Record.Exception(() => runner.Run(spec, plan));
            Assert.NotNull(ex);

            var stagingPath = plan.BuildSpec.StagingPath;
            Assert.False(string.IsNullOrWhiteSpace(stagingPath));
            Assert.False(Directory.Exists(stagingPath!), $"Expected generated staging directory to be cleaned up: {stagingPath}");
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

