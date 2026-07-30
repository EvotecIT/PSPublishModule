using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void EnvironmentMetadata_CapturesRepositoryWideStatusFromNestedSourceRoot()
    {
        string root = CreateTempRoot();
        string benchmarkRoot = Path.Combine(root, "benchmarks", "suite");
        string outputRoot = Path.Combine(benchmarkRoot, "out");
        Directory.CreateDirectory(benchmarkRoot);
        RunGit(root, "init");
        RunGit(root, "config user.email benchmark@example.test");
        RunGit(root, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(root, "source.txt"), "measured");
        File.WriteAllText(Path.Combine(benchmarkRoot, "suite.ps1"), "benchmark source");
        RunGit(root, "add .");
        RunGit(root, "commit -m measured");
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, "report.json"), "{}");
        File.WriteAllText(Path.Combine(root, "source.txt"), "dirty");
        var suite = new PowerShellBenchmarkSuite
        {
            Name = "nested-provenance",
            SourceRoot = benchmarkRoot,
            OutputRoot = "out"
        };

        PowerShellBenchmarkEnvironmentMetadata.SourceProvenance provenance =
            PowerShellBenchmarkEnvironmentMetadata.CaptureSourceProvenance(suite);

        Assert.Contains("source.txt", provenance.GitStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "benchmarks/suite/out",
            provenance.GitStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_UpdateFileWritesDiagnosticLaneArtifact()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        string resultPath = Path.Combine(root, "quick.json");
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);

        BenchmarkEvidenceCatalog catalog =
            new BenchmarkEvidenceCatalogService().UpdateFile(
                catalogPath,
                result,
                "comparison-a",
                "quick.json",
                "quick",
                publish: false);

        Assert.True(File.Exists(resultPath));
        Assert.Equal(
            BenchmarkJson.ComputeFileSha256(resultPath),
            Assert.Single(catalog.Entries).ResultSha256);
    }

    [Fact]
    public void EvidenceCatalog_RejectsNormalizedArtifactDestinationCollision()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "results/windows.json",
            "full",
            publish: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.UpdateFile(
                catalogPath,
                Result("Linux", "fixture-a", 11),
                "comparison-a",
                "results/../results/windows.json",
                "full",
                publish: true));

        Assert.Contains("artifact destination", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsSharedExplicitArtifactDestination()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        string sharedArtifactPath = Path.Combine(root, "site", "result.json");
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/windows.json",
            "full",
            publish: true,
            resultArtifactPath: sharedArtifactPath);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.UpdateFile(
                catalogPath,
                Result("Linux", "fixture-a", 11),
                "comparison-a",
                "/data/linux.json",
                "full",
                publish: true,
                resultArtifactPath: sharedArtifactPath));

        Assert.Contains("artifact destination", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvidenceCatalog_PreservesExplicitArtifactOwnershipWhenPriorFileIsUnavailable(
        bool tamperPriorArtifact)
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        string sharedArtifactPath = Path.Combine(root, "site", "result.json");
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "/data/windows.json",
            "full",
            publish: true,
            resultArtifactPath: sharedArtifactPath);
        if (tamperPriorArtifact)
            File.WriteAllText(sharedArtifactPath, "tampered");
        else
            File.Delete(sharedArtifactPath);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.UpdateFile(
                catalogPath,
                Result("Linux", "fixture-a", 11),
                "comparison-a",
                "/data/linux.json",
                "full",
                publish: true,
                resultArtifactPath: sharedArtifactPath));

        Assert.Contains("artifact destination", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_AllowsIdenticalPayloadsAtDistinctExplicitDestinations()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        service.UpdateFile(
            catalogPath,
            result,
            "comparison-a",
            "/data/windows-a.json",
            "quick",
            publish: false,
            resultArtifactPath: Path.Combine(root, "site", "result-a.json"));

        BenchmarkEvidenceCatalog catalog = service.UpdateFile(
            catalogPath,
            result,
            "comparison-b",
            "/data/windows-b.json",
            "quick",
            publish: false,
            resultArtifactPath: Path.Combine(root, "site", "result-b.json"));

        Assert.Equal(2, catalog.Entries.Length);
    }

    [Fact]
    public void EvidenceCatalog_PreservesCaseSensitiveArtifactDestinations()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            catalogPath,
            Result("Linux", "fixture-a", 10),
            "comparison-a",
            "/data/result.json",
            "quick",
            publish: false,
            resultArtifactPath: Path.Combine(root, "result.json"));

        BenchmarkEvidenceCatalog catalog = service.UpdateFile(
            catalogPath,
            Result("Linux", "fixture-a", 11),
            "comparison-b",
            "/data/RESULT.json",
            "quick",
            publish: false,
            resultArtifactPath: Path.Combine(root, "RESULT.json"));

        Assert.Equal(2, catalog.Entries.Length);
    }

    [Fact]
    public void EvidenceCatalog_RejectsDiagnosticArtifactDestinationCollision()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        service.UpdateFile(
            catalogPath,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "results/quick.json",
            "quick",
            publish: false);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.UpdateFile(
                catalogPath,
                Result("Linux", "fixture-a", 11),
                "comparison-a",
                "results/../results/quick.json",
                "quick",
                publish: false));

        Assert.Contains("artifact destination", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsMeasurementsMutatedAfterProvenanceValidation()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["importedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        BenchmarkResultImporter.CaptureValidatedProductionState(result);
        result.Summary[0].MedianMs = 20;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("changed after", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_AllowsMetadataEnrichmentAfterProvenanceValidation()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["importedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        BenchmarkResultImporter.CaptureValidatedProductionState(result);
        result.Metadata["benchmark.note"] = "enriched after import";

        BenchmarkEvidenceCatalog catalog =
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true);

        Assert.Single(catalog.Entries);
    }

    [Fact]
    public void EvidenceCatalog_AllowsNewCompatibilityMetadataAfterProvenanceValidation()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["importedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        BenchmarkResultImporter.CaptureValidatedProductionState(result);
        result.Metadata["benchmark.fixture.datasetSha256"] = new string('a', 64);
        result.Metadata["benchmark.package.AdditionalEngine"] = "1.2.3";
        result.Metadata["benchmark.workload.projectedColumns"] = "8";
        Assert.DoesNotContain(result.ValidatedProductionMetadata, item =>
            !result.Metadata.TryGetValue(item.Key, out string? value) ||
            !string.Equals(item.Value, value, StringComparison.Ordinal));
        Assert.Equal(
            result.ValidatedProductionContentSha256,
            BenchmarkResultImporter.ComputeValidatedProductionContentSha256(result));

        BenchmarkEvidenceCatalog catalog =
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true);

        BenchmarkEvidenceEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal(
            new string('a', 64),
            entry.Compatibility["benchmark.fixture.datasetSha256"]);
    }

    [Theory]
    [InlineData("gitSha")]
    [InlineData("gitWorktreeClean")]
    [InlineData("benchmark.provenance.sidecar.sha256")]
    public void EvidenceCatalog_RejectsBoundMetadataMutatedAfterProvenanceValidation(
        string metadataKey)
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["importedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        result.Metadata["gitSha"] = new string('a', 40);
        result.Metadata["gitWorktreeClean"] = "true";
        result.Metadata["benchmark.provenance.sidecar.sha256"] = new string('b', 64);
        BenchmarkResultImporter.CaptureValidatedProductionState(result);
        result.Metadata[metadataKey] = metadataKey == "gitSha"
            ? new string('c', 40)
            : metadataKey == "gitWorktreeClean"
                ? "false"
                : new string('d', 64);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("changed after", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsTimestampMutatedAfterProvenanceValidation()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["importedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        BenchmarkResultImporter.CaptureValidatedProductionState(result);
        result.FinishedUtc = result.FinishedUtc.AddMinutes(1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("changed after", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsInconsistentSummaryStatisticsWithoutSamples()
    {
        BenchmarkRunResult invalidRange = Result("Windows", "fixture-a", 10);
        invalidRange.Samples = Array.Empty<BenchmarkSample>();
        invalidRange.Summary[0].MinMs = 9;
        invalidRange.Summary[0].MedianMs = 10;
        invalidRange.Summary[0].MaxMs = 8;

        InvalidOperationException rangeException = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                invalidRange,
                "comparison-a",
                "range.json",
                "full",
                publish: true));
        Assert.Contains("MinMs <= MaxMs", rangeException.Message, StringComparison.Ordinal);

        BenchmarkRunResult invalidPercentile = Result("Windows", "fixture-a", 10);
        invalidPercentile.Samples = Array.Empty<BenchmarkSample>();
        invalidPercentile.Summary[0].P95Ms = -1;

        InvalidOperationException percentileException = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                invalidPercentile,
                "comparison-a",
                "percentile.json",
                "full",
                publish: true));
        Assert.Contains("P95Ms", percentileException.Message, StringComparison.Ordinal);

        BenchmarkRunResult invalidDeviation = Result("Windows", "fixture-a", 10);
        invalidDeviation.Samples = Array.Empty<BenchmarkSample>();
        invalidDeviation.Summary[0].StdDevMs = double.NaN;

        InvalidOperationException deviationException = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                invalidDeviation,
                "comparison-a",
                "deviation.json",
                "full",
                publish: true));
        Assert.Contains("StdDevMs", deviationException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceCatalog_RejectsArchitectureOnlyProcessorIdentity()
    {
        BenchmarkRunResult result = Result("Linux", "fixture-a", 10);
        result.Environment.ProcessorName = "X64 processor";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "linux.json",
                "full",
                publish: true));

        Assert.Contains("specific processorName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceCatalog_RejectsFutureCompletionTimestamp()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.FinishedUtc = DateTimeOffset.UtcNow.AddHours(1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("future", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
