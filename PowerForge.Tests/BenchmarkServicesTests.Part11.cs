using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void EvidenceCatalog_MergeFilesConsolidatesIndependentPlatformBundles()
    {
        string root = CreateTempRoot();
        string windowsRoot = Path.Combine(root, "windows");
        string linuxRoot = Path.Combine(root, "linux");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(windowsRoot);
        Directory.CreateDirectory(linuxRoot);
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            Path.Combine(windowsRoot, "index.json"),
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(windowsRoot, "windows-full.json"));
        service.UpdateFile(
            Path.Combine(linuxRoot, "index.json"),
            Result("Linux", "fixture-a", 11),
            "comparison-a",
            "/data/benchmarks/linux-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(linuxRoot, "linux-full.json"));

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(outputRoot, "index.json"),
            [
                Path.Combine(windowsRoot, "index.json"),
                Path.Combine(linuxRoot, "index.json")
            ]);

        Assert.Equal(2, merged.Entries.Length);
        Assert.All(merged.Entries, entry => Assert.True(entry.Publish));
        Assert.All(merged.Entries, entry => Assert.True(entry.Comparable));
        Assert.All(merged.Entries, entry =>
        {
            Assert.Contains(
                entry.ResultSha256,
                entry.ArtifactFileName,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(outputRoot, entry.ArtifactFileName)));
        });
        Assert.True(Assert.Single(
            merged.Availability,
            item => item.Platform == "windows").Available);
        Assert.True(Assert.Single(
            merged.Availability,
            item => item.Platform == "linux").Available);
        Assert.False(Assert.Single(
            merged.Availability,
            item => item.Platform == "macos").Available);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsLegacySchemaOneCatalogs()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        service.UpdateFile(
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "windows-full.json"));
        BenchmarkEvidenceCatalog source =
            BenchmarkJson.Read<BenchmarkEvidenceCatalog>(sourceCatalogPath);
        source.SchemaVersion = 1;
        BenchmarkJson.Write(sourceCatalogPath, source);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [sourceCatalogPath]));

        Assert.Contains("schema 1", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(root, "merged", "index.json")));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesPreservesExplicitPlatformForMetadataFreeArtifacts()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Environment.OsFamily = string.Empty;
        result.Environment.OsDescription = string.Empty;
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        service.UpdateFile(
            sourceCatalogPath,
            result,
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            platform: "windows",
            resultArtifactPath: Path.Combine(sourceRoot, "windows-full.json"));

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(root, "merged", "index.json"),
            [sourceCatalogPath]);

        Assert.Equal("windows", Assert.Single(merged.Entries).Platform);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsExplicitPlatformConflictingWithArtifactMetadata()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        service.UpdateFile(
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "windows-full.json"));
        BenchmarkEvidenceCatalog source =
            BenchmarkJson.Read<BenchmarkEvidenceCatalog>(sourceCatalogPath);
        source.Entries[0].Platform = "linux";
        BenchmarkJson.Write(sourceCatalogPath, source);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [sourceCatalogPath]));

        Assert.Contains("one operating-system platform", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesUsesPersistedExplicitArtifactFileName()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        string explicitArtifactPath = Path.Combine(sourceRoot, "run.json");
        service.UpdateFile(
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: explicitArtifactPath);
        File.Delete(Path.Combine(sourceRoot, "windows-full.json"));

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(root, "merged", "index.json"),
            [sourceCatalogPath]);

        BenchmarkEvidenceEntry entry = Assert.Single(merged.Entries);
        Assert.True(File.Exists(Path.Combine(root, "merged", entry.ArtifactFileName)));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsTamperedArtifactWithoutCreatingOutput()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputPath = Path.Combine(root, "merged", "index.json");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string artifactPath = Path.Combine(sourceRoot, "windows-full.json");
        service.UpdateFile(
            Path.Combine(sourceRoot, "index.json"),
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: artifactPath);
        File.AppendAllText(artifactPath, "tampered");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                outputPath,
                [Path.Combine(sourceRoot, "index.json")]));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsConflictingCopiesOfSameLane()
    {
        string root = CreateTempRoot();
        var service = new BenchmarkEvidenceCatalogService();
        string firstRoot = Path.Combine(root, "first");
        string secondRoot = Path.Combine(root, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        service.UpdateFile(
            Path.Combine(firstRoot, "index.json"),
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(firstRoot, "windows-full.json"));
        service.UpdateFile(
            Path.Combine(secondRoot, "index.json"),
            Result("Windows", "fixture-a", 12),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(secondRoot, "windows-full.json"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [
                    Path.Combine(firstRoot, "index.json"),
                    Path.Combine(secondRoot, "index.json")
                ]));

        Assert.Contains("conflicting copies", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRevalidatesPublishedResultContents()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        string artifactPath = Path.Combine(sourceRoot, "windows-full.json");
        service.UpdateFile(
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: artifactPath);

        BenchmarkRunResult result = BenchmarkJson.Read<BenchmarkRunResult>(artifactPath);
        result.Metadata["gitWorktreeClean"] = "false";
        BenchmarkJson.Write(artifactPath, result);
        BenchmarkEvidenceCatalog catalog = BenchmarkJson.Read<BenchmarkEvidenceCatalog>(catalogPath);
        catalog.Entries[0].ResultSha256 = BenchmarkJson.ComputeSha256(result);
        BenchmarkJson.Write(catalogPath, catalog);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [catalogPath]));

        Assert.Contains("gitWorktreeClean", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsDestinationArtifactCollisions()
    {
        string root = CreateTempRoot();
        string windowsRoot = Path.Combine(root, "windows");
        string linuxRoot = Path.Combine(root, "linux");
        Directory.CreateDirectory(windowsRoot);
        Directory.CreateDirectory(linuxRoot);
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            Path.Combine(windowsRoot, "index.json"),
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/windows/result.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(windowsRoot, "result.json"));
        service.UpdateFile(
            Path.Combine(linuxRoot, "index.json"),
            Result("Linux", "fixture-a", 11),
            "comparison-a",
            "/linux/result.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(linuxRoot, "result.json"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [
                    Path.Combine(windowsRoot, "index.json"),
                    Path.Combine(linuxRoot, "index.json")
                ]));

        Assert.Contains("same bundle artifact", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsCaseOnlyDestinationArtifactCollisions()
    {
        string root = CreateTempRoot();
        string firstRoot = Path.Combine(root, "first");
        string secondRoot = Path.Combine(root, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            Path.Combine(firstRoot, "index.json"),
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/windows/result.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(firstRoot, "result.json"));
        service.UpdateFile(
            Path.Combine(secondRoot, "index.json"),
            Result("Linux", "fixture-a", 11),
            "comparison-a",
            "/linux/RESULT.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(secondRoot, "RESULT.json"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [
                    Path.Combine(firstRoot, "index.json"),
                    Path.Combine(secondRoot, "index.json")
                ]));

        Assert.Contains("same bundle artifact", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsArtifactThatWouldOverwriteCatalog()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "catalog.json");
        service.UpdateFile(
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/index.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "index.json"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [sourceCatalogPath]));

        Assert.Contains("overwrite the destination catalog", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsCaseOnlyCatalogArtifactCollision()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "catalog.json");
        service.UpdateFile(
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/INDEX.JSON",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "INDEX.JSON"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [sourceCatalogPath]));

        Assert.Contains("overwrite the destination catalog", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesPublishesImmutableArtifactsBeforeCatalogSwitch()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        string sourceArtifactPath = Path.Combine(sourceRoot, "run.json");
        service.UpdateFile(
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: sourceArtifactPath);
        BenchmarkEvidenceCatalog first = service.MergeFiles(
            Path.Combine(outputRoot, "index.json"),
            [sourceCatalogPath]);
        BenchmarkEvidenceEntry firstEntry = Assert.Single(first.Entries);
        string firstArtifactPath = Path.Combine(outputRoot, firstEntry.ArtifactFileName);
        byte[] firstBytes = File.ReadAllBytes(firstArtifactPath);

        BenchmarkRunResult replacement = Result("Windows", "fixture-a", 20);
        replacement.FinishedUtc = replacement.FinishedUtc.AddMinutes(1);
        service.UpdateFile(
            sourceCatalogPath,
            replacement,
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: sourceArtifactPath);
        BenchmarkEvidenceCatalog second = service.MergeFiles(
            Path.Combine(outputRoot, "index.json"),
            [sourceCatalogPath]);
        BenchmarkEvidenceEntry secondEntry = Assert.Single(second.Entries);

        Assert.NotEqual(firstEntry.ArtifactFileName, secondEntry.ArtifactFileName);
        Assert.Equal(firstBytes, File.ReadAllBytes(firstArtifactPath));
        Assert.True(File.Exists(Path.Combine(outputRoot, secondEntry.ArtifactFileName)));
    }

    [Theory]
    [InlineData("/data/benchmarks/result.json?download=1")]
    [InlineData("/data/benchmarks/result.json#latest")]
    [InlineData("/data/benchmarks/CON.json")]
    [InlineData("/data/benchmarks/result?.json")]
    public void EvidenceCatalog_MergeFilesRejectsNonPortableArtifactNames(string resultPath)
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        service.UpdateFile(
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            resultPath,
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "artifact.json"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [catalogPath]));

        Assert.Contains("result path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeCmdletLeavesExpectedPlatformsOptional()
    {
        var command = new PSPublishModule.MergeBenchmarkEvidenceCatalogCommand();

        Assert.Empty(command.SourcePath);
        Assert.Null(command.ExpectedPlatform);
    }
}
