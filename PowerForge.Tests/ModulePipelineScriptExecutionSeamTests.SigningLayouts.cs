using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void Plan_SignedScriptArtefactRejectsRequiredModulesOutsideOutputRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Script");
            string externalRequiredModules = Path.Combine(root.FullName, "external-required-modules");
            var runner = CreateRunner(new FakeHostedOperations { AutoSuccessfulSigningResult = true });
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration!.RequiredModules = new ArtefactRequiredModulesConfiguration
            {
                Enabled = true,
                Path = externalRequiredModules,
                ModulesPath = "app"
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec));

            Assert.Contains("signed Script artefact", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("required modules", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outputRoot));
            Assert.False(Directory.Exists(externalRequiredModules));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_UnsignedScriptArtefactPreservesSplitLayoutCompatibility()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Script");
            var runner = CreateRunner(new FakeHostedOperations());
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);
            var build = Assert.IsType<ConfigurationBuildSegment>(
                spec.Segments.Single(segment => segment is ConfigurationBuildSegment));
            build.BuildModule!.SignMerged = false;
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration!.RequiredModules = new ArtefactRequiredModulesConfiguration
            {
                Enabled = true,
                Path = Path.Combine(root.FullName, "external-required-modules"),
                ModulesPath = "app"
            };

            ModulePipelinePlan plan = runner.Plan(spec);

            Assert.False(plan.SignModule);
            Assert.Single(plan.Artefacts);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Plan_SignedScriptArtefactRejectsCopyDestinationOutsideOutputRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Path.Combine(root.FullName, "Artefacts", "Script");
            string externalDestination = Path.Combine(root.FullName, "external", "helper.ps1");
            var runner = CreateRunner(new FakeHostedOperations { AutoSuccessfulSigningResult = true });
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration!.FilesOutput = new[]
            {
                new ArtefactCopyMapping
                {
                    Source = Path.Combine(root.FullName, moduleName + ".psm1"),
                    Destination = externalDestination
                }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => runner.Plan(spec));

            Assert.Contains("signed Script artefact", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("file copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(outputRoot));
            Assert.False(Directory.Exists(Path.GetDirectoryName(externalDestination)!));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
