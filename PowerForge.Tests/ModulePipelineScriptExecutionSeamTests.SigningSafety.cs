namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    public void Run_SignedScriptArtefactRejectsPreservedSymlinkBeforeSigningLayout()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Artefacts", "Script")).FullName;
            string externalRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "external")).FullName;
            string externalFile = Path.Combine(externalRoot, "outside.ps1");
            File.WriteAllText(externalFile, "'must remain unchanged'");
            string linkPath = Path.Combine(outputRoot, "preserved-link");
            try
            {
                Directory.CreateSymbolicLink(linkPath, externalRoot);
            }
            catch (Exception linkError) when (linkError is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            var hostedOperations = new FakeHostedOperations { AutoSuccessfulSigningResult = true };
            var runner = CreateRunner(hostedOperations);
            ModulePipelineSpec spec = CreateSignedPackedSpec(root.FullName, moduleName, outputRoot);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration!.DoNotClear = true;
            artefact.Configuration.ScriptName = "Invoke-TestModule.ps1";

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                runner.Run(spec, runner.Plan(spec)));

            Assert.Contains("symbolic links or reparse points", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, hostedOperations.SignCalls);
            Assert.Equal("'must remain unchanged'", File.ReadAllText(externalFile));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
