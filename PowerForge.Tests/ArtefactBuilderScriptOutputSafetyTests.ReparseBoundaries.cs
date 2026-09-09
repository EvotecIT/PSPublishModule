using PowerForge;

namespace PowerForge.Tests;

public sealed partial class ArtefactBuilderScriptOutputSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_RejectsExternalMappingDestinationThroughSymlinkBeforeMutation(bool directoryMapping)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? linkPath = null;
        try
        {
            const string moduleName = "ExternalLinkedMappingModule";
            string projectRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "project")).FullName;
            string projectAssets = Directory.CreateDirectory(Path.Combine(projectRoot, "assets")).FullName;
            string stagingRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "staging")).FullName;
            WriteModule(stagingRoot, moduleName);
            string outputRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "output")).FullName;
            string outputMarker = Path.Combine(outputRoot, "existing.txt");
            File.WriteAllText(outputMarker, "preserve-output");
            string projectMarker = Path.Combine(projectAssets, "preserve.txt");
            File.WriteAllText(projectMarker, "preserve-project");
            string externalRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "external-destination")).FullName;
            linkPath = Path.Combine(externalRoot, "linked-assets");
            try
            {
                Directory.CreateSymbolicLink(linkPath, projectAssets);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            ConfigurationArtefactSegment segment = CreateSegment(outputRoot, ArtefactType.Script);
            segment.Configuration.DoNotClear = true;
            if (directoryMapping)
            {
                string source = Directory.CreateDirectory(Path.Combine(root.FullName, "directory-source")).FullName;
                File.WriteAllText(Path.Combine(source, "replacement.txt"), "replacement");
                segment.Configuration.DirectoryOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = Path.Combine(linkPath, "content") }];
            }
            else
            {
                string source = Path.Combine(root.FullName, "file-source.txt");
                File.WriteAllText(source, "replacement");
                segment.Configuration.FilesOutput =
                    [new ArtefactCopyMapping { Source = source, Destination = Path.Combine(linkPath, "config.json") }];
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Build(segment, projectRoot, stagingRoot, moduleName));

            Assert.Contains("copy destination", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve-project", File.ReadAllText(projectMarker));
            Assert.Equal("preserve-output", File.ReadAllText(outputMarker));
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
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void DestinationValidation_StopsAtTrustedBoundaryAboveSymlinkAlias()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string? linkPath = null;
        try
        {
            string physicalRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "physical-temp")).FullName;
            string trustedPhysicalBoundary = Directory.CreateDirectory(Path.Combine(physicalRoot, "trusted")).FullName;
            linkPath = Path.Combine(root.FullName, "temp-alias");
            try
            {
                Directory.CreateSymbolicLink(linkPath, physicalRoot);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            string trustedLexicalBoundary = Path.Combine(linkPath, Path.GetFileName(trustedPhysicalBoundary));
            string destination = Path.Combine(trustedLexicalBoundary, "package", "payload.ps1");

            ArtefactBuilder.ValidateScriptDestinationDoesNotTraverseReparsePoint(
                destination,
                trustedLexicalBoundary,
                "module package destination");
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
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
