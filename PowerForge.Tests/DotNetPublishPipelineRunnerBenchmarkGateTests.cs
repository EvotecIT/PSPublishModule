using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerBenchmarkGateTests
{
    [Fact]
    public void Plan_AddsBenchmarkExtractAndGateSteps_WhenConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = CreateProject(root, "App/App.csproj");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = root,
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "app",
                        ProjectPath = projectPath,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            UseStaging = false
                        }
                    }
                },
                BenchmarkGates = new[]
                {
                    new DotNetPublishBenchmarkGate
                    {
                        Id = "enterprise",
                        SourcePath = "Artifacts/Benchmarks/enterprise.log",
                        BaselinePath = "Build/Baselines/enterprise.baseline.json",
                        Metrics = new[]
                        {
                            new DotNetPublishBenchmarkMetric
                            {
                                Name = "FilterAvgMs",
                                Source = DotNetPublishBenchmarkMetricSource.Regex,
                                Pattern = "filter=avg=([0-9]+(?:\\.[0-9]+)?)ms",
                                Group = 1
                            }
                        }
                    }
                }
            };

            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            Assert.Single(plan.BenchmarkGates);
            Assert.Contains(plan.Steps, s => s.Kind == DotNetPublishStepKind.BenchmarkExtract && s.GateId == "enterprise");
            Assert.Contains(plan.Steps, s => s.Kind == DotNetPublishStepKind.BenchmarkGate && s.GateId == "enterprise");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_BenchmarkGateVerifyMode_FailsWhenRegressionDetected()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "bench.log");
            var baselinePath = Path.Combine(root, "baseline.json");

            File.WriteAllText(sourcePath, "probes=1000: filter=avg=150ms transform=avg=240ms");
            File.WriteAllText(
                baselinePath,
                "{\"metrics\":{\"FilterAvgMs\":100.0,\"TransformAvgMs\":200.0}}");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                BenchmarkGates = new[]
                {
                    new DotNetPublishBenchmarkGatePlan
                    {
                        Id = "enterprise",
                        Enabled = true,
                        SourcePath = sourcePath,
                        BaselinePath = baselinePath,
                        BaselineMode = DotNetPublishBaselineMode.Verify,
                        FailOnNew = true,
                        RelativeTolerance = 0.05,
                        AbsoluteToleranceMs = 5,
                        OnRegression = DotNetPublishPolicyMode.Fail,
                        OnMissingMetric = DotNetPublishPolicyMode.Fail,
                        Metrics = new[]
                        {
                            new DotNetPublishBenchmarkMetricPlan
                            {
                                Name = "FilterAvgMs",
                                Source = DotNetPublishBenchmarkMetricSource.Regex,
                                Pattern = "filter=avg=([0-9]+(?:\\.[0-9]+)?)ms",
                                Group = 1
                            },
                            new DotNetPublishBenchmarkMetricPlan
                            {
                                Name = "TransformAvgMs",
                                Source = DotNetPublishBenchmarkMetricSource.Regex,
                                Pattern = "transform=avg=([0-9]+(?:\\.[0-9]+)?)ms",
                                Group = 1
                            }
                        }
                    }
                },
                Steps = new[]
                {
                    new DotNetPublishStep { Key = "benchmark.extract:enterprise", Kind = DotNetPublishStepKind.BenchmarkExtract, GateId = "enterprise" },
                    new DotNetPublishStep { Key = "benchmark.gate:enterprise", Kind = DotNetPublishStepKind.BenchmarkGate, GateId = "enterprise" }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
            Assert.Equal(DotNetPublishStepKind.BenchmarkGate, result.Failure!.StepKind);
            Assert.Equal("enterprise", result.Failure.GateId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_BenchmarkGateUpdateMode_WritesBaseline()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "bench.log");
            var baselinePath = Path.Combine(root, "baseline.updated.json");

            File.WriteAllText(sourcePath, "probes=1000: filter=avg=120.5ms");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                BenchmarkGates = new[]
                {
                    new DotNetPublishBenchmarkGatePlan
                    {
                        Id = "storage",
                        Enabled = true,
                        SourcePath = sourcePath,
                        BaselinePath = baselinePath,
                        BaselineMode = DotNetPublishBaselineMode.Update,
                        OnMissingMetric = DotNetPublishPolicyMode.Fail,
                        Metrics = new[]
                        {
                            new DotNetPublishBenchmarkMetricPlan
                            {
                                Name = "FilterAvgMs",
                                Source = DotNetPublishBenchmarkMetricSource.Regex,
                                Pattern = "filter=avg=([0-9]+(?:\\.[0-9]+)?)ms",
                                Group = 1
                            }
                        }
                    }
                },
                Steps = new[]
                {
                    new DotNetPublishStep { Key = "benchmark.extract:storage", Kind = DotNetPublishStepKind.BenchmarkExtract, GateId = "storage" },
                    new DotNetPublishStep { Key = "benchmark.gate:storage", Kind = DotNetPublishStepKind.BenchmarkGate, GateId = "storage" }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);
            Assert.True(result.Succeeded, result.ErrorMessage);
            var gate = Assert.Single(result.BenchmarkGates);
            Assert.Equal("storage", gate.GateId);
            Assert.True(gate.BaselineUpdated);
            Assert.True(File.Exists(baselinePath));
            var baselineJson = File.ReadAllText(baselinePath);
            Assert.Contains("FilterAvgMs", baselineJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_BenchmarkGate_ExtractsMetricFromJsonPath()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "bench.json");
            var baselinePath = Path.Combine(root, "baseline.json");

            File.WriteAllText(sourcePath, "{\"Summary\":{\"ReportTotalSeconds\":{\"P95\":10.0}}}");
            File.WriteAllText(baselinePath, "{\"metrics\":{\"ReportTotalP95Ms\":9.0}}");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                BenchmarkGates = new[]
                {
                    new DotNetPublishBenchmarkGatePlan
                    {
                        Id = "json-gate",
                        Enabled = true,
                        SourcePath = sourcePath,
                        BaselinePath = baselinePath,
                        BaselineMode = DotNetPublishBaselineMode.Verify,
                        RelativeTolerance = 0.20,
                        AbsoluteToleranceMs = 0,
                        OnRegression = DotNetPublishPolicyMode.Fail,
                        OnMissingMetric = DotNetPublishPolicyMode.Fail,
                        Metrics = new[]
                        {
                            new DotNetPublishBenchmarkMetricPlan
                            {
                                Name = "ReportTotalP95Ms",
                                Source = DotNetPublishBenchmarkMetricSource.JsonPath,
                                Path = "Summary.ReportTotalSeconds.P95",
                                Aggregation = DotNetPublishBenchmarkMetricAggregation.Last
                            }
                        }
                    }
                },
                Steps = new[]
                {
                    new DotNetPublishStep { Key = "benchmark.extract:json-gate", Kind = DotNetPublishStepKind.BenchmarkExtract, GateId = "json-gate" },
                    new DotNetPublishStep { Key = "benchmark.gate:json-gate", Kind = DotNetPublishStepKind.BenchmarkGate, GateId = "json-gate" }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);
            Assert.True(result.Succeeded, result.ErrorMessage);
            var gate = Assert.Single(result.BenchmarkGates);
            Assert.True(gate.Passed);
            var metric = Assert.Single(gate.Metrics);
            Assert.Equal("ReportTotalP95Ms", metric.Name);
            Assert.Equal(10.0, metric.Actual);
            Assert.Equal(9.0, metric.Baseline);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_WritesRunReport_WhenConfigured()
    {
        var root = CreateTempRoot();
        try
        {
            var runReportPath = Path.Combine(root, "Artifacts", "DotNetPublish", "run-report.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs
                {
                    RunReportPath = runReportPath
                },
                Steps = new[]
                {
                    new DotNetPublishStep
                    {
                        Key = "manifest",
                        Kind = DotNetPublishStepKind.Manifest,
                        Title = "Write manifest"
                    }
                }
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(runReportPath, result.RunReportPath);
            Assert.True(File.Exists(runReportPath));

            using var reportJson = JsonDocument.Parse(File.ReadAllText(runReportPath));
            Assert.Equal(JsonValueKind.Object, reportJson.RootElement.ValueKind);
            var steps = reportJson.RootElement
                .EnumerateObject()
                .FirstOrDefault(p => p.Name.Equals("steps", StringComparison.OrdinalIgnoreCase))
                .Value;
            Assert.Equal(JsonValueKind.Array, steps.ValueKind);
            Assert.True(steps.EnumerateArray().Any());
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateProject(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return fullPath;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
