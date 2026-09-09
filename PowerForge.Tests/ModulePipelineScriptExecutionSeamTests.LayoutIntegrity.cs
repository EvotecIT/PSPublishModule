using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_SignedScriptArtefactRejectsEmptyDirectoryMutationAfterFinalization(bool removeDirectory)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string emptySource = Directory.CreateDirectory(Path.Combine(root.FullName, "empty-source")).FullName;
            var hostedOperations = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                ActionStarted = (_, context) =>
                {
                    string layoutRoot = Assert.Single(context.ArtefactPaths);
                    if (removeDirectory)
                        Directory.Delete(Path.Combine(layoutRoot, "support", "empty"));
                    else
                        Directory.CreateDirectory(Path.Combine(layoutRoot, "support", "unexpected-empty"));
                }
            };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(
                root.FullName,
                moduleName,
                Path.Combine(root.FullName, "Artefacts", "Script"));
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration!.DirectoryOutput =
            [
                new ArtefactCopyMapping
                {
                    Source = emptySource,
                    Destination = Path.Combine("support", "empty")
                }
            ];
            spec.Segments = spec.Segments.Concat(new IConfigurationSegment[]
            {
                new ConfigurationActionSegment
                {
                    Configuration = new ModulePipelineActionConfiguration
                    {
                        Name = "remove finalized empty directory",
                        At = ModulePipelineActionStage.AfterArtefacts,
                        InlineScript = "# executed through the test host"
                    }
                }
            }).ToArray();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("changed after signing", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                removeDirectory ? "empty" : "unexpected-empty",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
