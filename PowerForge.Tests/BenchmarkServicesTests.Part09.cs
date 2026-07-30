using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void EvidenceCatalog_RejectsComparisonValuesNotRecomputedFromSummary()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 20);
        result.Summary =
        [
            SummaryRow(result, "OfficeIMO", 20),
            SummaryRow(result, "Sep", 10)
        ];
        result.Comparison = new BenchmarkSummaryService().Compare(
            result.Summary,
            "Sep",
            "MedianMs",
            tieTolerance: 0.05);
        BenchmarkComparisonRow office = Assert.Single(
            result.Comparison,
            row => row.Engine == "OfficeIMO");
        office.Actual = 40;
        office.Baseline = 20;
        office.Ratio = 2;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("recomputed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsSampleSuiteMismatch()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Samples =
        [
            new BenchmarkSample
            {
                Suite = "Different",
                Scenario = "CsvTyped",
                Operation = "Read",
                Engine = "OfficeIMO",
                Os = "Windows",
                RunMode = "full",
                Status = BenchmarkSampleStatus.Succeeded,
                DurationMs = 10
            }
        ];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("top-level suite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsSummarySuiteMismatch()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Summary[0].Suite = "Different";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("top-level suite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsComparisonSuiteMismatch()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Comparison =
        [
            new BenchmarkComparisonRow
            {
                Suite = "Different",
                Scenario = "CsvTyped",
                Operation = "Read",
                Engine = "OfficeIMO",
                BaselineEngine = "OfficeIMO",
                Os = "Windows",
                RunMode = "full",
                Status = "Succeeded",
                Actual = 10,
                Baseline = 10,
                Ratio = 1
            }
        ];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("top-level suite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MarksDifferentProcessorsAsNotComparable()
    {
        BenchmarkRunResult windows = Result("Windows", "fixture-a", 10);
        BenchmarkRunResult linux = Result("Linux", "fixture-a", 11);
        windows.Environment.ProcessorName = "AMD Ryzen 9";
        windows.Environment.PhysicalProcessorCount = 1;
        windows.Environment.PhysicalCoreCount = 16;
        windows.Environment.LogicalCoreCount = 32;
        linux.Environment.ProcessorName = "Apple M4";
        linux.Environment.PhysicalProcessorCount = 1;
        linux.Environment.PhysicalCoreCount = 10;
        linux.Environment.LogicalCoreCount = 10;
        var service = new BenchmarkEvidenceCatalogService();

        BenchmarkEvidenceCatalog catalog = service.Update(
            null,
            windows,
            "comparison-a",
            "windows.json",
            "full",
            publish: true);
        catalog = service.Update(
            catalog,
            linux,
            "comparison-a",
            "linux.json",
            "full",
            publish: true);

        Assert.All(catalog.Entries, entry => Assert.False(entry.Comparable));
        Assert.All(catalog.Entries, entry =>
            Assert.Contains(
                entry.CompatibilityIssues,
                issue => issue.Contains("processorName", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void EvidenceCatalog_UpdateFileWritesAndHashesValidatedResultArtifact()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        string resultPath = Path.Combine(root, "windows.json");
        File.WriteAllText(resultPath, "{\"suite\":\"stale\"}");
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);

        BenchmarkEvidenceCatalog catalog =
            new BenchmarkEvidenceCatalogService().UpdateFile(
                catalogPath,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true);

        BenchmarkRunResult written = BenchmarkJson.Read<BenchmarkRunResult>(resultPath);
        Assert.Equal(result.RunId, written.RunId);
        Assert.Equal(
            BenchmarkJson.ComputeFileSha256(resultPath),
            Assert.Single(catalog.Entries).ResultSha256);
    }

    [Fact]
    public void BenchmarkJson_WritesPlatformIndependentLfBytes()
    {
        string root = CreateTempRoot();
        string resultPath = Path.Combine(root, "canonical.json");
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);

        BenchmarkJson.Write(resultPath, result);

        byte[] bytes = File.ReadAllBytes(resultPath);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal(
            BenchmarkJson.ComputeSha256(result),
            BenchmarkJson.ComputeFileSha256(resultPath));
    }

    [Fact]
    public void EvidenceCatalog_SeparatesPortableResultPathFromLocalArtifactPath()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "site", "data", "benchmarks", "index.json");
        string artifactPath = Path.Combine(
            root,
            "site",
            "data",
            "benchmarks",
            "windows.json");
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);

        BenchmarkEvidenceCatalog catalog =
            new BenchmarkEvidenceCatalogService().UpdateFile(
                catalogPath,
                result,
                "comparison-a",
                "/data/benchmarks/windows.json",
                "full",
                publish: true,
                resultArtifactPath: artifactPath);

        BenchmarkEvidenceEntry entry = Assert.Single(catalog.Entries);
        Assert.Equal("/data/benchmarks/windows.json", entry.ResultPath);
        Assert.Equal(result.RunId, BenchmarkJson.Read<BenchmarkRunResult>(artifactPath).RunId);
        Assert.Equal(BenchmarkJson.ComputeFileSha256(artifactPath), entry.ResultSha256);
    }

    [Fact]
    public void EvidenceCatalog_DemotesLegacyPublishedLanesDuringSchemaMigration()
    {
        var legacy = new BenchmarkEvidenceCatalog
        {
            SchemaVersion = 1,
            Entries =
            [
                new BenchmarkEvidenceEntry
                {
                    ComparisonId = "comparison-a",
                    Platform = "linux",
                    RunMode = "full",
                    Publish = true,
                    ResultPath = "legacy-linux.json",
                    ResultSha256 = string.Empty
                }
            ]
        };

        BenchmarkEvidenceCatalog updated = new BenchmarkEvidenceCatalogService().Update(
            legacy,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "windows.json",
            "full",
            publish: true);

        Assert.Equal(3, updated.SchemaVersion);
        Assert.False(Assert.Single(updated.Entries, entry => entry.Platform == "linux").Publish);
        Assert.True(Assert.Single(updated.Entries, entry => entry.Platform == "windows").Publish);
        Assert.Contains(
            updated.Availability,
            lane => lane.Platform == "linux" && !lane.Available);
    }

    [Fact]
    public void EvidenceCatalog_RestoresPreviousResultWhenCatalogCommitFails()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        string resultPath = Path.Combine(root, "windows.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult original = Result("Windows", "fixture-a", 10);
        service.UpdateFile(
            catalogPath,
            original,
            "comparison-a",
            "windows.json",
            "full",
            publish: true);
        byte[] expectedArtifact = File.ReadAllBytes(resultPath);
        File.SetAttributes(catalogPath, File.GetAttributes(catalogPath) | FileAttributes.ReadOnly);
        try
        {
            BenchmarkRunResult replacement = Result("Windows", "fixture-a", 20);
            replacement.FinishedUtc = original.FinishedUtc.AddMinutes(1);

            Assert.ThrowsAny<Exception>(() => service.UpdateFile(
                catalogPath,
                replacement,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

            Assert.Equal(expectedArtifact, File.ReadAllBytes(resultPath));
        }
        finally
        {
            File.SetAttributes(catalogPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void EvidenceCatalog_RejectsImportedEvidenceWithoutProductionSidecar()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["importedUtc"] = DateTimeOffset.UtcNow.ToString("O");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("production provenance sidecar", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsForgedProductionSidecarMarker()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata["importedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        result.Metadata["benchmark.provenance.source"] = "sidecar";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("production provenance sidecar", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsPublishedLanesSharingOneResultPath()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkEvidenceCatalog catalog = service.Update(
            null,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "shared.json",
            "full",
            publish: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.Update(
                catalog,
                Result("Linux", "fixture-a", 11),
                "comparison-a",
                "shared.json",
                "full",
                publish: true));

        Assert.Contains("distinct result paths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RequiresPublishedHardwareIdentity()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Environment.ProcessorName = string.Empty;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "windows.json",
                "full",
                publish: true));

        Assert.Contains("processorName", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkProvenanceCapture_BindsFreshArtifactsToUnchangedSource()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string artifactRoot = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(sourceRoot);
        RunGit(sourceRoot, "init");
        RunGit(sourceRoot, "config user.email benchmark@example.test");
        RunGit(sourceRoot, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(sourceRoot, "source.txt"), "measured");
        RunGit(sourceRoot, "add source.txt");
        RunGit(sourceRoot, "commit -m measured");

        var service = new BenchmarkArtifactProvenanceService();
        BenchmarkProvenanceCaptureSession capture =
            service.Start(sourceRoot, artifactRoot);
        string reportPath = Path.Combine(artifactRoot, "Case-report-full.json");
        File.WriteAllText(reportPath, BenchmarkDotNetReport("Windows"));
        string sidecarPath = service.Complete(capture);

        BenchmarkRunResult imported =
            new BenchmarkResultImporter().Import(artifactRoot);

        Assert.True(File.Exists(sidecarPath));
        Assert.Equal(capture.SourceCommit, imported.Metadata["gitSha"]);
        Assert.Equal("true", imported.Metadata["gitWorktreeClean"]);
        Assert.Equal("sidecar", imported.Metadata["benchmark.provenance.source"]);
        Assert.Equal(capture.StartedUtc, imported.StartedUtc);
    }

    [Fact]
    public void BenchmarkProvenanceCapture_AllowsIgnoredInRepositoryArtifactRoot()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string artifactRoot = Path.Combine(sourceRoot, "Build", "BenchmarkArtifacts");
        Directory.CreateDirectory(sourceRoot);
        RunGit(sourceRoot, "init");
        RunGit(sourceRoot, "config user.email benchmark@example.test");
        RunGit(sourceRoot, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(sourceRoot, ".gitignore"), "Build/\n");
        File.WriteAllText(Path.Combine(sourceRoot, "source.txt"), "measured");
        RunGit(sourceRoot, "add .gitignore source.txt");
        RunGit(sourceRoot, "commit -m measured");

        var service = new BenchmarkArtifactProvenanceService();
        BenchmarkProvenanceCaptureSession capture = service.Start(sourceRoot, artifactRoot);
        File.WriteAllText(
            Path.Combine(artifactRoot, "nested-report.json"),
            BenchmarkDotNetReport("Windows"));

        string sidecarPath = service.Complete(capture);

        Assert.True(File.Exists(sidecarPath));
    }

    [Fact]
    public void BenchmarkProvenanceCapture_RejectsUnignoredInRepositoryArtifactRoot()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string artifactRoot = Path.Combine(sourceRoot, "Build", "BenchmarkArtifacts");
        Directory.CreateDirectory(sourceRoot);
        RunGit(sourceRoot, "init");
        RunGit(sourceRoot, "config user.email benchmark@example.test");
        RunGit(sourceRoot, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(sourceRoot, "source.txt"), "measured");
        RunGit(sourceRoot, "add source.txt");
        RunGit(sourceRoot, "commit -m measured");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkArtifactProvenanceService().Start(sourceRoot, artifactRoot));

        Assert.Contains("ignored by Git", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BenchmarkProvenanceCapture_ExclusivelyReservesArtifactRoot()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string artifactRoot = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(sourceRoot);
        RunGit(sourceRoot, "init");
        RunGit(sourceRoot, "config user.email benchmark@example.test");
        RunGit(sourceRoot, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(sourceRoot, "source.txt"), "measured");
        RunGit(sourceRoot, "add source.txt");
        RunGit(sourceRoot, "commit -m measured");
        var service = new BenchmarkArtifactProvenanceService();
        using BenchmarkProvenanceCaptureSession first = service.Start(sourceRoot, artifactRoot);

        Task<Exception?> competing = Task.Run(() =>
        {
            try
            {
                using BenchmarkProvenanceCaptureSession ignored =
                    service.Start(sourceRoot, artifactRoot);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        });
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.False(competing.IsCompleted);
        first.Dispose();

        Exception? completion = await competing;
        Assert.Null(completion);
    }

    [Fact]
    public void BenchmarkImporter_ValidatesNestedReportAgainstAncestorSidecar()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string artifactRoot = Path.Combine(root, "artifacts");
        string nestedRoot = Path.Combine(artifactRoot, "results", "net10.0");
        Directory.CreateDirectory(sourceRoot);
        RunGit(sourceRoot, "init");
        RunGit(sourceRoot, "config user.email benchmark@example.test");
        RunGit(sourceRoot, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(sourceRoot, "source.txt"), "measured");
        RunGit(sourceRoot, "add source.txt");
        RunGit(sourceRoot, "commit -m measured");
        var service = new BenchmarkArtifactProvenanceService();
        BenchmarkProvenanceCaptureSession capture = service.Start(sourceRoot, artifactRoot);
        Directory.CreateDirectory(nestedRoot);
        string reportPath = Path.Combine(nestedRoot, "Case-report-full.json");
        File.WriteAllText(reportPath, BenchmarkDotNetReport("Windows"));
        service.Complete(capture);

        BenchmarkRunResult imported = new BenchmarkResultImporter().Import(reportPath);

        Assert.Equal(capture.SourceCommit, imported.Metadata["gitSha"]);
        Assert.Equal("sidecar", imported.Metadata["benchmark.provenance.source"]);
    }

    [Fact]
    public void BenchmarkImporter_MapsBenchmarkDotNetCsvStatisticsToMilliseconds()
    {
        string root = CreateTempRoot();
        string reportPath = Path.Combine(root, "Case-report.csv");
        File.WriteAllText(
            reportPath,
            "Method,Job,N,Mean [ms],Median [ms],P95 [ms],P99 [ms],StdDev [ms]\n" +
            "OfficeIMO,full,12,10.5,10.0,12.5,13.5,0.75\n");

        BenchmarkSummaryRow row = Assert.Single(
            new BenchmarkResultImporter().Import(reportPath).Summary);

        Assert.Equal(1, row.SampleCount);
        Assert.Equal("12", row.Variables["N"]);
        Assert.Equal(12.5, row.P95Ms);
        Assert.Equal(13.5, row.P99Ms);
        Assert.Equal(0.75, row.StdDevMs);
        Assert.Equal(12.5, row.Metrics["P95Ms"]);
        Assert.Equal(13.5, row.Metrics["P99Ms"]);
        Assert.Equal(0.75, row.Metrics["StdDevMs"]);
    }

    [Fact]
    public void BenchmarkProvenanceCapture_RejectsArtifactMutationAfterCompletion()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string artifactRoot = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(sourceRoot);
        RunGit(sourceRoot, "init");
        RunGit(sourceRoot, "config user.email benchmark@example.test");
        RunGit(sourceRoot, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(sourceRoot, "source.txt"), "measured");
        RunGit(sourceRoot, "add source.txt");
        RunGit(sourceRoot, "commit -m measured");

        var service = new BenchmarkArtifactProvenanceService();
        BenchmarkProvenanceCaptureSession capture =
            service.Start(sourceRoot, artifactRoot);
        string reportPath = Path.Combine(artifactRoot, "Case-report-full.json");
        File.WriteAllText(reportPath, BenchmarkDotNetReport("Windows"));
        service.Complete(capture);
        File.AppendAllText(reportPath, " ");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkResultImporter().Import(artifactRoot));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkProvenanceSnapshotRemainsBoundAfterOriginalArtifactsChange()
    {
        string root = CreateTempRoot();
        string sourceRoot = Path.Combine(root, "source");
        string artifactRoot = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(sourceRoot);
        RunGit(sourceRoot, "init");
        RunGit(sourceRoot, "config user.email benchmark@example.test");
        RunGit(sourceRoot, "config user.name Benchmark");
        File.WriteAllText(Path.Combine(sourceRoot, "source.txt"), "measured");
        RunGit(sourceRoot, "add source.txt");
        RunGit(sourceRoot, "commit -m measured");
        var service = new BenchmarkArtifactProvenanceService();
        BenchmarkProvenanceCaptureSession capture = service.Start(sourceRoot, artifactRoot);
        string reportPath = Path.Combine(artifactRoot, "Case-report-full.json");
        File.WriteAllText(reportPath, BenchmarkDotNetReport("Windows"));
        service.Complete(capture);
        Assert.True(BenchmarkArtifactProvenanceService.TryLoadAndValidate(
            artifactRoot,
            out BenchmarkArtifactProvenanceDocument? provenance,
            out string validatedRoot,
            out string sidecarPath));

        using BenchmarkArtifactSnapshot snapshot =
            BenchmarkArtifactProvenanceService.CreateValidatedSnapshot(
                artifactRoot,
                provenance!,
                validatedRoot,
                sidecarPath);
        File.WriteAllText(reportPath, BenchmarkDotNetReport("Linux"));
        BenchmarkRunResult imported = new BenchmarkResultImporter().Import(snapshot.InputPath);

        Assert.Equal("Windows", imported.Environment.OsFamily);
        Assert.Equal(capture.SourceCommit, imported.Metadata["gitSha"]);
    }

    [Fact]
    public void EvidenceCatalog_RefusesUnixSymbolicLinkLockFile()
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;

        string root = CreateTempRoot();
        string destination = Path.Combine(root, "index.json");
        string victim = Path.Combine(root, "victim.txt");
        File.WriteAllText(victim, "unchanged");
        string lockPath = BenchmarkFileUpdateLock.CreateLockPath(destination);
        File.CreateSymbolicLink(lockPath, victim);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkFileUpdateLock.Acquire(destination));

        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("unchanged", File.ReadAllText(victim));
#endif
    }

    private static BenchmarkSummaryRow SummaryRow(
        BenchmarkRunResult result,
        string engine,
        double median)
        => new()
        {
            Suite = result.Suite,
            Scenario = "CsvTyped",
            Operation = "Read",
            Engine = engine,
            Os = result.Environment.OsFamily,
            RunMode = "full",
            Status = "Succeeded",
            SampleCount = 1,
            MedianMs = median
        };

    private static void RunGit(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Unable to start Git for test setup.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"git {arguments} failed: {output} {error}");
    }
}
