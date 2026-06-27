using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleBenchmarkReportWriterTests
{
    [Fact]
    public void BuildMarkdown_IncludesNeutralScenarioSummary()
    {
        var writer = new ManagedModuleBenchmarkReportWriter();
        var result = new ManagedModuleBenchmarkResult
        {
            StartedAtUtc = new DateTimeOffset(2026, 6, 27, 10, 0, 0, TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(2026, 6, 27, 10, 1, 0, TimeSpan.Zero),
            Runs = new[]
            {
                new ManagedModuleBenchmarkRunResult
                {
                    ScenarioId = "install-small",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Engine = "Managed",
                    Iteration = 1,
                    Succeeded = true,
                    Status = "Installed",
                    ModuleName = "Company.Tools",
                    Version = "1.0.0",
                    Elapsed = TimeSpan.FromMilliseconds(100),
                    PackageCount = 1,
                    TotalPackageBytes = 200,
                    FinalDiskBytes = 300
                },
                new ManagedModuleBenchmarkRunResult
                {
                    ScenarioId = "install-small",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Engine = "Managed",
                    Iteration = 2,
                    Succeeded = true,
                    Status = "Installed",
                    ModuleName = "Company.Tools",
                    Version = "1.0.0",
                    Elapsed = TimeSpan.FromMilliseconds(300),
                    PackageCount = 1,
                    TotalPackageBytes = 220,
                    FinalDiskBytes = 300
                },
                new ManagedModuleBenchmarkRunResult
                {
                    ScenarioId = "install-small",
                    Operation = ManagedModuleBenchmarkOperation.Install,
                    Engine = "PSResourceGet",
                    Iteration = 1,
                    Succeeded = false,
                    Status = "Failed",
                    ModuleName = "Company.Tools",
                    Elapsed = TimeSpan.FromMilliseconds(500),
                    ErrorMessage = "Baseline unavailable."
                }
            }
        };

        var markdown = writer.BuildMarkdown(result);

        Assert.Contains("## Scenario Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("| install-small | Install | Managed | 2 | 2 | 0 | 200 | 200 | 100 | 300 | 2 | 420 | 600 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| install-small | Install | PSResourceGet | 1 | 0 | 1 | 500 | 500 | 500 | 500 | 0 | 0 | 0 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Scenario | Module | Engine | Operation | Iteration | Status |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("faster", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMarkdown_RendersEmptyRunSummary()
    {
        var writer = new ManagedModuleBenchmarkReportWriter();
        var markdown = writer.BuildMarkdown(new ManagedModuleBenchmarkResult
        {
            StartedAtUtc = DateTimeOffset.UnixEpoch,
            CompletedAtUtc = DateTimeOffset.UnixEpoch
        });

        Assert.Contains("## Scenario Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("_No benchmark runs were recorded._", markdown, StringComparison.Ordinal);
    }
}
