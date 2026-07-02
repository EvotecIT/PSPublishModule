using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void Importer_PreservesRunModeFromNormalizedSampleCsv()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,OS,RunMode,Iteration,Status,DurationMs,Reason\nsuite,case,Run,Managed,Current,Windows,publish,0,Succeeded,12,\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("publish", sample.RunMode);
        Assert.Equal("publish", row.RunMode);
    }

    [Fact]
    public void Importer_FailsNonFiniteCsvDurations()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Mean\nWrite,NaN\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        var sample = Assert.Single(result.Samples);
        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal(0, sample.DurationMs);
        Assert.Contains("Duration", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Failed", Assert.Single(result.Summary).Status);
    }

    [Fact]
    public void Importer_DropsNonFiniteByteSizeCsvMetrics()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Suite,Scenario,Operation,Engine,Host,Iteration,Status,DurationMs,Reason,Allocated\nsuite,Write,Run,Managed,Current,0,Succeeded,12.5,,NaN KB\n");

        var result = new BenchmarkResultImporter().Import(csv);
        var sample = Assert.Single(result.Samples);

        Assert.Equal(BenchmarkSampleStatus.Succeeded, sample.Status);
        Assert.DoesNotContain("Allocated", sample.Metrics.Keys);
    }

    [Fact]
    public void Importer_PropagatesSuiteOverrideIntoNormalizedRun()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "run-report.json");
        BenchmarkJson.Write(path, new BenchmarkRunResult
        {
            Suite = "old",
            Samples = new[] { Sample("old", "case", "Run", "Managed", 1) }
        });

        var result = new BenchmarkResultImporter().Import(path, "new");

        Assert.Equal("new", result.Suite);
        Assert.Equal("new", Assert.Single(result.Samples).Suite);
        Assert.Equal("new", Assert.Single(result.Summary).Suite);
    }

    [Fact]
    public void Importer_ReadsSamplesJsonArrayAsSamples()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "samples.json");
        BenchmarkJson.Write(path, new[] { Sample("suite", "case", "Run", "Managed", 2) });

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Single(result.Samples);
        Assert.Equal(2, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_DirectoryUsesLatestRunReport()
    {
        var root = CreateTempRoot();
        var older = Path.Combine(root, "20260101-000000-old");
        var newer = Path.Combine(root, "20260102-000000-new");
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(newer);
        var olderReport = Path.Combine(older, "run-report.json");
        var newerReport = Path.Combine(newer, "run-report.json");
        BenchmarkJson.Write(olderReport, new BenchmarkRunResult { Suite = "old", Samples = new[] { Sample("old", "case", "Run", "Managed", 1) } });
        BenchmarkJson.Write(newerReport, new BenchmarkRunResult { Suite = "new", Samples = new[] { Sample("new", "case", "Run", "Managed", 2) } });
        File.SetLastWriteTimeUtc(olderReport, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newerReport, DateTime.UtcNow);

        var result = new BenchmarkResultImporter().Import(root);

        Assert.Equal("new", result.Suite);
        Assert.Single(result.Samples);
    }

    [Fact]
    public void Importer_ReadsBenchmarkDotNetJson()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "DisplayInfo": "Write",
      "Statistics": {
        "Mean": 1500000
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        var row = Assert.Single(result.Summary);
        Assert.Equal("Write", row.Scenario);
        Assert.Equal(1.5, row.MedianMs);
    }

    [Fact]
    public void Importer_UsesStableBenchmarkDotNetMethodName()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "DisplayInfo": "Write [Rows=10]",
      "Method": "Write",
      "Parameters": "Rows=10",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var sample = Assert.Single(result.Samples);

        Assert.Equal("Write", sample.Scenario);
        Assert.Equal("10", sample.Variables["Rows"]);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJobIdentity()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Job": "Net80",
      "Parameters": "Rows=10",
      "Statistics": {
        "Median": 500
      }
    },
    {
      "Method": "Write",
      "Job": "Net10",
      "Parameters": "Rows=10",
      "Statistics": {
        "Median": 700
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Contains(result.Summary, row => row.Engine == "Net80" && row.Variables["Rows"] == "10");
        Assert.Contains(result.Summary, row => row.Engine == "Net10" && row.Variables["Rows"] == "10");
    }

    [Fact]
    public void Importer_DirectoryDiscoversBenchmarkDotNetJsonReports()
    {
        var root = CreateTempRoot();
        var artifactRoot = Path.Combine(root, "BenchmarkDotNet.Artifacts");
        Directory.CreateDirectory(artifactRoot);
        File.WriteAllText(Path.Combine(artifactRoot, "Demo-report-full-compressed.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(root);

        Assert.Single(result.Samples);
        Assert.Equal("Write", Assert.Single(result.Summary).Scenario);
    }

    [Fact]
    public void Importer_DirectoryUsesDirectorySuiteForBenchmarkDotNetJsonReports()
    {
        var root = CreateTempRoot();
        var artifactRoot = Path.Combine(root, "DirectorySuite");
        Directory.CreateDirectory(artifactRoot);
        File.WriteAllText(Path.Combine(artifactRoot, "Demo-report-full.json"), """
{
  "Title": "json-title",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(artifactRoot);
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("DirectorySuite", result.Suite);
        Assert.Equal("DirectorySuite", sample.Suite);
        Assert.Equal("DirectorySuite", row.Suite);
    }

    [Fact]
    public void Importer_DirectoryPrefersFullBenchmarkDotNetJsonOverCsv()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "Demo-report.csv"), "Method,Median\nWrite,1.000 ms\n");
        File.WriteAllText(Path.Combine(root, "Demo-report-full.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 200
      },
      "Memory": {
        "BytesAllocatedPerOperation": 4096
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(root);
        var row = Assert.Single(result.Summary);

        Assert.InRange(row.MedianMs.GetValueOrDefault(), 0.000199, 0.000201);
        Assert.Equal("Demo.Bench", row.Variables["Type"]);
        Assert.Equal(4096, row.Metrics["Allocated"]);
    }

    [Fact]
    public void Importer_UsesBenchmarkDotNetMedianAndScalesNanoseconds()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "DisplayInfo": "Write",
      "Statistics": {
        "Mean": 900,
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Equal(0.0005, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetHostAndSplitsParameters()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "HostEnvironmentInfo": {
    "BenchmarkDotNetCaption": "BenchmarkDotNet v0.15",
    "RuntimeVersion": ".NET 10.0",
    "Architecture": "X64",
    "OperatingSystem": "Windows"
  },
  "Benchmarks": [
    {
      "DisplayInfo": "Write",
      "Parameters": "Rows=10, Profile=Fast",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);
        var sample = Assert.Single(result.Samples);

        Assert.Contains(".NET 10.0", sample.Host, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("10", sample.Variables["Rows"]);
        Assert.Equal("Fast", sample.Variables["Profile"]);
        Assert.DoesNotContain("Parameters", sample.Variables.Keys);
    }

    [Fact]
    public void Importer_ReadsNormalizedCsvDurations()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Scenario,Operation,Engine,Host,DurationMs\nWrite,Run,Managed,Current,12.5\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        Assert.Equal(12.5, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_FailsSucceededCsvSamplesWithoutDuration()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "Scenario,Operation,Engine,Host,Status,DurationMs\nWrite,Run,Managed,Current,Succeeded,\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal("Failed", row.Status);
        Assert.Equal(1, row.FailureCount);
        Assert.Null(row.MedianMs);
        Assert.Contains("Duration", sample.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Importer_PrefersBenchmarkDotNetMedianUnits()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Median [us],Mean [us]\nWrite,1500,9000\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");

        Assert.Equal(1.5, Assert.Single(result.Summary).MedianMs);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetScenarioParameter()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Scenario,Mean [ms]\nInstall,Az,12\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("Install", sample.Scenario);
        Assert.Equal("Az", sample.Variables["Scenario"]);
        Assert.DoesNotContain("Method", sample.Variables.Keys);
        Assert.Equal("Install", row.Scenario);
        Assert.Equal("Az", row.Variables["Scenario"]);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetStatisticsAsMetrics()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Rows,Median [us],Mean [us],Min [us],Max [us],P95 [ns],Error,StdDev [us],Allocated\nWrite,10,1500,9000,1000,12000,950,1.2,3400,8 KB\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("10", sample.Variables["Rows"]);
        Assert.DoesNotContain("Median [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Mean [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Min [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Max [us]", sample.Variables.Keys);
        Assert.DoesNotContain("P95 [ns]", sample.Variables.Keys);
        Assert.DoesNotContain("Error", sample.Variables.Keys);
        Assert.DoesNotContain("StdDev [us]", sample.Variables.Keys);
        Assert.DoesNotContain("Allocated", sample.Variables.Keys);
        Assert.Equal(1.5, sample.Metrics["MedianMs"]);
        Assert.Equal(9, sample.Metrics["MeanMs"]);
        Assert.Equal(1, sample.Metrics["MinMs"]);
        Assert.Equal(12, sample.Metrics["MaxMs"]);
        Assert.InRange(sample.Metrics["P95"], 0.000949, 0.000951);
        Assert.Equal(1.2, sample.Metrics["Error"]);
        Assert.Equal(3.4, sample.Metrics["StdDev"]);
        Assert.Equal(8192, sample.Metrics["Allocated"]);
        Assert.Equal(1.5, row.MedianMs);
        Assert.Equal(9, row.MeanMs);
        Assert.Equal(1, row.MinMs);
        Assert.Equal(12, row.MaxMs);
        Assert.Equal(8192, row.Metrics["Allocated"]);
    }

    [Fact]
    public void Importer_PreservesUnparseableBenchmarkDotNetStatisticNamedParameters()
    {
        var root = CreateTempRoot();
        var csv = Path.Combine(root, "Demo-report.csv");
        File.WriteAllText(csv, "Method,Allocated,Ratio,Gen0,Mean [ms]\nWrite,LargeObject,baseline,GroupA,12\n");

        var result = new BenchmarkResultImporter().Import(csv, "demo");
        var sample = Assert.Single(result.Samples);
        var row = Assert.Single(result.Summary);

        Assert.Equal("LargeObject", sample.Variables["Allocated"]);
        Assert.Equal("baseline", sample.Variables["Ratio"]);
        Assert.Equal("GroupA", sample.Variables["Gen0"]);
        Assert.DoesNotContain("Allocated", sample.Metrics.Keys);
        Assert.DoesNotContain("Ratio", sample.Metrics.Keys);
        Assert.DoesNotContain("Gen0", sample.Metrics.Keys);
        Assert.Equal("LargeObject", row.Variables["Allocated"]);
        Assert.Equal("baseline", row.Variables["Ratio"]);
        Assert.Equal("GroupA", row.Variables["Gen0"]);
        Assert.Equal(12, row.MeanMs);
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJsonTypeIdentity()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.FastBench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 500
      }
    },
    {
      "FullName": "Demo.SlowBench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 700
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Equal(2, result.Summary.Length);
        Assert.Contains(result.Summary, row => row.Scenario == "Write" && row.Variables["Type"] == "Demo.FastBench");
        Assert.Contains(result.Summary, row => row.Scenario == "Write" && row.Variables["Type"] == "Demo.SlowBench");
    }

    [Fact]
    public void Importer_PreservesBenchmarkDotNetJsonTypeParameter()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full-compressed.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Parameters": "Type=Fast",
      "Statistics": {
        "Median": 500
      }
    },
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Parameters": "Type=Slow",
      "Statistics": {
        "Median": 700
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(path);

        Assert.Equal(2, result.Summary.Length);
        Assert.Contains(result.Summary, row => row.Variables["Type"] == "Fast" && row.Variables["BenchmarkDotNetType"] == "Demo.Bench");
        Assert.Contains(result.Summary, row => row.Variables["Type"] == "Slow" && row.Variables["BenchmarkDotNetType"] == "Demo.Bench");
    }

    [Fact]
    public void Importer_ParsesQuotedBenchmarkDotNetJsonParameters()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Parameters": "Name=\"A,B\"; Mode='C;D'",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var sample = Assert.Single(new BenchmarkResultImporter().Import(path).Samples);

        Assert.Equal("A,B", sample.Variables["Name"]);
        Assert.Equal("C;D", sample.Variables["Mode"]);
    }

    [Fact]
    public void Importer_RejectsNonFiniteBenchmarkDotNetJsonStatistics()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "Demo-report-full.json");
        File.WriteAllText(path, """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": "NaN",
        "Mean": "Infinity"
      }
    }
  ]
}
""");

        var sample = Assert.Single(new BenchmarkResultImporter().Import(path).Samples);

        Assert.Equal(BenchmarkSampleStatus.Failed, sample.Status);
        Assert.Equal(0, sample.DurationMs);
        Assert.Contains("duration", sample.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MedianMs", sample.Metrics.Keys);
        Assert.DoesNotContain("MeanMs", sample.Metrics.Keys);
    }

    [Fact]
    public void Importer_DirectoryDeduplicatesBenchmarkDotNetJsonVariants()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "Demo-report.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 100
      }
    }
  ]
}
""");
        File.WriteAllText(Path.Combine(root, "Demo-report-full.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "FullName": "Demo.Bench.Write()",
      "Method": "Write",
      "Statistics": {
        "Median": 200
      }
    }
  ]
}
""");

        var result = new BenchmarkResultImporter().Import(root);
        var row = Assert.Single(result.Summary);

        Assert.InRange(row.MedianMs.GetValueOrDefault(), 0.000199, 0.000201);
        Assert.Equal("Demo.Bench", row.Variables["Type"]);
    }

    [Fact]
    public void Importer_DirectorySkipsUnrelatedBenchmarkDotNetReportJson()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "coverage-report.json"), """{"coverage": 100}""");
        File.WriteAllText(Path.Combine(root, "Demo-report.json"), """
{
  "Title": "demo",
  "Benchmarks": [
    {
      "Method": "Write",
      "Statistics": {
        "Median": 500
      }
    }
  ]
}
""");

        var row = Assert.Single(new BenchmarkResultImporter().Import(root).Summary);

        Assert.Equal("Write", row.Scenario);
        Assert.Equal(0.0005, row.MedianMs);
    }

}
