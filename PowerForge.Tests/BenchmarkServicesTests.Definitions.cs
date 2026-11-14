using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void EvidenceCatalog_DeclaredCoverageSurvivesSubsetMergeAndSchemaThreeImport()
    {
        string root = CreateTempRoot();
        var service = new BenchmarkEvidenceCatalogService();
        string source = Path.Combine(root, "source", "index.json");
        BenchmarkRunResult initialResult = Result("Windows", "fixture-a", 10);
        initialResult.FinishedUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        initialResult.StartedUtc = initialResult.FinishedUtc.AddMinutes(-1);
        service.UpdateFile(source, initialResult, "comparison-a",
            "/data/a.json", "full", true, resultArtifactPath: Path.Combine(root, "source", "a.json"));
        BenchmarkEvidenceCatalog legacy = BenchmarkJson.Read<BenchmarkEvidenceCatalog>(source);
        legacy.SchemaVersion = 3;
        BenchmarkJson.Write(source, legacy);
        var declarations = new[]
        {
            new BenchmarkComparisonDefinition { ComparisonId = "comparison-a", Area = "pdf", Title = "PDF merge", Libraries = ["OfficeIMO", "Other"] },
            new BenchmarkComparisonDefinition { ComparisonId = "never-run", Area = "word", Title = "Word create", Libraries = ["OfficeIMO", "Other"], ExpectedPlatforms = ["windows", "macos"] }
        };
        string destination = Path.Combine(root, "merged", "index.json");
        BenchmarkEvidenceCatalog merged = service.MergeFiles(destination, [source], expectedPlatforms: null, comparisons: declarations);
        Assert.Equal(4, merged.SchemaVersion);
        Assert.Equal(new[] { "pdf", "word" }, merged.Areas);
        Assert.Equal(2, merged.Comparisons.Length);
        BenchmarkEvidenceEntry retained = Assert.Single(merged.Entries);
        Assert.Equal("pdf", retained.Area);
        Assert.Equal(legacy.Entries[0].ResultSha256, retained.ResultSha256);
        Assert.Equal(legacy.Entries[0].Environment.ProcessorName, retained.Environment.ProcessorName);
        Assert.Equal(2, merged.Availability.Count(value => value.ComparisonId == "never-run"));
        Assert.All(merged.Availability.Where(value => value.ComparisonId == "never-run"), value => Assert.False(value.Available));

        string incoming = Path.Combine(root, "incoming", "index.json");
        service.UpdateFile(incoming, Result("Linux", "fixture-a", 11), "comparison-a",
            "/data/linux.json", "full", true, resultArtifactPath: Path.Combine(root, "incoming", "linux.json"));
        BenchmarkEvidenceCatalog updated = service.MergeFiles(destination, [destination, incoming]);
        Assert.Equal(2, updated.Entries.Length);
        Assert.Equal(2, updated.Comparisons.Length);
        Assert.Equal(retained.ResultSha256, Assert.Single(updated.Entries, value => value.Platform == "windows").ResultSha256);
        Assert.All(updated.Entries, value => Assert.Equal("pdf", value.Area));

        // A fresh Windows-only run replaces that lane, leaving Linux and declared gaps intact.
        string refresh = Path.Combine(root, "refresh", "index.json");
        service.UpdateFile(refresh, Result("Windows", "fixture-a", 7), "comparison-a",
            "fresh.json", "full", true, resultArtifactPath: Path.Combine(root, "refresh", "fresh.json"));
        BenchmarkEvidenceCatalog refreshed = service.MergeFiles(destination, [destination, refresh], null, null,
            "/data/benchmarks/library-comparisons");
        Assert.Equal(2, refreshed.Entries.Length);
        Assert.All(refreshed.Entries, value => Assert.StartsWith("/data/benchmarks/library-comparisons/", value.ResultPath));
        string refreshedHash = Assert.Single(refreshed.Entries, value => value.Platform == "windows").ResultSha256;
        Assert.NotEqual(retained.ResultSha256, refreshedHash);
        BenchmarkEvidenceCatalog replayed = service.MergeFiles(destination, [destination, refresh], null, null,
            "/data/benchmarks/library-comparisons");
        Assert.Equal(refreshedHash, Assert.Single(replayed.Entries, value => value.Platform == "windows").ResultSha256);
        Assert.Equal(2, replayed.Comparisons.Length);
    }

    [Fact]
    public void EvidenceCatalog_RejectsConflictingDeclarationsBeforeReplacingDestination()
    {
        string root = CreateTempRoot();
        string source = Path.Combine(root, "source.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkJson.Write(source, new BenchmarkEvidenceCatalog());
        string destination = Path.Combine(root, "destination.json");
        BenchmarkJson.Write(destination, new BenchmarkEvidenceCatalog());
        byte[] original = File.ReadAllBytes(destination);
        var declarations = new[]
        {
            new BenchmarkComparisonDefinition { ComparisonId = "a", Area = "pdf", Title = "First", Libraries = ["OfficeIMO"] },
            new BenchmarkComparisonDefinition { ComparisonId = "a", Area = "word", Title = "Second", Libraries = ["OfficeIMO"] }
        };
        Assert.Throws<InvalidOperationException>(() => service.MergeFiles(destination, [source], expectedPlatforms: null, comparisons: declarations));
        Assert.Equal(original, File.ReadAllBytes(destination));
    }

    [Fact]
    public void EvidenceCatalog_MergesPlatformSetsIndependentOfOrderAndKeepsOriginalOverload()
    {
        string root = CreateTempRoot();
        var service = new BenchmarkEvidenceCatalogService();
        string first = Path.Combine(root, "first.json");
        string second = Path.Combine(root, "second.json");
        BenchmarkJson.Write(first, new BenchmarkEvidenceCatalog
        {
            Comparisons = [new BenchmarkComparisonDefinition { ComparisonId = "a", Area = "pdf", Title = "PDF", Libraries = ["OfficeIMO"], ExpectedPlatforms = ["windows", "linux", "macos"] }]
        });
        BenchmarkJson.Write(second, new BenchmarkEvidenceCatalog
        {
            Comparisons = [new BenchmarkComparisonDefinition { ComparisonId = "a", Area = "pdf", Title = "PDF", Libraries = ["OfficeIMO"], ExpectedPlatforms = ["linux", "macos", "windows"] }]
        });
        BenchmarkEvidenceCatalog merged = service.MergeFiles(Path.Combine(root, "merged.json"), [first, second]);
        Assert.Single(merged.Comparisons);
        Assert.Equal(3, merged.Availability.Length);
        Assert.NotNull(typeof(BenchmarkEvidenceCatalogService).GetMethod("MergeFiles",
            [typeof(string), typeof(IEnumerable<string>), typeof(IEnumerable<string>)]));
    }
}
