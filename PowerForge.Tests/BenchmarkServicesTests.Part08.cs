using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void BenchmarkDotNetImporter_PreservesTypedEnvironmentAndOperatingSystemLane()
    {
        var root = CreateTempRoot();
        var report = Path.Combine(root, "Tabular-report-full.json");
        File.WriteAllText(report, """
        {
          "Title": "OfficeIMO.LibraryComparison",
          "HostEnvironmentInfo": {
            "BenchmarkDotNetCaption": "BenchmarkDotNet v0.15.8",
            "OsVersion": "Microsoft Windows 11 (10.0.26200.8875)",
            "ProcessorName": "AMD Ryzen 9 9950X3D2",
            "PhysicalProcessorCount": 1,
            "PhysicalCoreCount": 16,
            "LogicalCoreCount": 32,
            "RuntimeVersion": ".NET 10.0.10",
            "Architecture": "X64",
            "OsArchitecture": "Arm64",
            "ProcessArchitecture": "X64",
            "DotNetCliVersion": "10.0.302"
          },
          "Benchmarks": [
            {
              "Method": "CsvTyped",
              "Job": "net10",
              "Statistics": { "Mean": 36780000, "Median": 36000000 },
              "Memory": { "BytesAllocatedPerOperation": 23640000 }
            }
          ]
        }
        """);

        var result = new BenchmarkResultImporter().Import(report);

        Assert.Equal("Windows", result.Environment.OsFamily);
        Assert.Equal("AMD Ryzen 9 9950X3D2", result.Environment.ProcessorName);
        Assert.Equal(16, result.Environment.PhysicalCoreCount);
        Assert.Equal(32, result.Environment.LogicalCoreCount);
        Assert.Equal("Arm64", result.Environment.OsArchitecture);
        Assert.Equal("X64", result.Environment.ProcessArchitecture);
        Assert.Equal("10.0.302", result.Environment.DotNetSdkVersion);
        Assert.Equal("Windows", Assert.Single(result.Samples).Os);
        Assert.Equal("Windows", Assert.Single(result.Summary).Os);
    }

    [Fact]
    public void BenchmarkDotNetDirectoryImporter_PreservesEnvironmentAcrossReportFiles()
    {
        var root = CreateTempRoot();
        string report = """
        {
          "Title": "OfficeIMO.LibraryComparison",
          "HostEnvironmentInfo": {
            "OsVersion": "Ubuntu 24.04.3 LTS",
            "ProcessorName": "AMD Ryzen 9 9950X3D2",
            "RuntimeVersion": ".NET 10.0.10",
            "Architecture": "X64"
          },
          "Benchmarks": [
            {
              "Method": "OfficeIMO",
              "Job": "full",
              "Statistics": { "Mean": 1000000 }
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(root, "Csv-report-full.json"), report);
        File.WriteAllText(Path.Combine(root, "Xlsx-report-full.json"), report);

        var result = new BenchmarkResultImporter().Import(root);

        Assert.Equal("Linux", result.Environment.OsFamily);
        Assert.Equal(2, result.Samples.Length);
        Assert.All(result.Samples, sample => Assert.Equal("Linux", sample.Os));
    }

    [Fact]
    public void BenchmarkDotNetDirectoryImporter_RejectsReportsFromDifferentOperatingSystems()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "Windows-report-full.json"), BenchmarkDotNetReport("Microsoft Windows 11"));
        File.WriteAllText(Path.Combine(root, "Linux-report-full.json"), BenchmarkDotNetReport("Ubuntu 24.04.3 LTS"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new BenchmarkResultImporter().Import(root));

        Assert.Contains("one operating system", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Linux", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkDotNetDirectoryImporter_RejectsReportsFromDifferentMachines()
    {
        var root = CreateTempRoot();
        File.WriteAllText(
            Path.Combine(root, "First-report-full.json"),
            BenchmarkDotNetReport("Ubuntu 24.04.3 LTS", processorName: "AMD Ryzen"));
        File.WriteAllText(
            Path.Combine(root, "Second-report-full.json"),
            BenchmarkDotNetReport("Ubuntu 24.04.3 LTS", processorName: "Apple M4"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new BenchmarkResultImporter().Import(root));

        Assert.Contains("one benchmark environment", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(BenchmarkEnvironmentInfo.ProcessorName), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvDirectoryImporter_RejectsRowsFromDifferentOperatingSystems()
    {
        var root = CreateTempRoot();
        const string header = "Suite,Scenario,Operation,Engine,Host,OS,RunMode,Iteration,Status,DurationMs,Reason\n";
        File.WriteAllText(
            Path.Combine(root, "Windows-report.csv"),
            header + "suite,case,Read,OfficeIMO,Current,Windows,full,0,Succeeded,10,\n");
        File.WriteAllText(
            Path.Combine(root, "Linux-report.csv"),
            header + "suite,case,Read,OfficeIMO,Current,Linux,full,0,Succeeded,11,\n");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new BenchmarkResultImporter().Import(root));

        Assert.Contains("one operating system", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Linux", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_KeepsPlatformsSeparateAndShowsMissingLanes()
    {
        var service = new BenchmarkEvidenceCatalogService();
        var windows = Result("Windows", "fixture-a", 10);
        var linux = Result("Linux", "fixture-a", 12);

        var catalog = service.Update(null, windows, "tabular-65k-v1", "windows.json", "full", true);
        catalog = service.Update(catalog, linux, "tabular-65k-v1", "linux.json", "full", true);

        Assert.Equal(2, catalog.Entries.Length);
        Assert.Contains(catalog.Entries, entry => entry.Platform == "windows" && entry.Comparable);
        Assert.Contains(catalog.Entries, entry => entry.Platform == "linux" && entry.Comparable);
        Assert.Contains(catalog.Availability, lane => lane.Platform == "windows" && lane.Available);
        Assert.Contains(catalog.Availability, lane => lane.Platform == "linux" && lane.Available);
        Assert.Contains(catalog.Availability, lane => lane.Platform == "macos" && !lane.Available);
    }

    [Fact]
    public void EvidenceCatalog_ScopesAvailabilityToComparisonAndRunMode()
    {
        var service = new BenchmarkEvidenceCatalogService();
        var catalog = service.Update(
            null,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "a-windows.json",
            "full",
            true);
        catalog = service.Update(
            catalog,
            Result("Linux", "fixture-a", 11),
            "comparison-b",
            "b-linux.json",
            "full",
            true);
        catalog = service.Update(
            catalog,
            Result("macOS", "fixture-a", 12),
            "comparison-b",
            "b-macos.json",
            "quick",
            false);

        BenchmarkPlatformAvailability comparisonAWindows = Assert.Single(
            catalog.Availability,
            lane => lane.ComparisonId == "comparison-a" &&
                    lane.RunMode == "full" &&
                    lane.Platform == "windows");
        BenchmarkPlatformAvailability comparisonALinux = Assert.Single(
            catalog.Availability,
            lane => lane.ComparisonId == "comparison-a" &&
                    lane.RunMode == "full" &&
                    lane.Platform == "linux");
        BenchmarkPlatformAvailability comparisonBMacQuick = Assert.Single(
            catalog.Availability,
            lane => lane.ComparisonId == "comparison-b" &&
                    lane.RunMode == "quick" &&
                    lane.Platform == "macos");

        Assert.True(comparisonAWindows.Available);
        Assert.False(comparisonALinux.Available);
        Assert.True(comparisonBMacQuick.Available);
    }

    [Fact]
    public void EvidenceCatalog_FullAvailabilityRequiresPublishedLane()
    {
        var catalog = new BenchmarkEvidenceCatalogService().Update(
            null,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "diagnostic.json",
            "full",
            publish: false);

        BenchmarkPlatformAvailability windows = Assert.Single(
            catalog.Availability,
            lane => lane.ComparisonId == "comparison-a" &&
                    lane.RunMode == "full" &&
                    lane.Platform == "windows");
        Assert.False(windows.Available);
    }

    [Fact]
    public void EvidenceCatalog_RejectsPublishWithoutSuccessfulMeasurements()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Summary[0].Status = "Failed";
        result.Summary[0].MedianMs = null;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "failed.json",
                "full",
                publish: true));

        Assert.Contains("successful measurement", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsPublishWithNonzeroFailureCount()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Summary[0].FailureCount = 1;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "failed.json",
                "full",
                publish: true));

        Assert.Contains("no failed measurements", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsPublishWithoutSourceProvenance()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Metadata.Remove("gitSha");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "missing-source.json",
                "full",
                publish: true));

        Assert.Contains("gitSha", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_RejectsPublishingQuickRuns()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                Result("Windows", "fixture-a", 10),
                "comparison-a",
                "quick.json",
                "quick",
                publish: true));

        Assert.Contains("Full", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_PreservesConfiguredPlatformsWhenUpdateOmitsThem()
    {
        var service = new BenchmarkEvidenceCatalogService();
        var catalog = service.Update(
            null,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "windows.json",
            "full",
            true,
            new[] { "windows", "linux" });

        catalog = service.Update(
            catalog,
            Result("Linux", "fixture-a", 11),
            "comparison-a",
            "linux.json",
            "full",
            true);

        Assert.Equal(new[] { "windows", "linux" }, catalog.ExpectedPlatforms);
        Assert.Null(new PSPublishModule.UpdateBenchmarkEvidenceCatalogCommand().ExpectedPlatform);
    }

    [Fact]
    public void EvidenceCatalog_NormalizesLinuxDistributionLabels()
    {
        BenchmarkRunResult result = Result("Ubuntu 24.04", "fixture-a", 10);
        result.Environment = new BenchmarkEnvironmentInfo();

        BenchmarkEvidenceCatalog catalog = new BenchmarkEvidenceCatalogService().Update(
            null,
            result,
            "comparison-a",
            "ubuntu.json",
            "full",
            true);

        Assert.Equal("linux", Assert.Single(catalog.Entries).Platform);
        Assert.True(Assert.Single(
            catalog.Availability,
            lane => lane.ComparisonId == "comparison-a" &&
                    lane.RunMode == "full" &&
                    lane.Platform == "linux").Available);
    }

    [Fact]
    public void EvidenceCatalog_RejectsConflictingOperatingSystemLabels()
    {
        BenchmarkRunResult result = Result("Windows", "fixture-a", 10);
        result.Summary[0].Os = "Linux";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(
                null,
                result,
                "comparison-a",
                "mixed.json",
                "full",
                true));

        Assert.Contains("one operating-system platform", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("windows", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("linux", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_AcceptsExplicitPlatformForMetadataFreeCsvImports()
    {
        BenchmarkRunResult result = Result(string.Empty, "fixture-a", 10);
        result.Summary[0].Os = string.Empty;

        BenchmarkEvidenceCatalog catalog = new BenchmarkEvidenceCatalogService().Update(
            null,
            result,
            "comparison-a",
            "linux.json",
            "full",
            true,
            platform: "Ubuntu 24.04");

        Assert.Equal("linux", Assert.Single(catalog.Entries).Platform);
        Assert.Null(new PSPublishModule.UpdateBenchmarkEvidenceCatalogCommand().Platform);
    }

    [Fact]
    public void EvidenceCatalog_LockIdentityCanNormalizeCaseInsensitiveVolumes()
    {
        string root = CreateTempRoot();
        string upper = Path.Combine(root, "Index.json");
        string lower = Path.Combine(root, "index.json");

        Assert.Equal(
            BenchmarkFileUpdateLock.CreatePathHash(upper, caseInsensitive: true),
            BenchmarkFileUpdateLock.CreatePathHash(lower, caseInsensitive: true));
    }

    [Fact]
    public void EvidenceCatalog_LockFileSharesTheDestinationDirectory()
    {
        string root = CreateTempRoot();
        string destination = Path.Combine(root, "nested", "index.json");

        string lockPath = BenchmarkFileUpdateLock.CreateLockPath(destination);

        Assert.Equal(
            Path.GetDirectoryName(Path.GetFullPath(destination)),
            Path.GetDirectoryName(lockPath));
        Assert.EndsWith(".lock", lockPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceCatalog_MarksMismatchedFixturesAsNotComparable()
    {
        var service = new BenchmarkEvidenceCatalogService();
        var catalog = service.Update(null, Result("Windows", "fixture-a", 10), "tabular-65k-v1", "windows.json", "full", true);
        catalog = service.Update(catalog, Result("macOS", "fixture-b", 9), "tabular-65k-v1", "macos.json", "full", true);

        var mac = Assert.Single(catalog.Entries, entry => entry.Platform == "macos");
        Assert.False(mac.Comparable);
        Assert.Contains(mac.CompatibilityIssues, issue => issue.Contains("benchmark.fixture.sha256", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvidenceCatalog_MarksMismatchedMeasuredWorkloadShapesAsNotComparable()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult windows = Result("Windows", "fixture-a", 10);
        BenchmarkRunResult linux = Result("Linux", "fixture-a", 11);
        linux.Summary[0].Scenario = "DifferentScenario";

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
        Assert.All(
            catalog.Entries,
            entry => Assert.Contains(
                entry.CompatibilityIssues,
                issue => issue.Contains("benchmark.workload.shape.sha256", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void EvidenceCatalog_ComparesEachUnpublishedLaneAgainstPublishedEvidence()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkEvidenceCatalog catalog = service.Update(
            null,
            Result("Linux", "fixture-a", 10),
            "comparison-a",
            "linux.json",
            "full",
            publish: true);
        catalog = service.Update(
            catalog,
            Result("Windows", "fixture-b", 11),
            "comparison-a",
            "windows-diagnostic.json",
            "full",
            publish: false);

        BenchmarkEvidenceEntry published = Assert.Single(catalog.Entries, entry => entry.Publish);
        BenchmarkEvidenceEntry diagnostic = Assert.Single(catalog.Entries, entry => !entry.Publish);
        Assert.True(published.Comparable);
        Assert.False(diagnostic.Comparable);
        Assert.Contains(
            diagnostic.CompatibilityIssues,
            issue => issue.Contains("benchmark.fixture.sha256", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvidenceCatalog_TreatsCaseOnlyProvenanceDifferencesAsIncompatible()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult windows = Result("Windows", "fixture-a", 10);
        BenchmarkRunResult linux = Result("Linux", "fixture-a", 11);
        windows.Metadata["benchmark.fixture.path"] = "Cases/Foo.csv";
        linux.Metadata["benchmark.fixture.path"] = "Cases/foo.csv";

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
        Assert.All(
            catalog.Entries,
            entry => Assert.Contains(
                entry.CompatibilityIssues,
                issue => issue.Contains("benchmark.fixture.path", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void EvidenceCatalog_MarksMismatchedRuntimeIdentityAsNotComparable()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult windows = Result("Windows", "fixture-a", 10);
        BenchmarkRunResult linux = Result("Linux", "fixture-a", 11);
        linux.Environment.RuntimeVersion = ".NET 8.0.20";

        BenchmarkEvidenceCatalog catalog = service.Update(
            null,
            windows,
            "comparison-a",
            "windows.json",
            "full",
            true);
        catalog = service.Update(
            catalog,
            linux,
            "comparison-a",
            "linux.json",
            "full",
            true);

        Assert.All(catalog.Entries, entry => Assert.False(entry.Comparable));
        Assert.All(
            catalog.Entries,
            entry => Assert.Contains(
                entry.CompatibilityIssues,
                issue => issue.Contains("environment.runtimeVersion", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void EvidenceCatalog_PreservesCaseInsensitiveCompatibilityAfterJsonReload()
    {
        string root = CreateTempRoot();
        string path = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkEvidenceCatalog catalog = service.Update(
            null,
            Result("Windows", "fixture-a", 10),
            "comparison-a",
            "windows.json",
            "full",
            publish: true);
        BenchmarkEvidenceEntry windows = Assert.Single(catalog.Entries);
        windows.Compatibility = windows.Compatibility
            .ToDictionary(
                item => item.Key.Equals("gitSha", StringComparison.OrdinalIgnoreCase) ? "GitSha" : item.Key,
                item => item.Value,
                StringComparer.Ordinal);
        BenchmarkJson.Write(path, catalog);

        catalog = service.UpdateFile(
            path,
            Result("Linux", "fixture-a", 11),
            "comparison-a",
            "linux.json",
            "full",
            publish: true);

        Assert.All(catalog.Entries, entry => Assert.True(entry.Comparable));
        Assert.All(catalog.Entries, entry => Assert.Empty(entry.CompatibilityIssues));
    }

    [Fact]
    public void BenchmarkJson_PreservesUnixPermissionsDuringAtomicReplacement()
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;

        string root = CreateTempRoot();
        string path = Path.Combine(root, "result.json");
        File.WriteAllText(path, "{}");
        const UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, expectedMode);

        BenchmarkJson.Write(path, new Dictionary<string, string> { ["value"] = "updated" });

        Assert.Equal(expectedMode, File.GetUnixFileMode(path));
#endif
    }

    [Fact]
    public void BenchmarkJson_PreservesUnixSymbolicLinkDuringAtomicReplacement()
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;

        string root = CreateTempRoot();
        string target = Path.Combine(root, "versioned.json");
        string link = Path.Combine(root, "latest.json");
        File.WriteAllText(target, "{}");
        File.CreateSymbolicLink(link, target);

        BenchmarkJson.Write(link, new Dictionary<string, string> { ["value"] = "updated" });

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("updated", BenchmarkJson.Read<Dictionary<string, string>>(target)["value"]);
#endif
    }

    [Fact]
    public void BenchmarkJson_WritesThroughDanglingUnixSymbolicLink()
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;

        string root = CreateTempRoot();
        string target = Path.Combine(root, "versioned.json");
        string link = Path.Combine(root, "latest.json");
        File.CreateSymbolicLink(link, Path.GetFileName(target));

        BenchmarkJson.Write(link, new Dictionary<string, string> { ["value"] = "created" });

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("created", BenchmarkJson.Read<Dictionary<string, string>>(target)["value"]);
#endif
    }

    [Fact]
    public void EvidenceCatalog_ReplacesOnlyTheSamePlatformAndRunModeLane()
    {
        var service = new BenchmarkEvidenceCatalogService();
        var catalog = service.Update(null, Result("Windows", "fixture-a", 10), "tabular-65k-v1", "old.json", "quick", false);
        catalog = service.Update(catalog, Result("Windows", "fixture-a", 11), "tabular-65k-v1", "new.json", "quick", false);

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("new.json", entry.ResultPath);
    }

    [Fact]
    public void EvidenceCatalog_DoesNotReplaceNewerLaneWithLateOlderResult()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult newer = Result("Windows", "fixture-a", 10);
        BenchmarkRunResult older = Result("Windows", "fixture-a", 11);
        newer.FinishedUtc = DateTimeOffset.UtcNow;
        older.FinishedUtc = newer.FinishedUtc.AddMinutes(-5);

        BenchmarkEvidenceCatalog catalog = service.Update(
            null,
            newer,
            "comparison-a",
            "newer.json",
            "full",
            true);
        catalog = service.Update(
            catalog,
            older,
            "comparison-a",
            "older.json",
            "full",
            true);

        Assert.Equal("newer.json", Assert.Single(catalog.Entries).ResultPath);
    }

    [Fact]
    public async Task EvidenceCatalog_UpdateFilePreservesConcurrentWritersAndProducesValidJson()
    {
        var root = CreateTempRoot();
        string path = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        Task[] updates = Enumerable.Range(0, 12)
            .Select(index => Task.Run(() =>
                service.UpdateFile(
                    path,
                    Result("Windows", "fixture-a", 10 + index),
                    $"comparison-{index:D2}",
                    $"result-{index:D2}.json",
                    "full",
                    publish: true)))
            .ToArray();

        await Task.WhenAll(updates);

        BenchmarkEvidenceCatalog catalog = BenchmarkJson.Read<BenchmarkEvidenceCatalog>(path);
        Assert.Equal(12, catalog.Entries.Length);
        Assert.Equal(
            Enumerable.Range(0, 12).Select(index => $"comparison-{index:D2}"),
            catalog.Entries.Select(entry => entry.ComparisonId));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    private static string BenchmarkDotNetReport(
        string operatingSystem,
        string processorName = "Test CPU") =>
        $$"""
        {
          "Title": "OfficeIMO.LibraryComparison",
          "HostEnvironmentInfo": {
            "OsVersion": "{{operatingSystem}}",
            "ProcessorName": "{{processorName}}",
            "RuntimeVersion": ".NET 10.0.10",
            "Architecture": "X64"
          },
          "Benchmarks": [
            {
              "Method": "OfficeIMO",
              "Job": "full",
              "Statistics": { "Mean": 1000000 }
            }
          ]
        }
        """;

    private static BenchmarkRunResult Result(string platform, string fixture, double median)
    {
        var now = DateTimeOffset.UtcNow;
        return new BenchmarkRunResult
        {
            RunId = Guid.NewGuid().ToString("N"),
            Suite = "OfficeIMO.LibraryComparison",
            StartedUtc = now.AddMinutes(-1),
            FinishedUtc = now,
            Environment = new BenchmarkEnvironmentInfo
            {
                OsFamily = platform,
                OsArchitecture = "X64",
                ProcessArchitecture = "X64",
                RuntimeVersion = ".NET 10.0.10"
            },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gitSha"] = "abc123",
                ["benchmark.fixture.sha256"] = fixture,
                ["benchmark.package.officeimo"] = "3.0.3",
                ["benchmark.package.sylvan"] = "0.5.7"
            },
            Summary =
            [
                new BenchmarkSummaryRow
                {
                    Suite = "OfficeIMO.LibraryComparison",
                    Scenario = "CsvTyped",
                    Operation = "Read",
                    Engine = "OfficeIMO",
                    Os = platform,
                    RunMode = "full",
                    Status = "Succeeded",
                    MedianMs = median
                }
            ]
        };
    }
}
