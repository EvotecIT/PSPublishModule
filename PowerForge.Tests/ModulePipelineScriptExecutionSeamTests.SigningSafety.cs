namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Run_SignedRetainedScriptSignsOnlyBuildOwnedFiles()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            string project = Path.Combine(root.FullName, "Module");
            WriteMinimalModule(project, moduleName, "1.0.0");
            string output = Directory.CreateDirectory(Path.Combine(root.FullName, "Artefacts", "Script")).FullName;
            string retainedRoot = Path.Combine(output, "Caller.ps1");
            string retainedFolder = Directory.CreateDirectory(Path.Combine(output, "retained")).FullName;
            string retainedChild = Path.Combine(retainedFolder, "Caller.ps1");
            string copySource = Directory.CreateDirectory(Path.Combine(root.FullName, "copy-source")).FullName;
            string selectedSource = Path.Combine(copySource, "Selected.ps1");
            File.WriteAllText(selectedSource, "'selected'");
            File.WriteAllText(retainedRoot, "'caller root'");
            File.WriteAllText(retainedChild, "'caller child'");
            string mappedRoot = Directory.CreateDirectory(Path.Combine(output, "mapped")).FullName;
            string mappedFile = Path.Combine(mappedRoot, "Selected.ps1");
            string retainedSibling = Path.Combine(mappedRoot, "Caller.ps1");
            File.WriteAllText(retainedSibling, "'caller sibling'");
            var signed = new List<string>();
            var hosted = new FakeHostedOperations
            {
                AutoSuccessfulSigningResult = true,
                SigningFilesCompleted = (_, paths) => signed.AddRange(paths)
            };
            var runner = CreateRunner(hosted);
            var spec = CreateSignedPackedSpec(project, moduleName, output);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Last());
            artefact.ArtefactType = ArtefactType.Script;
            artefact.Configuration.DoNotClear = true;
            artefact.Configuration.ScriptName = "Invoke-TestModule.ps1";
            artefact.Configuration.DestinationFilesRelative = false;
            artefact.Configuration.FilesOutput =
            [
                new() { Source = copySource, Destination = retainedFolder },
                new() { Source = selectedSource, Destination = mappedFile }
            ];

            _ = runner.Run(spec, runner.Plan(spec));

            Assert.Contains(Path.Combine(output, "Invoke-TestModule.ps1"), signed);
            Assert.Contains(mappedFile, signed);
            Assert.DoesNotContain(retainedRoot, signed);
            Assert.DoesNotContain(retainedChild, signed);
            Assert.DoesNotContain(retainedSibling, signed);
            Assert.Equal("'caller root'", File.ReadAllText(retainedRoot));
            Assert.Equal("'caller child'", File.ReadAllText(retainedChild));
            Assert.Equal("'caller sibling'", File.ReadAllText(retainedSibling));
            Assert.Equal("'selected'", File.ReadAllText(mappedFile));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

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
