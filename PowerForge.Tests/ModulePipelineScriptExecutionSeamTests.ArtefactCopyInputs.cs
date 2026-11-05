using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Plan_DirectoryOnlyMappingsPreserveReleaseInputSelection()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            string module = Path.Combine(root.FullName, "Module");
            WriteMinimalModule(module, moduleName, "1.0.0");
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "Examples/\nArtefacts/\n");
            RunGit(root.FullName, "init", "--quiet");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "add", ".");
            RunGit(root.FullName, "commit", "--quiet", "-m", "fixture");
            string source = Directory.CreateDirectory(Path.Combine(root.FullName, "Examples", "GPO")).FullName;
            string empty = Directory.CreateDirectory(Path.Combine(source, "empty")).FullName;
            string unselected = Path.Combine(source, "unselected.xml");
            File.WriteAllText(unselected, "not selected");
            var spec = CreateSignedPackedSpec(module, moduleName, Path.Combine(root.FullName, "Artefacts", "Packed"));
            EnableGitHubPublish(spec, moduleName);
            var artefact = Assert.IsType<ConfigurationArtefactSegment>(spec.Segments.Single(segment => segment is ConfigurationArtefactSegment));
            artefact.Configuration.FilesOutput =
            [
                new() { Source = source, Destination = "backup" },
                new() { Source = empty, Destination = "backup/empty" }
            ];

            var plan = CreateRunner(new FakeHostedOperations()).Plan(spec);

            Assert.True(plan.RequireCleanReleaseSource);
            Assert.DoesNotContain(source, plan.SourceInputPaths);
            Assert.DoesNotContain(empty, plan.SourceInputPaths);
            Assert.DoesNotContain(unselected, plan.SourceInputPaths);
            var result = new ArtefactBuilder(new NullLogger()).Build(plan.Artefacts.Single(), plan.ProjectRoot,
                module, moduleName, "1.0.0", null, []);
            using var archive = ZipFile.OpenRead(result.OutputPath);
            Assert.NotNull(archive.GetEntry("backup/empty/"));
            Assert.Null(archive.GetEntry("backup/unselected.xml"));

            // Selecting the ignored file still requires a clean release input.
            artefact.Configuration.FilesOutput = artefact.Configuration.FilesOutput
                .Append(new ArtefactCopyMapping { Source = unselected, Destination = "backup/selected.xml" })
                .ToArray();
            var error = Assert.Throws<InvalidOperationException>(() => CreateRunner(new FakeHostedOperations()).Plan(spec));
            Assert.Contains("clean release inputs", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
                if (file.IsReadOnly)
                    file.IsReadOnly = false;
            root.Delete(recursive: true);
        }
    }
}
