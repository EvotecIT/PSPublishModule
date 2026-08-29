using System.Reflection.PortableExecutable;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadSourceProvenance_RejectsGeneratedOutputWithModifiedPeHeader(bool checksum)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            (string appProject, string libraryProject, string libraryOutput) =
                CreateWave40EmbeddedProjectFixture(root, packageReferenceXml: null);
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            byte[] image = File.ReadAllBytes(libraryOutput);
            int peHeaderStart;
            using (var stream = File.OpenRead(libraryOutput))
            using (var reader = new PEReader(stream))
                peHeaderStart = reader.PEHeaders.PEHeaderStartOffset;
            int offset = checksum ? peHeaderStart + 64 : peHeaderStart - 16;
            image[offset] ^= 0x01;
            File.WriteAllBytes(libraryOutput, image);

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void DotNetToolchainDiscovery_DerivesCustomRootFromActiveRuntimeDirectory()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string customRoot = Path.Combine(root, ".dotnet");
            string runtimeDirectory = Path.Combine(
                customRoot,
                "shared",
                "Microsoft.NETCore.App",
                "10.0.1");

            Assert.Equal(
                Path.GetFullPath(customRoot),
                DotNetPublishPipelineRunner.TryGetDotNetRootFromRuntimeDirectory(runtimeDirectory));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
