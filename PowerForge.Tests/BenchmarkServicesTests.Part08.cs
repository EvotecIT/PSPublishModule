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
          "Title": "OfficeIMO.Tabular",
          "HostEnvironmentInfo": {
            "BenchmarkDotNetCaption": "BenchmarkDotNet v0.15.8",
            "OsVersion": "Microsoft Windows 11 (10.0.26200.8875)",
            "ProcessorName": "AMD Ryzen 9 9950X3D2",
            "PhysicalProcessorCount": 1,
            "PhysicalCoreCount": 16,
            "LogicalCoreCount": 32,
            "RuntimeVersion": ".NET 10.0.10",
            "Architecture": "X64",
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
          "Title": "OfficeIMO.Tabular",
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
    public void EvidenceCatalog_ReplacesOnlyTheSamePlatformAndRunModeLane()
    {
        var service = new BenchmarkEvidenceCatalogService();
        var catalog = service.Update(null, Result("Windows", "fixture-a", 10), "tabular-65k-v1", "old.json", "quick", false);
        catalog = service.Update(catalog, Result("Windows", "fixture-a", 11), "tabular-65k-v1", "new.json", "quick", false);

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("new.json", entry.ResultPath);
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

    private static string BenchmarkDotNetReport(string operatingSystem) =>
        $$"""
        {
          "Title": "OfficeIMO.Tabular",
          "HostEnvironmentInfo": {
            "OsVersion": "{{operatingSystem}}",
            "ProcessorName": "Test CPU",
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
            Suite = "OfficeIMO.Tabular",
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
                    Suite = "OfficeIMO.Tabular",
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
