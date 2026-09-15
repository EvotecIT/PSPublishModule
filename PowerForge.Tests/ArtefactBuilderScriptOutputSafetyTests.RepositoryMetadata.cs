using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderScriptOutputSafetyTests
{
    [Theory]
    [InlineData(ArtefactType.Script, ".git")]
    [InlineData(ArtefactType.ScriptPacked, ".git")]
    [InlineData(ArtefactType.Script, ".hg/store")]
    [InlineData(ArtefactType.ScriptPacked, ".svn/pristine")]
    public void Build_RejectsRepositoryMetadataOutputRootBeforeMutation(
        ArtefactType artefactType,
        string metadataPath)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "MetadataSafetyModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(projectRoot, metadataPath)).FullName;
            string marker = Path.Combine(outputRoot, "preserve.txt");
            File.WriteAllText(marker, "preserve-metadata");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(CreateSegment(outputRoot, artefactType), projectRoot, stagingRoot, moduleName));

            Assert.Contains("repository metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-metadata", File.ReadAllText(marker));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
