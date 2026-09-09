using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderScriptClosureTests
{
    [Theory]
    [InlineData(ArtefactType.Script, "Resources/config.json")]
    [InlineData(ArtefactType.Script, "Resources")]
    [InlineData(ArtefactType.ScriptPacked, "Resources/config.json")]
    [InlineData(ArtefactType.ScriptPacked, "Resources")]
    public void Build_FileMappingCannotReplaceSelectedPackageNamespaceBeforeOutputChanges(
        ArtefactType artefactType,
        string mappingDestination)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "FileMappingCollisionModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string selectedResources = Directory.CreateDirectory(Path.Combine(stagingRoot, "Resources")).FullName;
            File.WriteAllText(Path.Combine(selectedResources, "config.json"), "selected payload");
            string mappingSource = Path.Combine(root.FullName, "replacement.json");
            File.WriteAllText(mappingSource, "replacement");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.DestinationFilesRelative = true;
            segment.Configuration.FilesOutput =
            [
                new ArtefactCopyMapping
                {
                    Source = mappingSource,
                    Destination = mappingDestination
                }
            ];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildWithInformation(
                    root.FullName,
                    stagingRoot,
                    outputRoot,
                    moduleName,
                    artefactType,
                    CreateFocusedPackagingInformation(includeAll: "Resources"),
                    Array.Empty<RequiredModuleReference>(),
                    segment));

            Assert.Contains("file copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("packaged payload destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_DirectoryMappingCannotReplaceSelectedPackageDirectoryBeforeOutputChanges(
        ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "DirectoryMappingCollisionModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string selectedResources = Directory.CreateDirectory(Path.Combine(stagingRoot, "Resources")).FullName;
            File.WriteAllText(Path.Combine(selectedResources, "config.json"), "selected payload");
            string mappingSource = Directory.CreateDirectory(Path.Combine(root.FullName, "mapping-source")).FullName;
            File.WriteAllText(Path.Combine(mappingSource, "replacement.txt"), "replacement");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.DestinationDirectoriesRelative = true;
            segment.Configuration.DirectoryOutput =
            [
                new ArtefactCopyMapping
                {
                    Source = mappingSource,
                    Destination = "Resources"
                }
            ];

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildWithInformation(
                    root.FullName,
                    stagingRoot,
                    outputRoot,
                    moduleName,
                    artefactType,
                    CreateFocusedPackagingInformation(includeAll: "Resources"),
                    Array.Empty<RequiredModuleReference>(),
                    segment));

            Assert.Contains("directory copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("packaged payload destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }
}
