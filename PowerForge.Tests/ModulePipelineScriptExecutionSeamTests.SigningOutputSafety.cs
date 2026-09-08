using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void Run_RejectsSharedPackedOutputThatWouldDeleteEarlierSigningEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "remote", "add", "origin", "https://github.com/EvotecIT/TestModule.git");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");

            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Packed");
            var runner = CreateRunner(new FakeHostedOperations { AutoSuccessfulSigningResult = true });
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);
            ConfigurationArtefactSegment first = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            first.Configuration!.ID = "first";
            first.Configuration.ArtefactName = "module.zip";
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        ID = "second",
                        Enabled = true,
                        Path = outputRoot,
                        ArtefactName = "module-secondary.zip"
                    }
                }
            }).ToArray();
            EnableGitHubPublish(spec, moduleName);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("packed signing evidence file", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("direct output files", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(outputRoot, "module.zip")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "module-secondary.zip")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
