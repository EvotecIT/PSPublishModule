using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void EvidenceCatalog_DeclaredCoverageSurvivesSubsetMergeAndKeepsSupersededRuns()
    {
        string root = CreateTempRoot();
        var service = new BenchmarkEvidenceCatalogService();
        string source = Path.Combine(root, "source", "index.json");
        BenchmarkRunResult initialResult = Result("Windows", "fixture-a", 10);
        initialResult.FinishedUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        initialResult.StartedUtc = initialResult.FinishedUtc.AddMinutes(-1);
        UpdateFileDeclared(service, source, initialResult, "comparison-a",
            "/data/a.json", "full", true, resultArtifactPath: Path.Combine(root, "source", "a.json"));
        BenchmarkDeclarationDocument declarations = TestDeclarations();
        declarations.Comparisons =
        [
            .. declarations.Comparisons,
            new BenchmarkComparisonDefinition { ComparisonId = "never-run", Area = "word", Title = "Word create", Subject = "OfficeIMO", Libraries = ["OfficeIMO", "Sep"], ExpectedPlatforms = ["windows", "macos"], Frameworks = ["net8.0", "net10.0"] }
        ];
        DeclareCases(declarations, "comparison-a", initialResult);
        string destination = Path.Combine(root, "merged", "index.json");
        BenchmarkEvidenceCatalog merged = service.MergeFiles(destination, [source], expectedPlatforms: null, declarations: declarations);
        Assert.Equal(5, merged.SchemaVersion);
        Assert.Equal(new[] { "csv", "word" }, merged.Areas);
        BenchmarkEvidenceEntry retained = Assert.Single(merged.Entries);
        Assert.Equal("csv", retained.Area);
        Assert.Equal(4, merged.Availability.Count(value => value.ComparisonId == "never-run"));
        Assert.All(merged.Availability.Where(value => value.ComparisonId == "never-run"), value => Assert.False(value.Available));

        string incoming = Path.Combine(root, "incoming", "index.json");
        UpdateFileDeclared(service, incoming, Result("Linux", "fixture-a", 11), "comparison-a",
            "/data/linux.json", "full", true, resultArtifactPath: Path.Combine(root, "incoming", "linux.json"));
        BenchmarkEvidenceCatalog updated = service.MergeFiles(destination, [destination, incoming]);
        Assert.Equal(2, updated.Entries.Length);
        Assert.Contains(updated.Comparisons, value => value.ComparisonId == "never-run");
        Assert.Equal(retained.ResultSha256, Assert.Single(updated.Entries, value => value.Platform == "windows").ResultSha256);

        // A fresh Windows run replaces that lane; the earlier run stays in the dated superseded history.
        string refresh = Path.Combine(root, "refresh", "index.json");
        UpdateFileDeclared(service, refresh, Result("Windows", "fixture-a", 7), "comparison-a",
            "fresh.json", "full", true, resultArtifactPath: Path.Combine(root, "refresh", "fresh.json"));
        BenchmarkEvidenceCatalog refreshed = service.MergeFiles(destination, [destination, refresh], null, null,
            "/data/benchmarks/library-comparisons");
        Assert.Equal(2, refreshed.Entries.Length);
        Assert.All(refreshed.Entries, value => Assert.StartsWith("/data/benchmarks/library-comparisons/", value.ResultPath));
        BenchmarkEvidenceEntry windows = Assert.Single(refreshed.Entries, value => value.Platform == "windows");
        Assert.NotEqual(retained.ResultSha256, windows.ResultSha256);
        BenchmarkSupersededLane superseded = Assert.Single(refreshed.Superseded);
        Assert.Equal(windows.LaneKey, superseded.LaneKey);
        // The superseded run stays in the history as a record; the catalog no longer points to its file.
        Assert.Equal(string.Empty, superseded.ArtifactRelativePath);
        Assert.Equal(retained.ResultSha256, superseded.ResultSha256);
        Assert.Equal(retained.GeneratedUtc, superseded.GeneratedUtc);
        Assert.Equal(windows.GeneratedUtc, superseded.SupersededByUtc);

        BenchmarkEvidenceCatalog replayed = service.MergeFiles(destination, [destination, refresh], null, null,
            "/data/benchmarks/library-comparisons");
        Assert.Equal(windows.ResultSha256, Assert.Single(replayed.Entries, value => value.Platform == "windows").ResultSha256);
        Assert.Single(replayed.Superseded);
    }

    [Fact]
    public void EvidenceCatalog_RejectsConflictingDeclarationsBeforeReplacingDestination()
    {
        string root = CreateTempRoot();
        string first = Path.Combine(root, "first.json");
        string second = Path.Combine(root, "second.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkJson.Write(first, new BenchmarkEvidenceCatalog
        {
            Comparisons = [new BenchmarkComparisonDefinition { ComparisonId = "a", Area = "pdf", Title = "First", Libraries = ["OfficeIMO"] }]
        });
        BenchmarkJson.Write(second, new BenchmarkEvidenceCatalog
        {
            Comparisons = [new BenchmarkComparisonDefinition { ComparisonId = "a", Area = "word", Title = "Second", Libraries = ["OfficeIMO"] }]
        });
        string destination = Path.Combine(root, "destination.json");
        BenchmarkJson.Write(destination, new BenchmarkEvidenceCatalog());
        byte[] original = File.ReadAllBytes(destination);

        Assert.Throws<InvalidOperationException>(() => service.MergeFiles(destination, [first, second]));
        Assert.Equal(original, File.ReadAllBytes(destination));
    }

    [Fact]
    public void EvidenceCatalog_MergesPlatformSetsIndependentOfOrder()
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
        BenchmarkComparisonEligibility eligibility = Assert.Single(merged.Eligibility);
        Assert.False(eligibility.Publishable);
        Assert.Contains("Single-library", eligibility.Reason, StringComparison.Ordinal);
    }
}
