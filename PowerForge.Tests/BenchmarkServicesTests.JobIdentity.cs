using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void BenchmarkDotNetDisplayInfo_SeparatesJobsFromActualJsonExporter()
    {
        // Four unchanged benchmark objects from JsonExporter.Full in BenchmarkDotNet0.15.8.
        // These Dry results test identity and unit conversion, not performance.
        using var stream = typeof(BenchmarkServicesTests).Assembly.GetManifestResourceStream(
            "PowerForge.Tests.Fixtures.Benchmarks.BenchmarkDotNet-0.15.8-jobs.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var result = ImportJobIdentityReport(json);
        using var document = JsonDocument.Parse(json);
        var exported = document.RootElement.GetProperty("Benchmarks").EnumerateArray().ToArray();

        Assert.Equal(4, result.Samples.Length);
        Assert.Equal(4, result.Summary.Length);
        Assert.Equal(2, result.Summary.Select(row => row.Engine).Distinct().Count());
        for (int index = 0; index < exported.Length; index++)
        {
            var benchmark = exported[index];
            var display = benchmark.GetProperty("DisplayInfo").GetString()!;
            var parameters = benchmark.GetProperty("Parameters").GetString()!;
            var expectedJob = display.Substring("JpegCalibration.Decode: ".Length);
            expectedJob = expectedJob.Substring(0, expectedJob.Length - (" [" + parameters + "]").Length);
            var sample = result.Samples[index];
            Assert.Equal(expectedJob, sample.Engine);
            Assert.Equal("Decode", sample.Scenario);
            Assert.Equal(parameters.Substring("Case=".Length), sample.Variables["Case"]);
            Assert.Equal(benchmark.GetProperty("Statistics").GetProperty("OriginalValues")[0].GetDouble() * 0.000001,
                sample.DurationMs);
            var row = Assert.Single(result.Summary, row => row.Engine == sample.Engine && row.Variables["Case"] == sample.Variables["Case"]);
            Assert.Equal(1, row.SampleCount);
            Assert.Equal(sample.DurationMs, row.MedianMs);
        }
    }

    [Theory]
    [InlineData("Bench.Decode: Dry", "Decode", "", "Dry")]
    [InlineData("Bench<Int32, String>.'decode: words': Control(Runtime=.NET10.0) [Rows=10, Label=text [42]&value]", "'decode: words'", "Rows=10&Label=text [42]&value", "Control(Runtime=.NET10.0)")]
    [InlineData("Bench.Decode: custom(id): words(EnvironmentVariables=VALUE=a [b]) [Case=one [two]]", "Decode", "Case=one [two]", "custom(id): words(EnvironmentVariables=VALUE=a [b])")]
    [InlineData("Bench.Decode: DefaultJob [Name=A,B]", "Decode", "Name=A,B", "DefaultJob")]
    public void BenchmarkDotNetDisplayInfo_PreservesJobSettingsAndDisplayBoundaries(
        string display, string title, string parameters, string expected)
    {
        var benchmark = new { DisplayInfo = display, Method = "Decode", MethodTitle = title, Parameters = parameters, Statistics = new { Mean = 1_000_000 } };
        var result = ImportJobIdentityReport(JsonSerializer.Serialize(new { Benchmarks = new[] { benchmark } }));

        Assert.Equal(expected, Assert.Single(result.Samples).Engine);
        Assert.Equal("Decode", Assert.Single(result.Summary).Scenario);
    }

    [Theory]
    [InlineData("\"Job\":\"Explicit\"", "Explicit")]
    [InlineData("\"JobDisplayInfo\":\"Explicit display\"", "Explicit display")]
    [InlineData("\"JobId\":\"Explicit id\"", "Explicit id")]
    [InlineData("\"Job\":{\"Id\":\"Net80\",\"Runtime\":\".NET8.0\"}", "Net80; .NET8.0")]
    public void BenchmarkDotNetDisplayInfo_KeepsExplicitJobMetadataPrecedence(string job, string expected)
    {
        var result = ImportJobIdentityReport("{\"Benchmarks\":[{" + job +
            ",\"DisplayInfo\":\"Bench.Decode: Fallback(Runtime=.NET10.0)\",\"Method\":\"Decode\",\"Statistics\":{\"Mean\":1000000}}]}");

        Assert.Equal(expected, Assert.Single(result.Summary).Engine);
    }

    [Fact]
    public void BenchmarkDotNetDisplayInfo_KeepsSameNamedJobsWithDifferentSettingsSeparate()
    {
        var result = ImportJobIdentityReport("""
            {"Benchmarks":[
              {"Method":"Decode","DisplayInfo":"Bench.Decode: Named(Runtime=.NET8.0)","Statistics":{"OriginalValues":[1000000,2000000]}},
              {"Method":"Decode","DisplayInfo":"Bench.Decode: Named(Runtime=.NET10.0)","Statistics":{"OriginalValues":[3000000,4000000]}}
            ]}
            """);

        Assert.Equal(4, result.Samples.Length);
        Assert.Equal(2, result.Summary.Length);
        Assert.Contains(result.Summary, row => row.Engine == "Named(Runtime=.NET8.0)" && row.SampleCount == 2 && row.MedianMs == 1.5);
        Assert.Contains(result.Summary, row => row.Engine == "Named(Runtime=.NET10.0)" && row.SampleCount == 2 && row.MedianMs == 3.5);
    }

    [Theory]
    [InlineData("Bench.Other: Dry [Rows=10]", "Rows=10")]
    [InlineData("legacy: job-looking text", "")]
    [InlineData("Bench.Decode: Dry [Rows=20]", "Rows=10")]
    public void BenchmarkDotNetDisplayInfo_DoesNotGuessJobsFromUnrecognizedDisplays(string display, string parameters)
    {
        var benchmark = new { DisplayInfo = display, Method = "Decode", Parameters = parameters, Statistics = new { Mean = 1_000_000 } };
        var result = ImportJobIdentityReport(JsonSerializer.Serialize(new { Benchmarks = new[] { benchmark } }));

        Assert.Equal("BenchmarkDotNet", Assert.Single(result.Summary).Engine);
    }

    [Fact]
    public void BenchmarkDotNetDisplayInfo_ImportsAmpersandSeparatedParametersWithoutSplittingQuotedValues()
    {
        var benchmark = new { Method = "Decode", Parameters = "Rows=10&Label=\"A&B\"&Mode='C&D'", Statistics = new { Mean = 1_000_000 } };
        var result = ImportJobIdentityReport(JsonSerializer.Serialize(new { Benchmarks = new[] { benchmark } }));
        var sample = Assert.Single(result.Samples);

        Assert.Equal("10", sample.Variables["Rows"]);
        Assert.Equal("A&B", sample.Variables["Label"]);
        Assert.Equal("C&D", sample.Variables["Mode"]);
    }

    [Fact]
    public void BenchmarkDotNetDisplayInfo_PreservesNativeParameterPunctuationAndEmptyValues()
    {
        var benchmark = new { DisplayInfo = "Bench.Decode: Dry [Name=one,two; O'Brien&value, Empty=]", Method = "Decode",
            Parameters = "Name=one,two; O'Brien&value&Empty=", Statistics = new { Mean = 1_000_000 } };
        var sample = Assert.Single(ImportJobIdentityReport(JsonSerializer.Serialize(new { Benchmarks = new[] { benchmark } })).Samples);

        Assert.Equal("Dry", sample.Engine);
        Assert.Equal("one,two; O'Brien&value", sample.Variables["Name"]);
        Assert.Equal("", sample.Variables["Empty"]);
        Assert.Equal(2, sample.Variables.Count);
    }

    private static BenchmarkRunResult ImportJobIdentityReport(string json)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.JobIdentity." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "Case-report-full.json");
            File.WriteAllText(path, json);
            return new BenchmarkResultImporter().Import(path);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
