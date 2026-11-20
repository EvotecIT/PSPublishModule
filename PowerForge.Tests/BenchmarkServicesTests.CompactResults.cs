using System.Text;
using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void CompactResult_StoresEachCaseOnceAndReadsBackTheSamples()
    {
        BenchmarkRunResult result = BenchmarkDotNetStyleResult();

        byte[] bytes = BenchmarkEvidenceResultCodec.Encode(result);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;

        Assert.Equal("powerforge.benchmark-result", root.GetProperty("format").GetString());
        Assert.Equal(2, root.GetProperty("formatVersion").GetInt32());
        Assert.False(root.TryGetProperty("samples", out _));
        Assert.False(root.TryGetProperty("summary", out _));
        Assert.Equal(1, root.GetProperty("engines").GetArrayLength());
        JsonElement first = root.GetProperty("cases")[0];
        Assert.Equal(15, first.GetProperty("durationsMs").GetArrayLength());
        // The job string, variables and metrics appear once per case; null and empty values are left out.
        string json = Encoding.UTF8.GetString(bytes);
        Assert.Equal(1, Occurrences(json, "Job-ABC(IterationCount=15"));
        Assert.Equal(2, Occurrences(json, "\"FullName\""));
        Assert.DoesNotContain("\"Median\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Gen0Collections\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("allocatedBytes", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reason\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"host\"", json, StringComparison.Ordinal);
        // Durations keep six significant digits.
        Assert.Equal(1.23457, first.GetProperty("durationsMs")[0].GetDouble());

        BenchmarkRunResult read = BenchmarkEvidenceResultCodec.Decode(bytes, "compact");
        Assert.Equal(result.Samples.Length, read.Samples.Length);
        Assert.All(read.Samples, sample => Assert.Equal("Job-ABC(IterationCount=15, LaunchCount=1, WarmupCount=5)", sample.Engine));
        Assert.Equal(
            result.Samples.Select(sample => BenchmarkEvidenceResultCodec.Round(sample.DurationMs)),
            read.Samples.Select(sample => sample.DurationMs));
        Assert.Equal(result.Samples.Select(sample => sample.Iteration), read.Samples.Select(sample => sample.Iteration));
        Assert.Equal(1105d, read.Samples[0].Metrics["Gen0"]);
        Assert.Equal(512d, read.Samples[0].Metrics["BytesAllocatedPerOperation"]);
        Assert.Equal("OfficeIMO.Read(Rows: 100)", read.Samples[0].Variables["FullName"]);
        // Summary rows are rebuilt from the samples, as publication validates them.
        BenchmarkSummaryRow[] expected = new BenchmarkSummaryService().Summarize(read.Samples);
        Assert.Equal(BenchmarkJson.Serialize(expected), BenchmarkJson.Serialize(read.Summary));

        // Encoding is stable, so a result has one published form and one hash, also after a trip through the full form.
        Assert.Equal(Encoding.UTF8.GetString(bytes), Encoding.UTF8.GetString(BenchmarkEvidenceResultCodec.Encode(read)));
        BenchmarkRunResult full = BenchmarkEvidenceResultCodec.Decode(BenchmarkJson.SerializeCanonicalBytes(read), "full");
        Assert.Equal(Encoding.UTF8.GetString(bytes), Encoding.UTF8.GetString(BenchmarkEvidenceResultCodec.Encode(full)));
        Assert.True(bytes.Length * 5 < BenchmarkJson.SerializeCanonicalBytes(result).Length);
    }

    [Fact]
    public void CompactResult_KeepsFailedSamplesAndUnsampledSummaryRows()
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", null));
        result.Summary = result.Summary.Append(new BenchmarkSummaryRow
        {
            Suite = "csv",
            Scenario = "Write",
            Operation = "Run",
            Engine = "EPPlus",
            Library = "EPPlus",
            Status = "Failed",
            FailureCount = 1,
            FailureReasons = new Dictionary<string, int> { ["Out of memory."] = 1 }
        }).ToArray();

        BenchmarkRunResult read = BenchmarkEvidenceResultCodec.Normalize(result);

        BenchmarkSample failed = Assert.Single(read.Samples, sample => sample.Library == "Sep");
        Assert.Equal(BenchmarkSampleStatus.Failed, failed.Status);
        Assert.Equal("Process exited with exit code 255.", failed.Reason);
        BenchmarkSummaryRow unsampled = Assert.Single(read.Summary, row => row.Library == "EPPlus");
        Assert.Equal("Failed", unsampled.Status);
        Assert.Equal(1, unsampled.FailureReasons["Out of memory."]);
    }

    [Fact]
    public void CompactResult_ReadsResultsWrittenInTheEarlierFullForm()
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        byte[] legacy = BenchmarkJson.SerializeCanonicalBytes(result);

        Assert.False(BenchmarkEvidenceResultCodec.IsCompact(legacy));
        BenchmarkRunResult read = BenchmarkEvidenceResultCodec.Decode(legacy, "legacy");

        Assert.Equal(BenchmarkJson.Serialize(result), BenchmarkJson.Serialize(read));
    }

    [Fact]
    public void EvidenceCatalog_MergeRewritesEarlierFullResultsInTheCompactForm()
    {
        string root = CreateTempRoot();
        var service = new BenchmarkEvidenceCatalogService();
        string sourcePath = Path.Combine(root, "source", "index.json");
        BenchmarkEvidenceCatalog source = service.UpdateFile(sourcePath, PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10))), TestDeclarations()));
        // Rewrite the recorded lane as an earlier full result, as catalogs before the compact form stored it.
        BenchmarkEvidenceEntry recorded = Assert.Single(source.Entries);
        string recordedPath = Path.Combine(root, "source", recorded.ArtifactRelativePath);
        byte[] legacy = BenchmarkJson.SerializeCanonicalBytes(BenchmarkEvidenceResultCodec.Read(recordedPath));
        string recordedHash = recorded.ResultSha256;
        string legacyHash = BenchmarkEvidenceResultCodec.ComputeSha256(legacy);
        string legacyName = recorded.ArtifactRelativePath.Replace(recordedHash, legacyHash);
        File.Delete(recordedPath);
        File.WriteAllBytes(Path.Combine(root, "source", legacyName), legacy);
        source.Entries[0].ResultSha256 = legacyHash;
        source.Entries[0].ResultPath = source.Entries[0].ResultPath.Replace(recordedHash, legacyHash);
        source.Entries[0].ArtifactRelativePath = legacyName;
        source.Entries[0].ArtifactFileName = legacyName;
        BenchmarkJson.Write(sourcePath, source);

        string mergedPath = Path.Combine(root, "merged", "index.json");
        BenchmarkEvidenceCatalog merged = service.MergeFiles(mergedPath, [sourcePath]);

        BenchmarkEvidenceEntry entry = Assert.Single(merged.Entries);
        Assert.True(entry.Publish);
        Assert.Equal(recordedHash, entry.ResultSha256);
        byte[] stored = File.ReadAllBytes(Path.Combine(root, "merged", entry.ArtifactRelativePath));
        Assert.True(BenchmarkEvidenceResultCodec.IsCompact(stored));
        Assert.Equal(entry.ResultSha256, BenchmarkEvidenceResultCodec.ComputeSha256(stored));
    }

    [Fact]
    public void ComparisonView_NotesNameTheValidationFailureAndTheCasesLeftOut()
    {
        var entry = new BenchmarkEvidenceEntry
        {
            Publish = true,
            Cells =
            [
                new BenchmarkEvidenceCell
                {
                    Library = "PeachPDF",
                    Case = "Default",
                    Publishable = false,
                    Issues =
                    [
                        "Cell failed: BenchmarkDotNet JSON duration could not be parsed.",
                        "Cell has 0 measured samples; at least 10 are required.",
                        "Output validation failed: the authored semantic-owner order differs."
                    ]
                },
                new BenchmarkEvidenceCell { Library = "OfficeIMO", Case = "Default", Publishable = true },
                new BenchmarkEvidenceCell
                {
                    Library = "OfficeIMO",
                    Case = "LazyValues",
                    Publishable = false,
                    Issues = ["Output validation was not recorded for this library and case."]
                }
            ]
        };

        BenchmarkNotPublishedView[] notes = BenchmarkComparisonViewBuilder.DescribeNotPublished(entry);

        BenchmarkNotPublishedView peach = Assert.Single(notes, note => note.Library == "PeachPDF");
        Assert.Equal("Output validation failed: the authored semantic-owner order differs.", peach.Text);
        Assert.Empty(peach.Cases);
        BenchmarkNotPublishedView officeImo = Assert.Single(notes, note => note.Library == "OfficeIMO");
        Assert.Equal(["LazyValues"], officeImo.Cases);
    }

    private static BenchmarkRunResult BenchmarkDotNetStyleResult()
    {
        const string job = "Job-ABC(IterationCount=15, LaunchCount=1, WarmupCount=5)";
        var samples = new List<BenchmarkSample>();
        foreach ((string library, double start) in new[] { ("OfficeIMO", 1.2345678901), ("Sep", 2.3456789012) })
        {
            var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Rows"] = "100",
                ["Namespace"] = "Benchmarks",
                ["FullName"] = library + ".Read(Rows: 100)",
                ["Method"] = library
            };
            for (int iteration = 0; iteration < 15; iteration++)
            {
                samples.Add(new BenchmarkSample
                {
                    RunId = "import",
                    Suite = "csv",
                    Scenario = "Default",
                    Operation = "Run",
                    Engine = job,
                    Library = library,
                    Os = "Windows",
                    RunMode = "standard",
                    Iteration = iteration,
                    Status = BenchmarkSampleStatus.Succeeded,
                    DurationMs = start + iteration * 0.000123456789,
                    Variables = new Dictionary<string, string?>(variables, StringComparer.OrdinalIgnoreCase),
                    Metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["MedianMs"] = start + 0.0008641975,
                        ["Median"] = start + 0.0008641975,
                        ["P50"] = start + 0.0008641975,
                        ["Gen0"] = 1105,
                        ["Gen0Collections"] = 1105,
                        ["BytesAllocatedPerOperation"] = 512,
                        ["Allocated"] = 512
                    }
                });
            }
        }

        BenchmarkSummaryRow[] summary = new BenchmarkSummaryService().Summarize(samples);
        return new BenchmarkRunResult
        {
            RunId = "import",
            Suite = "csv",
            StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-6),
            FinishedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Samples = samples.ToArray(),
            Summary = summary,
            Environment = new BenchmarkEnvironmentInfo { OsFamily = "Windows", RuntimeVersion = ".NET 10.0.1" },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["gitSha"] = "0123456789abcdef0123456789abcdef01234567" }
        };
    }

    private static int Occurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
