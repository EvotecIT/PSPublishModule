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

    [Fact]
    public void CompactResult_ReadsBackThroughEveryPublicReader()
    {
        string root = CreateTempRoot();
        BenchmarkRunResult measured = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        measured.Comparison = new BenchmarkSummaryService().Compare(measured.Summary, "Sep");
        BenchmarkEvidenceCatalog catalog = new BenchmarkEvidenceCatalogService().UpdateFile(
            Path.Combine(root, "index.json"), PublishRequest(measured, TestDeclarations()));
        string path = Path.Combine(root, Assert.Single(catalog.Entries).ArtifactRelativePath);
        Assert.True(BenchmarkEvidenceResultCodec.IsCompact(File.ReadAllBytes(path)));
        BenchmarkRunResult expected = BenchmarkEvidenceResultCodec.Read(path);
        Assert.Equal(20, expected.Samples.Length);
        Assert.NotEmpty(expected.Comparison);

        Assert.Equal(BenchmarkJson.Serialize(expected), BenchmarkJson.Serialize(BenchmarkJson.Read<BenchmarkRunResult>(path)));
        Assert.Equal(BenchmarkJson.Serialize(expected.Summary), BenchmarkJson.Serialize(BenchmarkJson.ReadSummary(path)));
        Assert.Equal(BenchmarkJson.Serialize(expected.Comparison), BenchmarkJson.Serialize(BenchmarkJson.ReadComparison(path)));
        BenchmarkRunResult imported = new BenchmarkResultImporter().Import(path);
        Assert.Equal(BenchmarkJson.Serialize(expected.Samples), BenchmarkJson.Serialize(imported.Samples));
        Assert.Equal(BenchmarkJson.Serialize(expected.Summary), BenchmarkJson.Serialize(imported.Summary));
    }

    [Fact]
    public void CompactResult_KeepsAComparedAliasOnlyOnTheSamplesThatRecordedIt()
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        foreach (BenchmarkSample sample in result.Samples)
        {
            sample.Metrics["BytesAllocatedPerOperation"] = sample.Library == "Sep" ? 100 : 300;
            if (sample.Library == "Sep")
                sample.Metrics["Allocated"] = 100;
        }
        // An unsampled row is stored as it is, so it must keep the compared alias as well.
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples).Append(new BenchmarkSummaryRow
        {
            Suite = "csv", Scenario = "Read", Operation = "Run", Engine = "EPPlus", Library = "EPPlus",
            Host = "dotnet", Os = "Windows", RunMode = "standard", Status = "Failed", FailureCount = 1, MedianMs = 15,
            Metrics = new Dictionary<string, double> { ["BytesAllocatedPerOperation"] = 50, ["Allocated"] = 50 }
        }).ToArray();
        result.Comparison = new BenchmarkSummaryService().Compare(result.Summary, "Sep", "Allocated");
        Assert.Equal(0.5, Assert.Single(result.Comparison, row => row.Engine == "EPPlus").Ratio);

        BenchmarkRunResult read = BenchmarkEvidenceResultCodec.Normalize(result);

        Assert.All(read.Samples.Where(sample => sample.Library == "Sep"), sample => Assert.Equal(100d, sample.Metrics["Allocated"]));
        Assert.All(read.Samples.Where(sample => sample.Library == "OfficeIMO"), sample => Assert.False(sample.Metrics.ContainsKey("Allocated")));
        Assert.Equal(BenchmarkJson.Serialize(result.Comparison), BenchmarkJson.Serialize(read.Comparison));
        Assert.Equal(
            Encoding.UTF8.GetString(BenchmarkEvidenceResultCodec.Encode(result)),
            Encoding.UTF8.GetString(BenchmarkEvidenceResultCodec.Encode(read)));
    }

    [Fact]
    public void Import_WithSuiteOverrideKeepsUnsampledSummaryRows()
    {
        string root = CreateTempRoot();
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)));
        result.Summary = result.Summary.Append(new BenchmarkSummaryRow
        {
            Suite = "csv", Scenario = "Write", Operation = "Run", Engine = "EPPlus", Library = "EPPlus",
            Status = "Failed", FailureCount = 1, FailureReasons = new Dictionary<string, int> { ["Out of memory."] = 1 }
        }).ToArray();
        string path = Path.Combine(root, "result.json");
        File.WriteAllBytes(path, BenchmarkEvidenceResultCodec.Encode(result));

        BenchmarkRunResult imported = new BenchmarkResultImporter().Import(path, "renamed");

        Assert.Equal(2, imported.Summary.Length);
        Assert.All(imported.Summary, row => Assert.Equal("renamed", row.Suite));
        Assert.Equal("Failed", Assert.Single(imported.Summary, row => row.Library == "EPPlus").Status);
    }

    [Fact]
    public void CompactResult_ReadsAFileWithAByteOrderMark()
    {
        string root = CreateTempRoot();
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)));
        string compact = Path.Combine(root, "compact.json");
        string legacy = Path.Combine(root, "legacy.json");
        byte[] bom = [0xEF, 0xBB, 0xBF];
        File.WriteAllBytes(compact, bom.Concat(BenchmarkEvidenceResultCodec.Encode(result)).ToArray());
        File.WriteAllBytes(legacy, bom.Concat(BenchmarkJson.SerializeCanonicalBytes(result)).ToArray());

        Assert.Equal(10, BenchmarkEvidenceResultCodec.Read(compact).Samples.Length);
        Assert.Equal(10, BenchmarkEvidenceResultCodec.Read(legacy).Samples.Length);
    }

    [Fact]
    public void CompactResult_KeepsAliasesWhoseRoundedValuesDifferAndIsStable()
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)));
        foreach (BenchmarkSample sample in result.Samples)
        {
            sample.Metrics["MeanMs"] = 1.234561;
            sample.Metrics["Mean"] = 1.234562;
            sample.Metrics["MaxMs"] = 2.5;
            sample.Metrics["Max"] = 2.6;
        }

        byte[] first = BenchmarkEvidenceResultCodec.Encode(result);
        byte[] second = BenchmarkEvidenceResultCodec.Encode(BenchmarkEvidenceResultCodec.Decode(first, "first"));

        Assert.Equal(Encoding.UTF8.GetString(first), Encoding.UTF8.GetString(second));
        Assert.DoesNotContain("\"Mean\":", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
        Assert.Contains("\"Max\":2.6", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void CompactResult_KeepsNullAndEmptyVariableValuesAsSeparateCases()
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("OfficeIMO", "Read", Spread(20, 10)));
        foreach ((BenchmarkSample sample, int index) in result.Samples.Select((sample, index) => (sample, index)))
            sample.Variables["Mode"] = index < 10 ? null : string.Empty;

        BenchmarkRunResult read = BenchmarkEvidenceResultCodec.Normalize(result);

        Assert.Equal(10, read.Samples.Count(sample => sample.Variables["Mode"] is null));
        Assert.Equal(10, read.Samples.Count(sample => sample.Variables["Mode"] == string.Empty));
        Assert.Equal(2, read.Summary.Length);
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
