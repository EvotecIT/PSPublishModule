using System.IO.Compression;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderScriptClosureTests
{
    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RejectsSelectedPackageSourceSymlinkBeforeOutputChanges(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? linkPath = null;
        try
        {
            const string moduleName = "LinkedPackageSourceModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string externalRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "external-assets")).FullName;
            string externalFile = Path.Combine(externalRoot, "outside.txt");
            File.WriteAllText(externalFile, "must not be packaged");
            linkPath = Path.Combine(stagingRoot, "LinkedAssets");
            try
            {
                Directory.CreateSymbolicLink(linkPath, externalRoot);
            }
            catch (Exception linkError) when (linkError is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            var information = CreateFocusedPackagingInformation(includeAll: "LinkedAssets");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BuildWithInformation(
                    root.FullName,
                    stagingRoot,
                    outputRoot,
                    moduleName,
                    artefactType,
                    information,
                    Array.Empty<RequiredModuleReference>()));

            Assert.Contains("package source directory", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(marker));
            Assert.Equal("must not be packaged", File.ReadAllText(externalFile));
        }
        finally
        {
            if (linkPath is not null)
            {
                try
                {
                    if (Directory.Exists(linkPath) &&
                        (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(linkPath);
                    }
                }
                catch { }
            }
            Delete(root);
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void Build_RequiredModuleCannotReplaceSelectedPackageDirectoryBeforeOutputChanges(
        ArtefactType artefactType)
    {
        var root = CreateRoot();
        try
        {
            const string moduleName = "RequiredPackageCollisionModule";
            const string dependencyName = "Dependency";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            string selectedDependency = Directory.CreateDirectory(Path.Combine(stagingRoot, dependencyName)).FullName;
            File.WriteAllText(Path.Combine(selectedDependency, "payload.txt"), "selected payload");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.RequiredModules.Enabled = true;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
                    segment,
                    root.FullName,
                    stagingRoot,
                    moduleName,
                    "1.0.0",
                    null,
                    [new RequiredModuleReference(dependencyName)],
                    finalizePackedArtefact: null,
                    CreateFocusedPackagingInformation(includeAll: dependencyName),
                    delivery: null,
                    includeScriptFolders: true,
                    finalizedPayloadFiles: null));

            Assert.Contains("required module destination", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Build_ScriptNamePackageCollisionUsesOutputFileSystemSemantics(ArtefactType artefactType)
    {
        var root = CreateRoot();
        string? extractionRoot = null;
        try
        {
            const string moduleName = "OutputCaseModule";
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteScriptModule(stagingRoot, moduleName);
            File.WriteAllText(Path.Combine(stagingRoot, "Tool.ps1"), "'selected payload'");
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string marker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(marker, "preserve");
            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, artefactType);
            segment.Configuration.ScriptName = "tool.ps1";
            var information = CreateFocusedPackagingInformation(includeRoot: "Tool.ps1");
            bool outputIsCaseInsensitive =
                FrameworkCompatibility.GetPathStringComparisonForPath(outputRoot) == StringComparison.OrdinalIgnoreCase;

            if (outputIsCaseInsensitive)
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    BuildWithInformation(
                        root.FullName,
                        stagingRoot,
                        outputRoot,
                        moduleName,
                        artefactType,
                        information,
                        Array.Empty<RequiredModuleReference>(),
                        segment));

                Assert.Contains("ScriptName", exception.Message, StringComparison.Ordinal);
                Assert.Contains("output filesystem", exception.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("preserve", File.ReadAllText(marker));
                return;
            }

            ArtefactBuildResult result = BuildWithInformation(
                root.FullName,
                stagingRoot,
                outputRoot,
                moduleName,
                artefactType,
                information,
                Array.Empty<RequiredModuleReference>(),
                segment);
            string inspectionRoot = result.OutputPath;
            if (artefactType == ArtefactType.ScriptPacked)
            {
                extractionRoot = Path.Combine(root.FullName, "extracted");
                ZipFile.ExtractToDirectory(result.OutputPath, extractionRoot);
                inspectionRoot = extractionRoot;
            }

            Assert.Equal("'selected payload'", File.ReadAllText(Path.Combine(inspectionRoot, "Tool.ps1")));
            Assert.True(File.Exists(Path.Combine(inspectionRoot, "tool.ps1")));
        }
        finally
        {
            if (extractionRoot is not null)
            {
                try { Directory.Delete(extractionRoot, recursive: true); } catch { }
            }
            Delete(root);
        }
    }

    private static ArtefactBuildResult BuildWithInformation(
        string projectRoot,
        string stagingRoot,
        string outputRoot,
        string moduleName,
        ArtefactType artefactType,
        InformationConfiguration information,
        IReadOnlyList<RequiredModuleReference> requiredModules,
        ConfigurationArtefactSegment? segment = null)
        => new ArtefactBuilder(new NullLogger()).BuildWithFinalizer(
            segment ?? CreateSegment(outputRoot, artefactType),
            projectRoot,
            stagingRoot,
            moduleName,
            "1.0.0",
            null,
            requiredModules,
            finalizePackedArtefact: null,
            information,
            delivery: null,
            includeScriptFolders: true,
            finalizedPayloadFiles: null);

    private static InformationConfiguration CreateFocusedPackagingInformation(
        string? includeRoot = null,
        string? includeAll = null)
        => new()
        {
            IncludeRoot =
            [
                "*.psd1",
                "*.psm1",
                .. string.IsNullOrWhiteSpace(includeRoot) ? Array.Empty<string>() : [includeRoot]
            ],
            IncludePS1 = ["MissingScriptFolder"],
            IncludeAll = string.IsNullOrWhiteSpace(includeAll) ? ["MissingAssetFolder"] : [includeAll]
        };
}
