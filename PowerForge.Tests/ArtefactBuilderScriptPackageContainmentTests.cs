using Xunit;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderScriptClosureTests
{
    [Theory]
    [InlineData(ArtefactType.Script, true, true)]
    [InlineData(ArtefactType.Script, true, false)]
    [InlineData(ArtefactType.Script, false, true)]
    [InlineData(ArtefactType.Script, false, false)]
    [InlineData(ArtefactType.ScriptPacked, true, true)]
    [InlineData(ArtefactType.ScriptPacked, true, false)]
    [InlineData(ArtefactType.ScriptPacked, false, true)]
    [InlineData(ArtefactType.ScriptPacked, false, false)]
    public void Build_PackageIncludeDirectoryMustRemainInsideStagingBeforeOutputChanges(
        ArtefactType artefactType,
        bool includeAll,
        bool useRootedPath)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "ContainedPackageModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string outsideRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "outside")).FullName;
            File.WriteAllText(Path.Combine(outsideRoot, "escaped.ps1"), "'must not be packaged'");

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            string configuredPath = useRootedPath ? outsideRoot : Path.Combine("..", "outside");
            var information = new InformationConfiguration
            {
                IncludeAll = includeAll ? new[] { configuredPath } : Array.Empty<string>(),
                IncludePS1 = includeAll ? Array.Empty<string>() : new[] { configuredPath }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
                    CreateSegment(outputRoot, artefactType),
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    Array.Empty<RequiredModuleReference>(),
                    finalizePackedArtefact: null,
                    information,
                    delivery: null,
                    includeScriptFolders: true,
                    finalizedPayloadFiles: null));

            Assert.Contains("outside staging root", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
            Assert.Equal("preserve", File.ReadAllText(marker));
            Assert.True(File.Exists(Path.Combine(outsideRoot, "escaped.ps1")));
        }
        finally
        {
            Delete(root);
        }
    }
}
