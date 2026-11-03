using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(ArtefactType.Packed)]
    [InlineData(ArtefactType.Unpacked)]
    public void Run_RejectsSharedOutputThatWouldOverwriteEarlierSigningEvidence(ArtefactType artefactType)
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
            first.ArtefactType = artefactType;
            first.Configuration.DoNotClear = artefactType == ArtefactType.Unpacked;
            first.Configuration.RequiredModules.ModulesPath = "First";
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationArtefactSegment
                {
                    ArtefactType = artefactType,
                    Configuration = new ArtefactConfiguration
                    {
                        ID = "second",
                        Enabled = true,
                        Path = outputRoot,
                        ArtefactName = "module-secondary.zip",
                        DoNotClear = artefactType == ArtefactType.Unpacked,
                        RequiredModules = new ArtefactRequiredModulesConfiguration { ModulesPath = "Second" }
                    }
                }
            }).ToArray();
            EnableGitHubPublish(spec, moduleName);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                runner.Run(spec, runner.Plan(spec)));

            Assert.Contains(artefactType == ArtefactType.Unpacked ? "unpacked signing evidence file" : "packed signing evidence file", exception.Message, StringComparison.OrdinalIgnoreCase);
            if (artefactType == ArtefactType.Packed)
                Assert.Contains("direct output files", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(outputRoot, "module.zip")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "module-secondary.zip")));
            Assert.False(Directory.Exists(Path.Combine(outputRoot, "First")));
            Assert.False(Directory.Exists(Path.Combine(outputRoot, "Second")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
