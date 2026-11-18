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
        UpdateFileDeclared(service,
            Path.Combine(windowsRoot, "index.json"),
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(windowsRoot, "windows-full.json"));
        UpdateFileDeclared(service,
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
            item => item.ComparisonId == "comparison-a" && item.Platform == "windows").Available);
        Assert.True(Assert.Single(
            merged.Availability,
            item => item.ComparisonId == "comparison-a" && item.Platform == "linux").Available);
        Assert.False(Assert.Single(
            merged.Availability,
            item => item.ComparisonId == "comparison-a" && item.Platform == "macos").Available);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsLegacySchemaOneCatalogs()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        UpdateFileDeclared(service,
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
        UpdateFileDeclared(service,
            sourceCatalogPath,
            result,
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: false,
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
        UpdateFileDeclared(service,
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
        UpdateFileDeclared(service,
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
        BenchmarkEvidenceCatalog recorded = UpdateFileDeclared(service,
            Path.Combine(sourceRoot, "index.json"),
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "windows-full.json"));
        File.AppendAllText(Path.Combine(sourceRoot, Assert.Single(recorded.Entries).ArtifactRelativePath), "tampered");

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
        BenchmarkRunResult firstResult = Result("Windows", "fixture-a", 10);
        BenchmarkRunResult secondResult = Result("Windows", "fixture-a", 12);
        secondResult.FinishedUtc = firstResult.FinishedUtc;
        UpdateFileDeclared(service,
            Path.Combine(firstRoot, "index.json"),
            firstResult,
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(firstRoot, "windows-full.json"));
        UpdateFileDeclared(service,
            Path.Combine(secondRoot, "index.json"),
            secondResult,
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
        BenchmarkEvidenceCatalog recorded = UpdateFileDeclared(service,
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows-full.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "windows-full.json"));
        string artifactPath = Path.Combine(sourceRoot, Assert.Single(recorded.Entries).ArtifactRelativePath);

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
    public void EvidenceCatalog_MergeFilesPublishesImmutableArtifactsBeforeCatalogSwitch()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        string sourceArtifactPath = Path.Combine(sourceRoot, "run.json");
        UpdateFileDeclared(service,
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
        UpdateFileDeclared(service,
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

    [Fact]
    public void EvidenceCatalog_MergeFilesPublishesRelativeResultDirectories()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        UpdateFileDeclared(service,
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "results/windows.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "run.json"));

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(outputRoot, "index.json"),
            [sourceCatalogPath]);

        BenchmarkEvidenceEntry entry = Assert.Single(merged.Entries);
        string expectedRelativePath = Path.Combine(
            "results",
            $"windows.{entry.ResultSha256}.json");
        Assert.Equal(
            expectedRelativePath.Replace('\\', '/'),
            entry.ResultPath.Replace('\\', '/'));
        Assert.Equal(
            expectedRelativePath.Replace('\\', '/'),
            entry.ArtifactRelativePath.Replace('\\', '/'));
        Assert.Equal(Path.GetFileName(expectedRelativePath), entry.ArtifactFileName);
        Assert.True(File.Exists(Path.Combine(outputRoot, expectedRelativePath)));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesPreservesImplicitRelativeArtifactDirectory()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        BenchmarkEvidenceCatalog source = UpdateFileDeclared(service,
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "results/windows.json",
            "full",
            publish: true);

        BenchmarkEvidenceEntry sourceEntry = Assert.Single(source.Entries);
        Assert.Equal(
            $"results/windows.{sourceEntry.ResultSha256}.json",
            sourceEntry.ArtifactRelativePath.Replace('\\', '/'));
        Assert.True(File.Exists(
            Path.Combine(sourceRoot, sourceEntry.ArtifactRelativePath)));

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(outputRoot, "index.json"),
            [sourceCatalogPath]);

        BenchmarkEvidenceEntry mergedEntry = Assert.Single(merged.Entries);
        Assert.True(File.Exists(
            Path.Combine(outputRoot, mergedEntry.ArtifactRelativePath)));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRejectsAmbiguousLegacyContentHashMatches()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        BenchmarkEvidenceCatalog recorded = UpdateFileDeclared(service,
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/benchmarks/windows.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "Run.json"));
        string sourceArtifactPath = Path.Combine(sourceRoot, Assert.Single(recorded.Entries).ArtifactRelativePath);
        File.Copy(
            sourceArtifactPath,
            Path.Combine(sourceRoot, "Copy.json"));
        BenchmarkEvidenceCatalog source =
            BenchmarkJson.Read<BenchmarkEvidenceCatalog>(sourceCatalogPath);
        source.Entries[0].ArtifactFileName = string.Empty;
        source.Entries[0].ArtifactRelativePath = string.Empty;
        source.Entries[0].ArtifactDestinationSha256 = new string('a', 64);
        BenchmarkJson.Write(sourceCatalogPath, source);

        Assert.Throws<FileNotFoundException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [sourceCatalogPath]));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesKeepsContentAddressedNamesStableOnRepeatedMerge()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        string outputCatalogPath = Path.Combine(outputRoot, "index.json");
        UpdateFileDeclared(service,
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "results/windows.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "run.json"));
        BenchmarkEvidenceCatalog first = service.MergeFiles(
            outputCatalogPath,
            [sourceCatalogPath]);
        string firstResultPath = Assert.Single(first.Entries).ResultPath;

        BenchmarkEvidenceCatalog second = service.MergeFiles(
            outputCatalogPath,
            [outputCatalogPath]);

        BenchmarkEvidenceEntry entry = Assert.Single(second.Entries);
        Assert.Equal(firstResultPath, entry.ResultPath);
        Assert.Equal(
            1,
            entry.ResultPath.Split(
                new[] { entry.ResultSha256 },
                StringSplitOptions.None).Length - 1);
        Assert.True(File.Exists(
            Path.Combine(outputRoot, entry.ArtifactRelativePath)));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesDecodesUriArtifactFileNames()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        UpdateFileDeclared(service,
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "https://benchmarks.example/data/my%20run.json",
            "full",
            publish: true,
            resultArtifactPath: Path.Combine(sourceRoot, "my run.json"));

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(outputRoot, "index.json"),
            [sourceCatalogPath]);

        BenchmarkEvidenceEntry entry = Assert.Single(merged.Entries);
        Assert.Equal(
            $"https://benchmarks.example/data/my%20run.{entry.ResultSha256}.json",
            entry.ResultPath);
        Assert.Equal($"my run.{entry.ResultSha256}.json", entry.ArtifactFileName);
        Assert.True(File.Exists(
            Path.Combine(outputRoot, entry.ArtifactRelativePath)));
    }

    [Theory]
    [InlineData("/data/benchmarks/result.json?download=1")]
    [InlineData("/data/benchmarks/result.json#latest")]
    [InlineData("/data/benchmarks/CON.json")]
    [InlineData("/data/benchmarks/result?.json")]
    [InlineData("/data/benchmarks/COM¹.json")]
    [InlineData("/data/benchmarks/COM².json")]
    [InlineData("/data/benchmarks/COM³.json")]
    [InlineData("/data/benchmarks/LPT¹.json")]
    [InlineData("/data/benchmarks/LPT².json")]
    [InlineData("/data/benchmarks/LPT³.json")]
    public void EvidenceCatalog_RejectsNonPortableResultPathsWhenRecorded(string resultPath)
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            UpdateFileDeclared(service,
                catalogPath,
                Result("Windows", "fixture-a", 10),
                "comparison-a",
                resultPath,
                "full",
                publish: true,
                resultArtifactPath: Path.Combine(sourceRoot, "artifact.json")));

        Assert.Contains("result path", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(catalogPath));
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRetainsImmutableArtifactWhenCatalogSwitchFails()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "merged");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string sourceCatalogPath = Path.Combine(sourceRoot, "index.json");
        BenchmarkEvidenceCatalog source = UpdateFileDeclared(service,
            sourceCatalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "result.json",
            "full",
            publish: true);
        BenchmarkEvidenceEntry sourceEntry = Assert.Single(source.Entries);
        string outputCatalogPath = Path.Combine(outputRoot, "index.json");
        Directory.CreateDirectory(outputCatalogPath);

        Assert.ThrowsAny<Exception>(() =>
            service.MergeFiles(
                outputCatalogPath,
                [sourceCatalogPath]));

        string expectedArtifactPath = Path.Combine(
            outputRoot,
            $"result.{sourceEntry.ResultSha256}.json");
        Assert.True(File.Exists(expectedArtifactPath));
        Assert.Equal(
            sourceEntry.ResultSha256,
            BenchmarkJson.ComputeFileSha256(expectedArtifactPath));
    }

    [Theory]
    [InlineData("https://benchmarks.example/data/")]
    [InlineData("results/")]
    [InlineData("results\\")]
    public void EvidenceCatalog_RejectsResultPathsEndingInSeparatorWhenRecorded(
        string resultPath)
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            UpdateFileDeclared(service,
                catalogPath,
                Result("Windows", "fixture-a", 10),
                "comparison-a",
                resultPath,
                "full",
                publish: true,
                resultArtifactPath: Path.Combine(sourceRoot, "artifact.json")));

        Assert.Contains("directory separator", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(catalogPath));
    }

    [Fact]
    public void EvidenceCatalog_RejectsResultNamesOverPortableUtf8ByteLimitWhenRecorded()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        string longName = new string('界', 70) + ".json";
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            UpdateFileDeclared(service,
                catalogPath,
                Result("Windows", "fixture-a", 10),
                "comparison-a",
                "https://benchmarks.example/data/" + longName,
                "full",
                publish: true,
                resultArtifactPath: Path.Combine(sourceRoot, "artifact.json")));

        Assert.Contains("portable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_UpdateFilePersistsFallbackCompletionTimestampForMerge()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.FinishedUtc = default;
        UpdateFileDeclared(service,
            catalogPath,
            result,
            "comparison-a",
            "windows.json",
            "full",
            publish: true);

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(root, "merged", "index.json"),
            [catalogPath]);

        Assert.NotEqual(default, result.FinishedUtc);
        Assert.Equal(result.FinishedUtc, Assert.Single(merged.Entries).GeneratedUtc);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesKeepsDiagnosticRunModeValidationConditional()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        UpdateFileDeclared(service,
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "windows.json",
            "quick",
            publish: false);

        BenchmarkEvidenceCatalog merged = service.MergeFiles(
            Path.Combine(root, "merged", "index.json"),
            [catalogPath]);

        BenchmarkEvidenceEntry entry = Assert.Single(merged.Entries);
        Assert.False(entry.Publish);
        Assert.Equal(BenchmarkEvidenceProfile.Diagnostic, entry.Profile);
    }

    [Fact]
    public void EvidenceCatalog_MergeFilesRevalidatesDurableSidecarWorkloadIdentity()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var service = new BenchmarkEvidenceCatalogService();
        string catalogPath = Path.Combine(sourceRoot, "index.json");
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["benchmark.provenance.source"] = "sidecar";
        result.Metadata["benchmark.provenance.runMode"] = "full";
        result.Metadata["benchmark.workload.id"] = "comparison-a";
        UpdateFileDeclared(service,
            catalogPath,
            result,
            "comparison-a",
            "windows.json",
            "full",
            publish: true);
        BenchmarkEvidenceCatalog catalog =
            BenchmarkJson.Read<BenchmarkEvidenceCatalog>(catalogPath);
        catalog.Entries[0].ComparisonId = "comparison-b";
        BenchmarkJson.Write(catalogPath, catalog);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.MergeFiles(
                Path.Combine(root, "merged", "index.json"),
                [catalogPath]));

        Assert.Contains("must exactly match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MergeCmdletLeavesExpectedPlatformsOptional()
    {
        var command = new PSPublishModule.MergeBenchmarkEvidenceCatalogCommand();

        Assert.Empty(command.SourcePath);
        Assert.Null(command.ExpectedPlatform);
    }
}
