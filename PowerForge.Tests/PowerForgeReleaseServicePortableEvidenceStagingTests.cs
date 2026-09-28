namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("Portable", ".zip", "portable")]
    [InlineData("Tool", ".exe", "tools")]
    public void StageReleaseAssets_DetachedEvidenceFollowsRenamedRunnableArtifact(
        string categoryName, string extension, string directory)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var category = Enum.Parse<PowerForgeReleaseAssetCategory>(categoryName);
            string artifact = Path.Combine(root, "original" + extension);
            string[] sources = { artifact, artifact + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                artifact + PowerForgePortablePayloadInventory.DirectSignatureSuffix };
            foreach (string source in sources) File.WriteAllText(source, Path.GetFileName(source));
            var entries = sources.Select((source, index) => new PowerForgeReleaseAssetEntry
            {
                Path = source, Category = index == 0 ? category : PowerForgeReleaseAssetCategory.Metadata,
                Source = "DotNetPublish", Target = "Sample", Version = "1.2.3", Runtime = "win-x64"
            }).ToArray();
            var options = new PowerForgeReleaseStagingOptions
            {
                PortableNameTemplate = "{Target}-{Version}-{Runtime}{Extension}",
                ToolsNameTemplate = "{Target}-{Version}-{Runtime}{Extension}",
                MetadataPath = "separate-metadata", MetadataNameTemplate = "metadata-{FileName}"
            };
            var staged = PowerForgeReleaseService.StageReleaseAssets(entries, Path.Combine(root, "staged"),
                options, null, null).ToArray();
            string expected = Path.Combine(root, "staged", directory, "Sample-1.2.3-win-x64" + extension);
            Assert.Equal(expected, staged[0].StagedPath);
            for (int index = 0; index < staged.Length; index++)
            {
                string suffix = index == 0 ? "" : index == 1
                    ? PowerForgePortablePayloadInventory.DirectInventorySuffix : PowerForgePortablePayloadInventory.DirectSignatureSuffix;
                Assert.Equal(expected + suffix, staged[index].StagedPath);
                Assert.Equal(File.ReadAllBytes(sources[index]), File.ReadAllBytes(staged[index].StagedPath!));
                Assert.NotEmpty(staged[index].StagedSha256!);
            }
            Assert.All(staged.Skip(1), entry => Assert.Equal(PowerForgeReleaseAssetCategory.Metadata, entry.Category));
            string catalog = ModulePublisher.WriteGitHubChecksumCatalog(Path.Combine(root, "SHA256SUMS.txt"),
                staged.Select(entry => entry.StagedPath!).ToList());
            Assert.Equal(3, File.ReadAllLines(catalog).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TryBuildDotNetGitHubRunnableAssets_SignedZipIncludesDetachedEvidence()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string archive = Path.Combine(root, "Sample.zip");
            string[] expected = { archive, archive + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                archive + PowerForgePortablePayloadInventory.DirectSignatureSuffix };
            foreach (string path in expected) File.WriteAllText(path, Path.GetFileName(path));
            var target = new DotNetPublishTargetPlan { Name = "Sample", Publish = new DotNetPublishPublishOptions { Zip = true } };
            var plan = new DotNetPublishPlan { Targets = new[] { target } };
            var result = new DotNetPublishResult
            {
                ChecksumsPath = Path.Combine(root, "SHA256SUMS.txt"),
                Artefacts = new[] { new DotNetPublishArtefactResult
                {
                    Category = DotNetPublishArtefactCategory.Publish, Target = "Sample", Framework = "net10.0",
                    Runtime = "win-x64", Style = DotNetPublishStyle.PortableCompat, ZipPath = archive, SignedFiles = 1
                } }
            };
            Assert.True(PowerForgeReleaseService.TryBuildDotNetGitHubRunnableAssets(plan, target, result,
                out List<string> assets, out _, out _, out string? error), error);
            Assert.Equal(expected.OrderBy(path => path), assets.OrderBy(path => path));
            string catalog = ModulePublisher.WriteGitHubChecksumCatalog(result.ChecksumsPath, assets);
            Assert.Equal(3, File.ReadAllLines(catalog).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
