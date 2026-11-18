using System.Text.Json;


namespace PowerForge.Tests;

public class BenchmarkComparisonViewTests
{
    [Fact]
    public void Importer_KeepsBenchmarkDotNetJobSettingsAndStatisticsOutOfCaseVariables()
    {
        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "MarkdownParse-report.csv"),
                "Method,Job,Runtime,IterationCount,CorpusName,Mean [ms],Error [ms],Allocated [KB],Alloc Ratio\n" +
                "Markdig,ShortRun,.NET 10.0,1,RichAst,0.881,NA,145.49,0.14\n" +
                "OfficeIMO,ShortRun,.NET 10.0,1,RichAst,2.850,NA,1024.00,1.00\n");

            var result = new BenchmarkResultImporter().Import(root);

            Assert.Equal(2, result.Summary.Length);
            foreach (var row in result.Summary)
            {
                Assert.Equal("RichAst", row.Variables["CorpusName"]);
                Assert.DoesNotContain("Runtime", row.Variables.Keys);
                Assert.DoesNotContain("IterationCount", row.Variables.Keys);
                Assert.DoesNotContain("Error", row.Variables.Keys);
                Assert.DoesNotContain("Alloc Ratio", row.Variables.Keys);
            }
            Assert.Equal(".NET 10.0", result.Metadata["benchmarkdotnet.job.Runtime"]);
            Assert.Equal("1", result.Metadata["benchmarkdotnet.job.IterationCount"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("Scale=Easy&Producer=IText", "Bench.Merge(Scale: Easy, Producer: IText)", "Easy", "IText")]
    [InlineData("Scale=Easy&Producer=a&b", "Bench.Merge(Scale: Easy, Producer: \"a&b\")", "Easy", "a&b")]
    public void Importer_RecoversParametersFromFullNameWhenDisplayInfoDoesNotMatch(string parameters, string fullName, string scale, string producer)
    {
        var benchmark = new
        {
            DisplayInfo = "Bench.Merge: Job-Unmatched [unrelated]",
            Method = "Merge",
            Parameters = parameters,
            FullName = fullName,
            Statistics = new { Mean = 1_000_000 }
        };
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "Merge-report-full.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { Benchmarks = new[] { benchmark } }));

            var sample = Assert.Single(new BenchmarkResultImporter().Import(path).Samples);

            Assert.Equal(scale, sample.Variables["Scale"]);
            Assert.Equal(producer, sample.Variables["Producer"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_PutsEveryContenderOfEachCaseSideBySide()
    {
        var view = BuildSingle(
            libraries: new[] { "OfficeIMO", "Markdig" },
            rows: new[]
            {
                Row("Markdig", 1.0, ("CorpusName", "RichAst")),
                Row("OfficeIMO-Semantic", 3.0, ("CorpusName", "RichAst")),
                Row("Markdig", 2.0, ("CorpusName", "LargeTable")),
                Row("OfficeIMO-Semantic", 1.0, ("CorpusName", "LargeTable"))
            });

        var run = Assert.Single(view.Runs);
        Assert.Equal(new[] { "OfficeIMO Semantic", "Markdig" }, run.Contenders.Select(c => c.Name));
        Assert.True(run.Contenders[0].IsSubject);
        Assert.Equal(new[] { "RichAst", "LargeTable" }, run.Cases.Select(c => c.Label));
        Assert.All(run.Cases, c => Assert.Equal(2, c.Cells.Length));
        Assert.True(run.Cases[0].Cells[1].Relative.IsFastest);
        Assert.Equal("3.00 ms", run.Cases[0].Cells[0].TimeText);
        Assert.Equal("3.0×", run.Cases[0].Cells[0].Relative.Text);
        Assert.Equal(1, run.Contenders[0].Wins);

        var versus = Assert.Single(run.HeadToHead);
        Assert.Equal("behind", versus.Outcome);
        Assert.Equal("Markdig is 1.22× faster than OfficeIMO Semantic", versus.Text);
        Assert.Equal("geometric mean of 2 cases", versus.Detail);
    }

    [Fact]
    public void Build_ReadsCaseParametersFromFullNameForResultsImportedEarlier()
    {
        var view = BuildSingle(
            libraries: new[] { "OfficeIMO", "iText" },
            rows: new[]
            {
                // The shape OfficeIMO published before this change: parameters joined in one value, job settings
                // and statistics mixed in, and the exact parameters only in FullName.
                Row("IText", 2.0, ("Scale", "Easy&Producer=IText"), ("FullName", "Bench.IText(Scale: Easy, Producer: IText)"), ("Runtime", ".NET 10.0")),
                Row("OfficeIMO", 1.0, ("Scale", "Easy&Producer=IText"), ("FullName", "Bench.OfficeIMO(Scale: Easy, Producer: IText)"), ("Runtime", ".NET 10.0")),
                Row("IText", 4.0, ("Scale", "Easy&Producer=QuestPDF"), ("FullName", "Bench.IText(Scale: Easy, Producer: QuestPDF)"), ("Runtime", ".NET 10.0")),
                Row("OfficeIMO", 2.0, ("Scale", "Easy&Producer=QuestPDF"), ("FullName", "Bench.OfficeIMO(Scale: Easy, Producer: QuestPDF)"), ("Runtime", ".NET 10.0"))
            });

        var run = Assert.Single(view.Runs);
        Assert.Equal(new[] { "IText", "QuestPDF" }, run.Cases.Select(c => c.Label));
        Assert.Equal("Scale", Assert.Single(run.Workload).Name);
        Assert.Equal(new[] { "OfficeIMO", "iText" }, run.Contenders.Select(c => c.Name));
        var versus = Assert.Single(run.HeadToHead);
        Assert.Equal("ahead", versus.Outcome);
        Assert.Equal("OfficeIMO is 2.0× faster than iText", versus.Text);
    }

    [Fact]
    public void Build_DropsJobSettingsAndStatisticsFromEarlierCsvImports()
    {
        var view = BuildSingle(
            libraries: new[] { "OfficeIMO", "Markdig" },
            rows: new[]
            {
                Row("Markdig", 1.0, ("CorpusName", "RichAst"), ("BenchmarkDotNetReport", "Parse"), ("Runtime", ".NET 10.0"), ("Error", "NA"), ("Alloc Ratio", "0.14")),
                Row("OfficeIMO-Semantic", 2.0, ("CorpusName", "RichAst"), ("BenchmarkDotNetReport", "Parse"), ("Runtime", ".NET 10.0"), ("Error", "NA"), ("Alloc Ratio", "1.00"))
            });

        var run = Assert.Single(view.Runs);
        var workload = Assert.Single(run.Workload, parameter => parameter.Name != "BenchmarkDotNetReport");
        Assert.Equal("CorpusName", workload.Name);
        var single = Assert.Single(run.Cases);
        Assert.Equal(2, single.Cells.Length);
    }

    [Fact]
    public void Build_TreatsASuffixSharedByLibrariesAsAnOperation()
    {
        var view = BuildSingle(
            libraries: new[] { "OfficeIMO", "Magick.NET" },
            rows: new[]
            {
                Row("OfficeIMOEncode", 2.0),
                Row("MagickNETEncode", 1.0),
                Row("OfficeIMODecode", 1.0),
                Row("MagickNETDecode", 1.0),
                Row("OfficeIMOEncodeParallel", 0.5)
            });

        var run = Assert.Single(view.Runs);
        Assert.Equal(new[] { "Encode", "Decode" }, run.Cases.Select(c => c.Label));
        Assert.Contains(run.Contenders, c => c.Name == "Magick.NET");
        Assert.Contains(run.Contenders, c => c.Name == "OfficeIMO Parallel");
        Assert.Equal("500 µs", run.Cases[0].Cells.Single(c => c.Contender == "OfficeIMO Parallel").TimeText);
    }

    [Fact]
    public void Build_PrefersFullPublishedRunsAndSaysWhenNoEnvironmentWasRecorded()
    {
        var root = CreateTempRoot();
        try
        {
            var catalogPath = WriteCatalog(root, new[] { "OfficeIMO", "Sylvan" },
                ("windows", "quick", true, Result(Row("OfficeIMO", 1.0), Row("Sylvan", 2.0))),
                ("linux", "full", false, Result(Row("OfficeIMO", 3.0), Row("Sylvan", 1.0))));

            var comparison = Assert.Single(new BenchmarkComparisonViewBuilder().Build(catalogPath).Comparisons);

            Assert.Equal("linux-full", comparison.PreferredRunId);
            Assert.Equal(new[] { "windows-diagnostic", "linux-full" }, comparison.Runs.Select(r => r.Id));
            Assert.Equal("Environment not recorded", comparison.Runs[0].Environment.Text);
            var subject = comparison.Overview.First();
            Assert.Equal("OfficeIMO", subject.Name);
            Assert.Equal(new[] { "1.00×", "3.0×" }, subject.Runs.Select(r => r.Text));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Importer_KeepsJobSettingsThatDifferBetweenRowsAsVariables()
    {
        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "Parse-report.csv"),
                "Method,Job,Runtime,IterationCount,Mean [ms]\n" +
                "OfficeIMO,Net8,.NET 8.0,1,2.0\n" +
                "OfficeIMO,Net10,.NET 10.0,1,1.0\n");

            var result = new BenchmarkResultImporter().Import(root);

            Assert.Equal(2, result.Summary.Length);
            Assert.Equal(new[] { ".NET 10.0", ".NET 8.0" }, result.Summary.Select(row => row.Variables["Runtime"]).OrderBy(v => v, StringComparer.Ordinal));
            Assert.All(result.Summary, row => Assert.DoesNotContain("IterationCount", row.Variables.Keys));
            Assert.False(result.Metadata.ContainsKey("benchmarkdotnet.job.Runtime"));
            Assert.Equal("1", result.Metadata["benchmarkdotnet.job.IterationCount"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_KeepsCasesApartWhenAParameterIsMissingOrValuesLookAlike()
    {
        var view = BuildSingle(
            libraries: new[] { "OfficeIMO", "Sylvan" },
            rows: new[]
            {
                Row("OfficeIMO", 1.0, ("Mode", "A")),
                Row("OfficeIMO", 2.0),
                Row("Sylvan", 3.0, ("A", "x · B y"), ("B", "z")),
                Row("Sylvan", 4.0, ("A", "x"), ("B", "y · B z"))
            });

        var run = Assert.Single(view.Runs);
        Assert.Equal(4, run.Cases.Length);
        Assert.Empty(run.Workload);
        Assert.Equal(new double?[] { 1.0, 2.0, 3.0, 4.0 },
            run.Cases.SelectMany(c => c.Cells).Where(c => !c.Missing).Select(c => c.TimeMs).OrderBy(t => t));
    }

    [Fact]
    public void Build_KeepsRecordedOperationsApart()
    {
        BenchmarkSummaryRow WithOperation(string scenario, string operation, double ms)
        {
            var row = Row(scenario, ms);
            row.Operation = operation;
            return row;
        }

        var view = BuildSingle(
            libraries: new[] { "OfficeIMO", "Sylvan" },
            rows: new[]
            {
                WithOperation("OfficeIMO", "Read", 1.0),
                WithOperation("OfficeIMO", "Write", 2.0),
                WithOperation("Sylvan", "Read", 1.5),
                WithOperation("Sylvan", "Write", 1.0)
            });

        var run = Assert.Single(view.Runs);
        Assert.Equal(new[] { "Read", "Write" }, run.Cases.Select(c => c.Label));
        Assert.All(run.Cases, c => Assert.DoesNotContain(c.Cells, cell => cell.Missing));
    }

    [Fact]
    public void Build_IdentifiesTheSubjectByItsMatchedLibrary()
    {
        var view = BuildSingle(
            libraries: new[] { "Foo", "Foo.Bar" },
            rows: new[] { Row("Foo", 2.0), Row("FooBar", 1.0) });

        var run = Assert.Single(view.Runs);
        Assert.Equal(new[] { true, false }, run.Contenders.Select(c => c.IsSubject));
        Assert.Equal("Foo.Bar is 2.0× faster than Foo", Assert.Single(run.HeadToHead).Text);
    }

    [Fact]
    public void Build_KeepsMethodsApartWhenRowsRecordTheirLibrary()
    {
        static BenchmarkSummaryRow Recorded(string scenario, string library, double medianMs)
        {
            BenchmarkSummaryRow row = Row(scenario, medianMs);
            row.Library = library;
            return row;
        }

        var view = BuildSingle(
            libraries: new[] { "OfficeIMO", "Sep" },
            rows: new[] { Recorded("OfficeIMOEncode", "OfficeIMO", 1.0), Recorded("OfficeIMODecode", "OfficeIMO", 2.0), Recorded("SepEncode", "Sep", 3.0), Recorded("SepDecode", "Sep", 4.0) });

        var run = Assert.Single(view.Runs);
        Assert.Equal(new[] { "OfficeIMO", "Sep" }, run.Contenders.Select(c => c.Name).OrderBy(name => name));
        Assert.Equal(2, run.Cases.Length);
        Assert.All(run.Cases, c => Assert.DoesNotContain(c.Cells, cell => cell.Missing));
    }

    [Fact]
    public void Build_UsesTheStatisticTheRunnerRecorded()
    {
        static BenchmarkSummaryRow Both(string library, double median, double mean)
        {
            BenchmarkSummaryRow row = Row("Read", median);
            row.Library = library;
            row.MeanMs = mean;
            return row;
        }

        var root = CreateTempRoot();
        try
        {
            BenchmarkRunResult result = Result(Both("OfficeIMO", 1.0, 4.0), Both("Sep", 2.0, 3.0));
            result.Runner = new BenchmarkRunnerInfo { Kind = "rotated", Statistic = "mean" };
            var view = Assert.Single(new BenchmarkComparisonViewBuilder().Build(WriteCatalog(root, new[] { "OfficeIMO", "Sep" }, ("windows", "standard", true, result))).Comparisons);

            var run = Assert.Single(view.Runs);
            Assert.Equal("mean", run.Statistic);
            Assert.Equal("Sep is 1.33× faster than OfficeIMO", Assert.Single(run.HeadToHead).Text);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_KeepsRecordedCasesApartWhenAnOperationIsRecorded()
    {
        static BenchmarkSummaryRow Recorded(string scenario, string library, double medianMs, params (string Name, string Value)[] variables)
        {
            BenchmarkSummaryRow row = Row(scenario, medianMs, variables);
            row.Library = library;
            row.Operation = "Read";
            return row;
        }

        var view = BuildSingle(
            libraries: new[] { "A", "B" },
            rows: new[]
            {
                Recorded("small", "A", 1.0, ("Method", "ASmall")), Recorded("large", "A", 2.0, ("Method", "ALarge")),
                Recorded("small", "B", 3.0, ("Method", "BSmall")), Recorded("large", "B", 4.0, ("Method", "BLarge"))
            });

        var run = Assert.Single(view.Runs);
        Assert.Equal(2, run.Cases.Length);
        Assert.Equal(new[] { "A", "B" }, run.Contenders.Select(c => c.Name).OrderBy(name => name));
        Assert.All(run.Cases, c => Assert.DoesNotContain(c.Cells, cell => cell.Missing));
    }

    [Fact]
    public void Build_KeepsAWorkloadParameterNamedCase()
    {
        static BenchmarkSummaryRow Recorded(string library, string size, double medianMs)
        {
            BenchmarkSummaryRow row = Row("Read", medianMs, ("Case", size));
            row.Library = library;
            return row;
        }

        var view = BuildSingle(
            libraries: new[] { "A", "B" },
            rows: new[] { Recorded("A", "Small", 1.0), Recorded("A", "Large", 2.0), Recorded("B", "Small", 3.0), Recorded("B", "Large", 4.0) });

        var run = Assert.Single(view.Runs);
        Assert.Equal(2, run.Cases.Length);
        Assert.All(run.Cases, c => Assert.DoesNotContain(c.Cells, cell => cell.Missing));
    }

    [Fact]
    public void Build_KeepsASharedCaseNameThatStartsWithALibraryName()
    {
        static BenchmarkSummaryRow Recorded(string library, double medianMs)
        {
            BenchmarkSummaryRow row = Row("SepRead", medianMs);
            row.Library = library;
            return row;
        }

        var view = BuildSingle(libraries: new[] { "OfficeIMO", "Sep" }, rows: new[] { Recorded("OfficeIMO", 1.0), Recorded("Sep", 2.0) });

        var run = Assert.Single(view.Runs);
        var shared = Assert.Single(run.Cases);
        Assert.DoesNotContain(shared.Cells, cell => cell.Missing);
        Assert.Equal("OfficeIMO is 2.0× faster than Sep", Assert.Single(run.HeadToHead).Text);
    }

    private static BenchmarkComparisonView BuildSingle(string[] libraries, BenchmarkSummaryRow[] rows)
    {
        var root = CreateTempRoot();
        try
        {
            var catalogPath = WriteCatalog(root, libraries, ("windows", "full", true, Result(rows)));
            return Assert.Single(new BenchmarkComparisonViewBuilder().Build(catalogPath).Comparisons);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string WriteCatalog(string root, string[] libraries, params (string Platform, string Mode, bool Publish, BenchmarkRunResult Result)[] runs)
    {
        var entries = runs.Select((run, index) =>
        {
            var file = $"compare-{run.Platform}-{run.Mode}.json";
            BenchmarkJson.Write(Path.Combine(root, file), run.Result);
            return new BenchmarkEvidenceEntry
            {
                ComparisonId = "compare",
                Area = "test",
                Platform = run.Platform,
                Profile = BenchmarkEvidenceCatalogService.ParseProfile(run.Mode == "quick" ? "diagnostic" : run.Mode),
                Publish = run.Publish,
                GeneratedUtc = new DateTimeOffset(2026, 11, 1 + index, 0, 0, 0, TimeSpan.Zero),
                ResultPath = "/data/" + file,
                ArtifactRelativePath = file
            };
        }).ToArray();
        var catalog = new BenchmarkEvidenceCatalog
        {
            Areas = new[] { "test" },
            Comparisons = new[] { new BenchmarkComparisonDefinition { ComparisonId = "compare", Area = "test", Title = "Compare", Libraries = libraries } },
            Entries = entries
        };
        var path = Path.Combine(root, "index.json");
        BenchmarkJson.Write(path, catalog);
        return path;
    }

    private static BenchmarkRunResult Result(params BenchmarkSummaryRow[] rows) => new() { Suite = "test", Summary = rows };

    private static BenchmarkSummaryRow Row(string scenario, double medianMs, params (string Name, string Value)[] variables) => new()
    {
        Scenario = scenario,
        Status = "Succeeded",
        MedianMs = medianMs,
        Variables = variables.ToDictionary(v => v.Name, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase)
    };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bench-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
