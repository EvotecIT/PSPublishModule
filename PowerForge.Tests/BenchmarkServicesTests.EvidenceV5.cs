using System.Management.Automation;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void EvidencePolicy_RecordsCrashedLibraryAsFailedCellAndPublishesTheRest()
    {
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)),
            ("Sep", "Read", Spread(20, 10)),
            ("EPPlus", "Read", null));
        // EPPlus is also the comparison baseline, so the other rows carry no baseline value or ratio.
        result.Comparison = new BenchmarkSummaryService().Compare(result.Summary, "EPPlus");

        BenchmarkEvidenceCatalog catalog = new BenchmarkEvidenceCatalogService().Update(null, PublishRequest(result, ThreeLibraryDeclarations()));

        BenchmarkEvidenceEntry entry = Assert.Single(catalog.Entries);
        Assert.True(entry.Publish);
        Assert.Equal("partial", entry.Completeness);
        BenchmarkEvidenceCell crashed = Assert.Single(entry.Cells, cell => cell.Library == "EPPlus");
        Assert.Equal("Failed", crashed.Status);
        Assert.Contains("exit code 255", crashed.Reason, StringComparison.Ordinal);
        Assert.False(crashed.Publishable);
        Assert.All(entry.Cells.Where(cell => cell.Library != "EPPlus"), cell => Assert.True(cell.Publishable));
    }

    [Fact]
    public void EvidencePolicy_TreatsOverlappingConfidenceIntervalsAsEven()
    {
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)),
            ("Sep", "Read", Spread(10.5, 10)),
            ("EPPlus", "Read", Spread(30, 10)));

        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService()
            .Update(null, PublishRequest(result, ThreeLibraryDeclarations())).Entries);

        Assert.Equal("complete", entry.Completeness);
        Assert.Equal("fastest", Assert.Single(entry.Cells, cell => cell.Library == "OfficeIMO").Outcome);
        Assert.Equal("even", Assert.Single(entry.Cells, cell => cell.Library == "Sep").Outcome);
        Assert.Equal("slower", Assert.Single(entry.Cells, cell => cell.Library == "EPPlus").Outcome);
        BenchmarkConfidenceInterval interval = Assert.Single(entry.Cells, cell => cell.Library == "OfficeIMO").ConfidenceInterval!;
        Assert.Equal(0.95, interval.Level);
        Assert.Equal(10.1, interval.LowerMs, 6);
        Assert.Equal(10.8, interval.UpperMs, 6);
    }

    [Theory]
    [InlineData("diagnostic-profile", "never published")]
    [InlineData("two-warmups", "at least 3 recorded warmups")]
    [InlineData("no-runner", "runner block")]
    [InlineData("full-from-rotated-runner", "full profile requires")]
    [InlineData("dirty-source", "gitWorktreeClean")]
    [InlineData("machine-name", "machine or user name")]
    [InlineData("undeclared-comparison", "is not declared")]
    [InlineData("unpinned-package", "pins 0.10.0")]
    [InlineData("weaker-policy", "weaker than the standard: minimum warmups 1 is below 3.")]
    [InlineData("conflicting-framework", "conflicts with the framework the result records ('net10.0'")]
    [InlineData("affinity-with-separator", "cannot contain '/' or '='")]
    [InlineData("no-library", "Some cells have no library identity")]
    public void EvidencePolicy_RejectsLanesThatDoNotMeetThePolicy(string mutation, string expected)
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        BenchmarkEvidenceUpdateRequest request = PublishRequest(result, TestDeclarations());
        switch (mutation)
        {
            case "diagnostic-profile": request.Profile = BenchmarkEvidenceProfile.Diagnostic; break;
            case "two-warmups": result.Runner!.Warmups = 2; break;
            case "no-runner": result.Runner = null; break;
            case "full-from-rotated-runner":
                request.Profile = BenchmarkEvidenceProfile.Full;
                foreach (BenchmarkSample sample in result.Samples) sample.RunMode = "full";
                foreach (BenchmarkSummaryRow row in result.Summary) row.RunMode = "full";
                break;
            case "dirty-source": result.Metadata["gitWorktreeClean"] = "false"; break;
            case "machine-name": result.Environment.MachineName = "BUILD-01"; break;
            case "undeclared-comparison": request.ComparisonId = "comparison-z"; break;
            case "unpinned-package": result.Metadata["benchmark.package.sep"] = "0.9.0"; break;
            case "weaker-policy": request.Policy = new BenchmarkPublishPolicy { MinimumWarmups = 1 }; break;
            case "conflicting-framework": request.Framework = "net8.0"; break;
            case "affinity-with-separator": result.Runner!.Affinity = "0xff/variant=fast"; break;
            case "no-library":
                foreach (BenchmarkSample sample in result.Samples.Where(sample => sample.Library == "Sep"))
                    sample.Library = string.Empty;
                result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);
                break;
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(null, request));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nine-samples", "9 measured samples")]
    [InlineData("validation-missing", "Output validation was not recorded")]
    [InlineData("validation-failed", "Output validation failed: row count 99")]
    [InlineData("invalid-interval", "confidence interval is invalid")]
    [InlineData("declared-statistic-missing", "does not report the runner's declared statistic (median)")]
    public void EvidencePolicy_ExcludesCellsThatDoNotMeetThePolicy(string mutation, string expected)
    {
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)),
            ("Sep", "Read", Spread(20, mutation == "nine-samples" ? 9 : 10)));
        switch (mutation)
        {
            case "validation-missing":
                result.Validations = result.Validations.Where(value => value.Library != "Sep").ToArray();
                break;
            case "validation-failed":
                result.Validations = result.Validations
                    .Select(value => value.Library == "Sep" ? new BenchmarkOutputValidation { Library = "Sep", Status = BenchmarkValidationStatus.Failed, Detail = "row count 99" } : value)
                    .ToArray();
                break;
            case "declared-statistic-missing":
                // A summary-only producer that declares median but supplies only a mean for Sep.
                result.Samples = [];
                foreach (BenchmarkSummaryRow row in result.Summary)
                {
                    double value = row.Library == "Sep" ? row.MeanMs!.Value : row.MedianMs!.Value;
                    row.ConfidenceInterval = new BenchmarkConfidenceInterval { Level = 0.95, LowerMs = value - 0.2, UpperMs = value + 0.2 };
                    if (row.Library == "Sep")
                        row.MedianMs = null;
                }
                break;
            case "invalid-interval":
                // A summary-only producer supplies its own intervals; with raw samples PowerForge computes them.
                result.Runner!.Statistic = "mean";
                result.Samples = [];
                foreach (BenchmarkSummaryRow row in result.Summary)
                    row.ConfidenceInterval = row.Library == "Sep"
                        ? new BenchmarkConfidenceInterval { Level = 0.95 }
                        : new BenchmarkConfidenceInterval { Level = 0.95, LowerMs = row.MeanMs!.Value - 0.2, UpperMs = row.MeanMs.Value + 0.2 };
                break;
        }
        BenchmarkEvidenceUpdateRequest request = PublishRequest(result, TestDeclarations());

        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService().Update(null, request).Entries);

        Assert.True(Assert.Single(entry.Cells, cell => cell.Library == "OfficeIMO").Publishable);
        BenchmarkEvidenceCell excluded = Assert.Single(entry.Cells, cell => cell.Library != "OfficeIMO");
        Assert.False(excluded.Publishable);
        Assert.Contains(excluded.Issues, issue => issue.Contains(expected, StringComparison.Ordinal));
        Assert.Equal("partial", entry.Completeness);
    }

    [Fact]
    public void EvidencePolicy_DropsRestrictedLibraryCellsWithoutBlockingTheComparison()
    {
        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        declarations.Libraries.Single(value => value.Id == "EPPlus").Restriction = BenchmarkLibraryRestriction.AcceptanceRequired;
        declarations.Libraries.Single(value => value.Id == "EPPlus").Reason = "PolyForm Noncommercial; needs a licence context.";
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)),
            ("Sep", "Read", Spread(20, 10)),
            ("EPPlus", "Read", Spread(30, 10)));
        result.AcceptedLibraries = ["EPPlus"];

        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService().Update(null, PublishRequest(result, declarations)).Entries);
        BenchmarkEvidenceCell epplus = Assert.Single(entry.Cells, cell => cell.Library == "EPPlus");
        Assert.False(epplus.Publishable);
        Assert.Contains(epplus.Issues, issue => issue.Contains("acceptance-required", StringComparison.Ordinal));
        Assert.Equal("complete", entry.Completeness);

        // A publish-restricted library never blocks the comparison: its cells are dropped and the rest is published.
        declarations.Libraries.Single(value => value.Id == "Sep").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "Sep").Reason = "Publishing results needs the licensor's permission.";
        BenchmarkEvidenceCatalog restricted = new BenchmarkEvidenceCatalogService().Update(null, PublishRequest(MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10))), declarations));
        BenchmarkEvidenceEntry lane = Assert.Single(restricted.Entries);
        Assert.True(lane.Publish);
        Assert.True(Assert.Single(restricted.Eligibility, value => value.ComparisonId == "comparison-a").Publishable);
        Assert.True(Assert.Single(lane.Cells, cell => cell.Library == "OfficeIMO").Publishable);
        BenchmarkEvidenceCell sep = Assert.Single(lane.Cells, cell => cell.Library == "Sep");
        Assert.False(sep.Publishable);
        Assert.Null(sep.ValueMs);
        Assert.Null(sep.ConfidenceInterval);
        Assert.Contains(sep.Issues, issue => issue.StartsWith("Not published (licence): library 'Sep' is publish-restricted.", StringComparison.Ordinal));
    }

    [Fact]
    public void EvidencePolicy_RejectsDeclarationDrift()
    {
        BenchmarkDeclarationDocument declarations = TestDeclarations();
        declarations.Comparisons[0].Cases = ["Read"];
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)),
            ("Sep", "Read", Spread(20, 10)),
            ("OfficeIMO", "OnePdfLayout", Spread(12, 10)));

        BenchmarkDeclarationIssue drift = Assert.Single(new BenchmarkDeclarationService()
            .CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(null, PublishRequest(result, declarations)));

        Assert.Equal("unexpected-case", drift.Code);
        Assert.Contains("Declaration drift: Case 'OnePdfLayout' ran but is not declared.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Declarations_ReportMissingAxisValuesButNotCrashedCases()
    {
        BenchmarkDeclarationDocument declarations = TestDeclarations();
        BenchmarkComparisonDefinition comparison = declarations.Comparisons[0];
        comparison.Cases = ["Read"];
        comparison.Axes = [new BenchmarkComparisonAxis { Name = "Scale", Values = ["Easy", "Medium", "High"] }];
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", null));
        foreach (BenchmarkSample sample in result.Samples)
            sample.Variables["Scale"] = "Easy";
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);

        string[] findings = new BenchmarkDeclarationService().CheckDrift(comparison, declarations.Libraries, result)
            .Select(issue => issue.Code + ":" + issue.Message).ToArray();

        // Sep crashed in the declared case: that is a failed cell, not a missing case.
        Assert.Equal(
            new[] { "missing-axis-value:Declared axis 'Scale' value 'High' did not run.", "missing-axis-value:Declared axis 'Scale' value 'Medium' did not run." },
            findings);
    }

    [Fact]
    public void EvidenceCatalog_RevalidatesRecordedLanesAgainstReplacedRules()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkEvidenceCatalog catalog = service.Update(null, PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10))), TestDeclarations()));
        BenchmarkRunResult linux = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)));
        linux.Environment.OsFamily = "Linux";
        foreach (BenchmarkSample sample in linux.Samples) sample.Os = "Linux";
        foreach (BenchmarkSummaryRow row in linux.Summary) row.Os = "Linux";
        BenchmarkEvidenceUpdateRequest stricter = PublishRequest(linux, TestDeclarations());
        stricter.Publish = false;
        stricter.Profile = BenchmarkEvidenceProfile.Diagnostic;
        stricter.ResultPath = "linux.json";
        stricter.Policy = new BenchmarkPublishPolicy { MinimumMeasuredSamples = 20 };
        string before = BenchmarkJson.Serialize(catalog);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.Update(catalog, stricter));

        Assert.Equal(before, BenchmarkJson.Serialize(catalog));

        Assert.Contains("comparison-a/windows/net10.0/standard", exception.Message, StringComparison.Ordinal);
        Assert.Contains("at least 20 are required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceCatalog_UpdateFileKeepsSupersededArtifactsByContentAddress()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult first = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        first.StartedUtc = first.StartedUtc.AddMinutes(-10);
        first.FinishedUtc = first.FinishedUtc.AddMinutes(-10);
        BenchmarkRunResult second = MeasuredResult(("OfficeIMO", "Read", Spread(9, 10)), ("Sep", "Read", Spread(20, 10)));

        string artifact = Path.Combine(root, "data", "windows.json");
        BenchmarkEvidenceUpdateRequest firstRequest = PublishRequest(first, TestDeclarations());
        firstRequest.ResultPath = "/data/windows.json";
        firstRequest.ResultArtifactPath = artifact;
        service.UpdateFile(catalogPath, firstRequest);
        BenchmarkEvidenceUpdateRequest secondRequest = PublishRequest(second, TestDeclarations());
        secondRequest.ResultPath = "/data/windows.json";
        secondRequest.ResultArtifactPath = artifact;
        BenchmarkEvidenceCatalog catalog = service.UpdateFile(catalogPath, secondRequest);

        BenchmarkEvidenceEntry entry = Assert.Single(catalog.Entries);
        BenchmarkSupersededLane superseded = Assert.Single(catalog.Superseded);
        Assert.Equal($"/data/windows.{entry.ResultSha256}.json", entry.ResultPath);
        Assert.Equal($"/data/windows.{superseded.ResultSha256}.json", superseded.ResultPath);
        Assert.Equal(entry.ResultSha256, BenchmarkJson.ComputeFileSha256(Path.Combine(root, "data", $"windows.{entry.ResultSha256}.json")));
        Assert.Equal(superseded.ResultSha256, BenchmarkJson.ComputeFileSha256(Path.Combine(root, "data", $"windows.{superseded.ResultSha256}.json")));

        // A late, older run does not replace the lane but is still written for its place in the history.
        BenchmarkRunResult late = MeasuredResult(("OfficeIMO", "Read", Spread(11, 10)), ("Sep", "Read", Spread(20, 10)));
        late.StartedUtc = first.StartedUtc.AddMinutes(-20);
        late.FinishedUtc = first.FinishedUtc.AddMinutes(-20);
        BenchmarkEvidenceUpdateRequest lateRequest = PublishRequest(late, TestDeclarations());
        lateRequest.ResultPath = "/data/windows.json";
        lateRequest.ResultArtifactPath = artifact;
        catalog = service.UpdateFile(catalogPath, lateRequest);
        Assert.Equal(entry.ResultSha256, Assert.Single(catalog.Entries).ResultSha256);
        BenchmarkSupersededLane lateHistory = Assert.Single(catalog.Superseded, value => value.GeneratedUtc == late.FinishedUtc);
        Assert.Equal(lateHistory.ResultSha256, BenchmarkJson.ComputeFileSha256(Path.Combine(root, "data", $"windows.{lateHistory.ResultSha256}.json")));

        // Both superseded files are found through their local paths and copied into a fresh merged bundle.
        string mergedRoot = Path.Combine(root, "published");
        BenchmarkEvidenceCatalog merged = service.MergeFiles(Path.Combine(mergedRoot, "index.json"), [catalogPath], null, null, "/data/benchmarks");
        Assert.Equal(2, merged.Superseded.Length);
        Assert.All(merged.Superseded, history =>
            Assert.Equal(history.ResultSha256, BenchmarkJson.ComputeFileSha256(Path.Combine(mergedRoot, history.ArtifactRelativePath))));
    }

    [Theory]
    [InlineData("/data/windows.json")]
    [InlineData("https://benchmarks.example/data/windows.json")]
    [InlineData("ABSOLUTE")]
    public void EvidenceCatalog_RequiresArtifactPathForRootedResultPaths(string resultPath)
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        if (resultPath == "ABSOLUTE")
            resultPath = Path.Combine(root, "elsewhere", "windows.json");
        BenchmarkEvidenceUpdateRequest request = PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10))), TestDeclarations());
        request.ResultPath = resultPath;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request));

        Assert.Contains("ResultArtifactPath", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(catalogPath));
        Assert.False(Directory.Exists(Path.Combine(root, "elsewhere")));

        request.ResultArtifactPath = Path.Combine(root, "site", "windows.json");
        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request).Entries);
        Assert.True(File.Exists(Path.Combine(root, "site", $"windows.{entry.ResultSha256}.json")));
    }

    [Fact]
    public void EvidenceCatalog_MergeRejectsLaneRelabelledToAnotherFramework()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkEvidenceUpdateRequest request = PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10))), TestDeclarations());
        new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request);
        BenchmarkEvidenceCatalog tampered = new BenchmarkEvidenceCatalogService().ReadCatalog(catalogPath);
        BenchmarkEvidenceEntry entry = Assert.Single(tampered.Entries);
        entry.Framework = "net8.0";
        entry.LaneKey = BenchmarkEvidenceIdentity.CreateLaneKey(entry.ComparisonId, entry.Platform, entry.Framework, entry.Profile, entry.Parameters);
        BenchmarkJson.Write(catalogPath, tampered);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [catalogPath]));

        Assert.Contains("claims framework 'net8.0' but its result records 'net10.0'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonView_ReportsOverlappingConfidenceIntervalsAsEven()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        // Medians differ by about 5%, but the accepted intervals overlap.
        new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(10.5, 10))), TestDeclarations()));

        BenchmarkRunView run = Assert.Single(Assert.Single(new BenchmarkComparisonViewBuilder().Build(catalogPath).Comparisons,
            comparison => comparison.Id == "comparison-a").Runs);

        BenchmarkHeadToHeadView headToHead = Assert.Single(run.HeadToHead);
        Assert.Equal("even", headToHead.Outcome);
        Assert.Equal("OfficeIMO and Sep are about as fast", headToHead.Text);
    }

    [Fact]
    public void EvidenceCatalog_RestrictingAPublishedLibraryRequiresAMergeThatRedactsHistoryToo()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkRunResult older = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        older.StartedUtc = older.StartedUtc.AddMinutes(-30);
        older.FinishedUtc = older.FinishedUtc.AddMinutes(-30);
        service.UpdateFile(catalogPath, PublishRequest(older, TestDeclarations()));
        service.UpdateFile(catalogPath, PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(9, 10)), ("Sep", "Read", Spread(20, 10))), TestDeclarations()));
        BenchmarkDeclarationDocument restricted = TestDeclarations();
        restricted.Libraries.Single(value => value.Id == "Sep").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        restricted.Libraries.Single(value => value.Id == "Sep").Reason = "Publishing results needs permission.";

        // An update cannot rewrite stored results, so it refuses to restrict a library a published lane measured.
        BenchmarkRunResult linux = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)));
        linux.Environment.OsFamily = "Linux";
        foreach (BenchmarkSample sample in linux.Samples) sample.Os = "Linux";
        foreach (BenchmarkSummaryRow row in linux.Summary) row.Os = "Linux";
        BenchmarkEvidenceUpdateRequest diagnostic = PublishRequest(linux, restricted);
        diagnostic.Publish = false;
        diagnostic.Profile = BenchmarkEvidenceProfile.Diagnostic;
        diagnostic.ResultPath = "linux.json";
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => service.UpdateFile(catalogPath, diagnostic));
        Assert.Contains("Merge-BenchmarkEvidenceCatalog", exception.Message, StringComparison.Ordinal);

        // Replacing the lane itself with a redacted newer run is refused too: the run it supersedes still measures Sep.
        BenchmarkEvidenceUpdateRequest replacement = PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(8, 10)), ("Sep", "Read", Spread(20, 10))), restricted);
        InvalidOperationException historyError = Assert.Throws<InvalidOperationException>(() => service.UpdateFile(catalogPath, replacement));
        Assert.Contains("Superseded run 'comparison-a/windows/net10.0/standard'", historyError.Message, StringComparison.Ordinal);
        Assert.Contains("Merge-BenchmarkEvidenceCatalog", historyError.Message, StringComparison.Ordinal);

        // The merge redacts the current run and the superseded run it carries.
        string mergedRoot = Path.Combine(root, "merged");
        BenchmarkEvidenceCatalog merged = service.MergeFiles(Path.Combine(mergedRoot, "index.json"), [catalogPath], null, restricted);
        BenchmarkSupersededLane historyRun = Assert.Single(merged.Superseded);
        Assert.DoesNotContain("Sep", historyRun.Libraries);
        BenchmarkSupersededLane history = historyRun;
        string historyPath = Path.Combine(mergedRoot, history.ResultPath.Substring(history.ResultPath.LastIndexOf('/') + 1));
        Assert.Equal(history.ResultSha256, BenchmarkJson.ComputeFileSha256(historyPath));
        Assert.DoesNotContain(BenchmarkJson.Read<BenchmarkRunResult>(historyPath).Summary, row => row.Library == "Sep");
        BenchmarkEvidenceEntry current = Assert.Single(merged.Entries);
        Assert.DoesNotContain(BenchmarkJson.Read<BenchmarkRunResult>(Path.Combine(mergedRoot, current.ArtifactRelativePath)).Summary, row => row.Library == "Sep");

        // Merging the output again together with the original bundle selects the same redacted runs.
        BenchmarkEvidenceCatalog remerged = service.MergeFiles(Path.Combine(mergedRoot, "index.json"), [Path.Combine(mergedRoot, "index.json"), catalogPath], null, restricted);
        Assert.Equal(current.ResultSha256, Assert.Single(remerged.Entries).ResultSha256);
        Assert.Single(remerged.Superseded);
    }

    [Fact]
    public void EvidenceCatalog_MergeCountsLicenceMarkersAsCoveringDeclaredCases()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        declarations.Comparisons[0].Cases = ["Read", "Write"];
        declarations.Libraries.Single(value => value.Id == "EPPlus").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "EPPlus").Reason = "Publishing results needs permission.";
        // Only the restricted library measured Write, so after redaction that case exists only as a licence marker.
        new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, PublishRequest(MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)), ("EPPlus", "Write", Spread(5, 10))), declarations));

        BenchmarkEvidenceCatalog merged = new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [catalogPath], null, declarations);

        BenchmarkEvidenceEntry entry = Assert.Single(merged.Entries);
        Assert.True(entry.Publish);
        Assert.Contains(entry.Cells, cell => cell.Case == "Write" && cell.Library == "EPPlus" && !cell.Publishable && cell.ValueMs is null);
    }

    [Fact]
    public void ComparisonView_NeverShowsRestrictedLibrariesInDiagnosticRuns()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        declarations.Libraries.Single(value => value.Id == "EPPlus").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "EPPlus").Reason = "Publishing results needs permission.";
        BenchmarkEvidenceUpdateRequest request = PublishRequest(MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)), ("EPPlus", "Read", Spread(5, 10))), declarations);
        request.Publish = false;
        request.Profile = BenchmarkEvidenceProfile.Diagnostic;
        foreach (BenchmarkSample sample in request.Result.Samples) sample.RunMode = "diagnostic";
        foreach (BenchmarkSummaryRow row in request.Result.Summary) row.RunMode = "diagnostic";

        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request).Entries);

        Assert.DoesNotContain(BenchmarkJson.Read<BenchmarkRunResult>(Path.Combine(root, entry.ArtifactRelativePath)).Summary, row => row.Library == "EPPlus");
        BenchmarkRunView run = Assert.Single(Assert.Single(new BenchmarkComparisonViewBuilder().Build(catalogPath).Comparisons,
            comparison => comparison.Id == "comparison-a").Runs);
        Assert.DoesNotContain(run.Contenders, contender => contender.Library == "EPPlus");
    }

    [Fact]
    public void EvidenceCatalog_RedactsComparisonsOfRestrictedLibrariesSharingAnEngineName()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        declarations.Libraries.Single(value => value.Id == "EPPlus").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "EPPlus").Reason = "Publishing results needs permission.";
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)),
            ("EPPlus", "Write", Spread(5, 10)), ("Sep", "Write", Spread(7, 10)));
        // OfficeIMO and the restricted EPPlus run under one engine name in different cases.
        foreach (BenchmarkSample sample in result.Samples.Where(sample => sample.Library is "OfficeIMO" or "EPPlus"))
            sample.Engine = "Shared";
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);
        result.Comparison = new BenchmarkSummaryService().Compare(result.Summary, "Sep");
        Assert.Contains(result.Comparison, row => row.Scenario == "Write" && row.Engine == "Shared");

        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, PublishRequest(result, declarations)).Entries);

        BenchmarkRunResult stored = BenchmarkJson.Read<BenchmarkRunResult>(Path.Combine(root, entry.ArtifactRelativePath));
        Assert.DoesNotContain(stored.Comparison, row => row.Scenario == "Write" && row.Engine == "Shared");
        Assert.Contains(stored.Comparison, row => row.Scenario == "Read" && row.Engine == "Shared");
        Assert.Contains(stored.Comparison, row => row.Scenario == "Write" && row.Engine == "Sep");
        Assert.Equal(
            BenchmarkJson.Serialize(new BenchmarkSummaryService().Compare(stored.Summary, "Sep")),
            BenchmarkJson.Serialize(stored.Comparison));

        // The merge path removes the same rows and the redacted result still validates.
        BenchmarkEvidenceCatalog merged = new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [catalogPath], null, declarations);
        Assert.True(Assert.Single(merged.Entries).Publish);
    }

    [Fact]
    public void ComparisonView_ShowsOnlyCellsThePublishPolicyAccepted()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        declarations.Libraries.Single(value => value.Id == "EPPlus").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "EPPlus").Reason = "Publishing results needs permission.";
        BenchmarkEvidenceUpdateRequest request = PublishRequest(MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)), ("EPPlus", "Read", Spread(5, 10))), declarations);
        BenchmarkEvidenceCatalog recorded = new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request);

        BenchmarkComparisonView view = Assert.Single(new BenchmarkComparisonViewBuilder().Build(catalogPath).Comparisons,
            comparison => comparison.Id == "comparison-a");

        BenchmarkRunView run = Assert.Single(view.Runs);
        Assert.Equal(new[] { "OfficeIMO", "Sep" }, run.Contenders.Select(contender => contender.Library).OrderBy(name => name));
        BenchmarkEvidenceEntry entry = Assert.Single(recorded.Entries);
        BenchmarkRunResult stored = BenchmarkJson.Read<BenchmarkRunResult>(Path.Combine(root, entry.ArtifactRelativePath));
        Assert.DoesNotContain(stored.Samples, sample => sample.Library == "EPPlus");
        Assert.DoesNotContain(stored.Summary, row => row.Library == "EPPlus");
        Assert.DoesNotContain(stored.Validations, validation => validation.Library == "EPPlus");
        BenchmarkEvidenceCell marker = Assert.Single(entry.Cells, cell => cell.Library == "EPPlus");
        Assert.Null(marker.ValueMs);
        Assert.StartsWith("Not published (licence): ", Assert.Single(marker.Issues, issue => issue.StartsWith("Not published", StringComparison.Ordinal)), StringComparison.Ordinal);

        // A merge keeps the marker although the stored result has no EPPlus rows.
        BenchmarkEvidenceCatalog merged = new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [catalogPath]);
        Assert.Contains(Assert.Single(merged.Entries).Cells, cell => cell.Library == "EPPlus" && !cell.Publishable && cell.ValueMs is null);

        // Declarations given to a merge that restrict another library remove its rows from the merged result too.
        declarations.Libraries.Single(value => value.Id == "Sep").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "Sep").Reason = "Publishing results needs permission.";
        string remergedPath = Path.Combine(root, "remerged", "index.json");
        BenchmarkEvidenceEntry remerged = Assert.Single(new BenchmarkEvidenceCatalogService().MergeFiles(remergedPath, [catalogPath], null, declarations).Entries);
        BenchmarkRunResult remergedResult = BenchmarkJson.Read<BenchmarkRunResult>(Path.Combine(root, "remerged", remerged.ArtifactRelativePath));
        Assert.DoesNotContain(remergedResult.Summary, row => row.Library == "Sep");
        Assert.Equal(remerged.ResultSha256, BenchmarkJson.ComputeFileSha256(Path.Combine(root, "remerged", remerged.ArtifactRelativePath)));
        Assert.Null(Assert.Single(remerged.Cells, cell => cell.Library == "Sep").ValueMs);
    }

    [Fact]
    public void EvidenceCatalog_KeysLanesByFrameworkAndAffinityAndKeepsSupersededRuns()
    {
        var service = new BenchmarkEvidenceCatalogService();
        BenchmarkEvidenceCatalog? catalog = null;
        foreach ((string framework, string? affinity) in new[] { ("net10.0", (string?)null), ("net8.0", null), ("net10.0", "0xffff0000") })
        {
            BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
            result.Metadata["benchmark.workload.framework"] = framework;
            result.Runner!.Affinity = affinity ?? string.Empty;
            BenchmarkEvidenceUpdateRequest request = PublishRequest(result, TestDeclarations());
            request.ResultPath = $"{framework}-{affinity ?? "all"}.json";
            catalog = service.Update(catalog, request);
        }

        Assert.Equal(
            new[] { "comparison-a/windows/net10.0/standard", "comparison-a/windows/net10.0/standard/affinity=0xffff0000", "comparison-a/windows/net8.0/standard" },
            catalog!.Entries.Select(entry => entry.LaneKey).ToArray());
        Assert.All(catalog.Entries, entry => Assert.Equal("comparison-a", entry.ComparisonId));

        BenchmarkRunResult older = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        older.Metadata["benchmark.workload.framework"] = "net8.0";
        string replacedHash = catalog.Entries.Single(entry => entry.Framework == "net8.0").ResultSha256;
        older.StartedUtc = older.StartedUtc.AddMinutes(1);
        older.FinishedUtc = older.FinishedUtc.AddMinutes(1);
        BenchmarkEvidenceUpdateRequest replace = PublishRequest(older, TestDeclarations());
        replace.ResultPath = "net8.0-rerun.json";
        catalog = service.Update(catalog, replace);
        BenchmarkSupersededLane superseded = Assert.Single(catalog.Superseded);
        Assert.Equal("comparison-a/windows/net8.0/standard", superseded.LaneKey);
        Assert.Equal(replacedHash, superseded.ResultSha256);
        Assert.StartsWith("net8.0-rerun.", catalog.Entries.Single(entry => entry.Framework == "net8.0").ResultPath, StringComparison.Ordinal);

        foreach (string encoded in new[] { "comparison-a-net10.0", "comparison-a-affinity-0xffff0000" })
        {
            BenchmarkEvidenceUpdateRequest bad = PublishRequest(MeasuredResult(("OfficeIMO", "Read", Spread(10, 10))), TestDeclarations());
            bad.ComparisonId = encoded;
            ArgumentException rejected = Assert.Throws<ArgumentException>(() => service.Update(null, bad));
            Assert.Contains("lane", rejected.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Declarations_SelectLibrariesOnlyWithExplicitAcceptance()
    {
        BenchmarkLibraryDeclaration[] libraries =
        [
            new() { Id = "OfficeIMO", Package = "OfficeIMO.Excel", Version = "1.0.0", License = "MIT", Restriction = BenchmarkLibraryRestriction.Open },
            new() { Id = "EPPlus", Package = "EPPlus", Version = "8.0.0", License = "LicenseRef-PolyForm-Noncommercial", Restriction = BenchmarkLibraryRestriction.AcceptanceRequired, Reason = "Needs a licence context." },
            new() { Id = "Restricted", Package = "Restricted", Version = "4.0.0", License = "LicenseRef-Restricted", Restriction = BenchmarkLibraryRestriction.PublishRestricted, Reason = "Results need permission to publish." },
            new() { Id = "Legacy", Package = "Legacy", Version = "1.0.0", License = "LicenseRef-Proprietary", Restriction = BenchmarkLibraryRestriction.Excluded, Reason = "Licence forbids benchmarking." }
        ];
        var service = new BenchmarkDeclarationService();

        BenchmarkLibrarySelection defaults = service.SelectLibraries(libraries);
        BenchmarkLibrarySelection accepted = service.SelectLibraries(libraries, exclude: ["Restricted"], accept: ["EPPlus"]);

        Assert.Equal(new[] { "OfficeIMO", "Restricted" }, defaults.Selected);
        Assert.Empty(defaults.Accepted);
        Assert.Contains(defaults.Skipped, skip => skip.Id == "EPPlus" && skip.Reason.Contains("explicit licence acceptance", StringComparison.Ordinal));
        Assert.Contains(defaults.Skipped, skip => skip.Id == "Legacy");
        Assert.Equal(new[] { "EPPlus", "OfficeIMO" }, accepted.Selected);
        Assert.Equal(new[] { "EPPlus" }, accepted.Accepted);
        Assert.Throws<InvalidOperationException>(() => service.SelectLibraries(libraries, include: ["NotDeclared"]));
    }

    [Fact]
    public void Declarations_LintReportsEachInvalidField()
    {
        var document = new BenchmarkDeclarationDocument
        {
            Libraries =
            [
                new BenchmarkLibraryDeclaration { Id = "OfficeIMO", Package = "OfficeIMO.Pdf", Version = "[1.0,2.0)", License = "MIT" },
                new BenchmarkLibraryDeclaration { Id = "QuestPDF", Package = "QuestPDF", Version = "2025.1.0", License = "LicenseRef-QuestPDF", Restriction = BenchmarkLibraryRestriction.PublishRestricted }
            ],
            Comparisons =
            [
                new BenchmarkComparisonDefinition { ComparisonId = "pdf-merge-net10.0", Area = "pdf", Title = "Merge", Subject = "Aspose", Libraries = ["OfficeIMO", "PdfSharp", "officeimo"] }
            ]
        };

        string[] codes = new BenchmarkDeclarationService().Validate(document).Select(issue => issue.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "comparison-id", "comparison-subject", "duplicate-library-reference", "library-reason", "library-restriction", "library-version", "undeclared-library" }, codes);
    }

    [Fact]
    public void Importer_TakesLibraryFromBenchmarkCategoryOrDeclaredMap()
    {
        string root = CreateTempRoot();
        string report = Path.Combine(root, "Comparison-report-full.json");
        File.WriteAllText(report, """
            {
              "Title": "Comparison",
              "HostEnvironmentInfo": { "OsVersion": "Windows 11", "ProcessorName": "Test CPU", "RuntimeVersion": ".NET 10.0.1", "Architecture": "X64", "BenchmarkDotNetCaption": "BenchmarkDotNet v0.15.8" },
              "Benchmarks": [
                { "Method": "ReadFast", "Categories": ["library:OfficeIMO"], "Statistics": { "Median": 1000000, "OriginalValues": [1000000, 1000001] },
                  "Measurements": [
                    { "IterationMode": "Workload", "IterationStage": "Warmup", "LaunchIndex": 1 },
                    { "IterationMode": "Workload", "IterationStage": "Warmup", "LaunchIndex": 1 },
                    { "IterationMode": "Workload", "IterationStage": "Warmup", "LaunchIndex": 1 },
                    { "IterationMode": "Workload", "IterationStage": "Actual", "LaunchIndex": 1 },
                    { "IterationMode": "Workload", "IterationStage": "Actual", "LaunchIndex": 1 } ] },
                { "Method": "ReadCrash", "Categories": ["library:EPPlus"] },
                { "Method": "ReadSep", "Statistics": { "Median": 2000000, "OriginalValues": [2000000, 2000001] },
                  "Measurements": [
                    { "IterationMode": "Workload", "IterationStage": "Actual", "LaunchIndex": 1 },
                    { "IterationMode": "Workload", "IterationStage": "Actual", "LaunchIndex": 1 } ] }
              ]
            }
            """);
        var importer = new BenchmarkResultImporter();

        BenchmarkRunResult mapped = importer.Import(report, new BenchmarkImportOptions { LibraryMap = new Dictionary<string, string> { ["ReadSep"] = "Sep" } });
        InvalidOperationException unmapped = Assert.Throws<InvalidOperationException>(() =>
            importer.Import(report, new BenchmarkImportOptions { LibraryMap = new Dictionary<string, string> { ["Other"] = "Sep" } }));
        BenchmarkRunResult unannotated = importer.Import(report);
        InvalidOperationException statusless = Assert.Throws<InvalidOperationException>(() =>
            importer.Import(report, new BenchmarkImportOptions { Validations = [new BenchmarkOutputValidation { Library = "Sep" }] }));
        Assert.Contains("needs a library and a status", statusless.Message, StringComparison.Ordinal);

        Assert.Equal("OfficeIMO", Assert.Single(mapped.Summary, row => row.Scenario == "ReadFast").Library);
        Assert.Equal("Sep", Assert.Single(mapped.Summary, row => row.Scenario == "ReadSep").Library);
        Assert.All(mapped.Samples, sample => Assert.False(string.IsNullOrEmpty(sample.Library)));
        Assert.Contains("'ReadSep'", unmapped.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, Assert.Single(unannotated.Summary, row => row.Scenario == "ReadSep").Library);
        Assert.Equal("benchmarkdotnet", mapped.Runner!.Kind);
        Assert.Equal("0.15.8", mapped.Runner.Version);
        // ReadSep's measurements show no warmup entries, so the run's warmup count is unknown rather than zero
        // or ReadFast's three; both recorded two measured iterations, and the crashed ReadCrash counts for nothing.
        Assert.Null(mapped.Runner.Warmups);
        Assert.Equal(2, mapped.Runner.Iterations);

        BenchmarkRunResult shared = importer.Import(report, new BenchmarkImportOptions
        {
            LibraryMap = new Dictionary<string, string> { ["ReadSep"] = "Sep" },
            CaseMap = new Dictionary<string, string> { ["ReadFast"] = "Read", ["ReadSep"] = "Read", ["ReadCrash"] = "Read" }
        });
        Assert.All(shared.Summary, row => Assert.Equal("Read", row.Scenario));
        Assert.Equal(new[] { "ReadCrash", "ReadFast", "ReadSep" }, shared.Summary.Select(row => row.Variables["Method"]).OrderBy(name => name));
        Assert.Equal("Failed", Assert.Single(shared.Summary, row => row.Library == "EPPlus").Status);
        BenchmarkEvidenceCell[] cells = BenchmarkEvidenceCellBuilder.Build(shared, "comparison-a", BenchmarkPublishPolicy.CreateDefault());
        Assert.Single(cells.Select(BenchmarkEvidenceCellBuilder.WorkloadIdentity).Distinct());
    }

    [Fact]
    public void Importer_CaseMapRenamesStoredComparisonRowsWithTheirSummaries()
    {
        string root = CreateTempRoot();
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "ReadTyped", Spread(10, 10)), ("Sep", "ReadTyped", Spread(20, 10)));
        result.Comparison = new BenchmarkSummaryService().Compare(result.Summary, "OfficeIMO");
        string path = Path.Combine(root, "run.json");
        BenchmarkJson.Write(path, result);

        BenchmarkRunResult imported = new BenchmarkResultImporter().Import(path, new BenchmarkImportOptions
        {
            CaseMap = new Dictionary<string, string> { ["ReadTyped"] = "Read" }
        });

        Assert.NotEmpty(imported.Comparison);
        Assert.All(imported.Comparison, row => Assert.Equal("Read", row.Scenario));
        Assert.Equal(
            BenchmarkJson.Serialize(new BenchmarkSummaryService().Compare(imported.Summary, "OfficeIMO")),
            BenchmarkJson.Serialize(imported.Comparison));
    }

    [Fact]
    public void CellIdentity_KeepsCaseDistinctValuesAndWorkloadParametersNamedLikeProducerFields()
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("OfficeIMO", "Read", Spread(12, 10)));
        BenchmarkSample[] first = result.Samples.Take(10).ToArray();
        foreach (BenchmarkSample sample in first)
        {
            sample.Variables["Input"] = "a";
            sample.Variables["Type"] = "Fast";
            sample.Variables["BenchmarkDotNetType"] = "Bench.Read";
        }
        foreach (BenchmarkSample sample in result.Samples.Skip(10))
        {
            sample.Variables["Input"] = "A";
            sample.Variables["Type"] = "Fast";
            sample.Variables["BenchmarkDotNetType"] = "Bench.Read";
        }
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);

        BenchmarkEvidenceCell[] cells = BenchmarkEvidenceCellBuilder.Build(result, "comparison-a", BenchmarkPublishPolicy.CreateDefault());

        Assert.Equal(new[] { "A", "a" }, cells.Select(cell => cell.Parameters["Input"]).OrderBy(value => value, StringComparer.Ordinal));
        Assert.All(cells, cell => Assert.Equal("Fast", cell.Parameters["Type"]));
        Assert.All(cells, cell => Assert.DoesNotContain("BenchmarkDotNetType", cell.Parameters.Keys));
    }

    [Fact]
    public void Framework_AcceptsAPlatformSuffixTheRuntimeCannotContradict()
    {
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)));

        Assert.Equal("net10.0-windows", BenchmarkEvidenceIdentity.ResolveFramework(result, "net10.0-windows"));
        result.Metadata["benchmark.workload.framework"] = "net10.0";
        Assert.Throws<InvalidOperationException>(() => BenchmarkEvidenceIdentity.ResolveFramework(result, "net10.0-windows"));
    }

    [Fact]
    public void CellIdentity_KeepsSuiteHostsApart()
    {
        BenchmarkRunResult result = MeasuredResult(("ImportExcel", "Read", Spread(10, 10)), ("ImportExcel", "Read", Spread(12, 10)));
        foreach (BenchmarkSample sample in result.Samples.Skip(10))
            sample.Host = "Desktop-5.1";
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);

        BenchmarkEvidenceCell[] cells = BenchmarkEvidenceCellBuilder.Build(result, "comparison-a", BenchmarkPublishPolicy.CreateDefault());

        Assert.Equal(new[] { "Desktop-5.1", "dotnet" }, cells.Select(cell => cell.Parameters["Host"]).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void MeanConfidenceInterval_UsesStudentT()
    {
        BenchmarkConfidenceInterval interval = BenchmarkEvidenceIdentity.MeanConfidenceInterval(Enumerable.Range(1, 10).Select(value => (double)value), 0.95)!;

        // mean 5.5, standard error 0.9574, t(0.975, 9) = 2.262
        Assert.Equal(3.334, interval.LowerMs, 2);
        Assert.Equal(7.666, interval.UpperMs, 2);
        Assert.Null(BenchmarkEvidenceIdentity.MeanConfidenceInterval([1.0], 0.95));
    }

    [Fact]
    public void EvidenceCatalog_MergeKeepsTheSourcesPolicyAndRefusesUnattributedRowsUnderRestrictions()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkEvidenceUpdateRequest strict = PublishRequest(
            MeasuredResult(("OfficeIMO", "Read", Spread(10, 12)), ("Sep", "Read", Spread(20, 12))), TestDeclarations());
        strict.Policy = new BenchmarkPublishPolicy { MinimumMeasuredSamples = 11 };
        new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, strict);

        BenchmarkEvidenceCatalog merged = new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [catalogPath]);
        Assert.Equal(11, merged.Policy.MinimumMeasuredSamples);

        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        declarations.Libraries.Single(value => value.Id == "EPPlus").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "EPPlus").Reason = "Publishing results needs permission.";
        BenchmarkRunResult unattributed = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("AsposeRead", "Read", Spread(5, 10)));
        foreach (BenchmarkSample sample in unattributed.Samples.Where(sample => sample.Library == "AsposeRead"))
            sample.Library = string.Empty;
        unattributed.Summary = new BenchmarkSummaryService().Summarize(unattributed.Samples);
        BenchmarkEvidenceUpdateRequest diagnostic = PublishRequest(unattributed, declarations);
        diagnostic.Publish = false;
        diagnostic.Profile = BenchmarkEvidenceProfile.Diagnostic;
        foreach (BenchmarkSample sample in unattributed.Samples) sample.RunMode = "diagnostic";
        foreach (BenchmarkSummaryRow row in unattributed.Summary) row.RunMode = "diagnostic";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().UpdateFile(Path.Combine(root, "diagnostic", "index.json"), diagnostic));
        Assert.Contains("rows without a library", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CellIdentity_KeepsParameterValuesThatLookLikeSeparatorsApart()
    {
        var joined = new Dictionary<string, string?> { ["A"] = "x;b=y" };
        var separate = new Dictionary<string, string?> { ["A"] = "x", ["B"] = "y" };

        Assert.NotEqual(BenchmarkEvidenceCellBuilder.FormatParameters(joined), BenchmarkEvidenceCellBuilder.FormatParameters(separate));
    }

    [Fact]
    public void SuiteRunner_RecordsEngineAsLibraryAndItsRunnerBlock()
    {
        var engine = new PowerShellBenchmarkEngine { Name = "ImportExcel" };
        engine.Operations["Read"] = ScriptBlock.Create("param($case, $run)");
        var suite = new PowerShellBenchmarkSuite
        {
            Name = "suite",
            OutputRoot = CreateTempRoot(),
            WarmupCount = 1,
            IterationCount = 2,
            Artifacts = BenchmarkArtifactKind.None
        };
        suite.SourceRoot = suite.OutputRoot;
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Rows", Values = { "100" } });
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Engine", Values = { "ImportExcel" } });
        suite.Axes.Add(new PowerShellBenchmarkAxis { Name = "Operation", Values = { "Read" } });
        suite.Engines.Add(engine);

        BenchmarkRunResult result = new PowerShellBenchmarkRunner().Run(suite);

        BenchmarkSummaryRow row = Assert.Single(result.Summary);
        Assert.Equal("ImportExcel", row.Library);
        Assert.NotEqual(row.Scenario, row.Library);
        Assert.Equal("powershell-suite", result.Runner!.Kind);
        Assert.Equal("rotated", result.Runner.Ordering);
        Assert.Equal(1, result.Runner.Warmups);
        Assert.Equal(2, result.Runner.Iterations);
        Assert.True(result.Runner.InProcess);
        Assert.DoesNotContain("machine", result.Metadata.Keys);
        Assert.Equal(string.Empty, result.Environment.MachineName);

        // The CSV artifacts keep the library, so a re-import from them keeps the engine's library identity.
        suite.Artifacts = BenchmarkArtifactKind.Csv;
        BenchmarkRunResult written = new PowerShellBenchmarkRunner().Run(suite);
        BenchmarkRunResult fromSamples = new BenchmarkResultImporter().Import(written.Artifacts["samples.csv"]);
        BenchmarkRunResult fromSummary = new BenchmarkResultImporter().Import(written.Artifacts["summary.csv"]);
        Assert.All(fromSamples.Samples, sample => Assert.Equal("ImportExcel", sample.Library));
        Assert.Equal("ImportExcel", Assert.Single(fromSummary.Summary).Library);
    }

    [Theory]
    [InlineData(5, null, null)]
    [InlineData(6, 1.0, 6.0)]
    [InlineData(10, 2.0, 9.0)]
    [InlineData(30, 10.0, 21.0)]
    public void MedianConfidenceInterval_UsesBinomialOrderStatistics(int count, double? lower, double? upper)
    {
        BenchmarkConfidenceInterval? interval = BenchmarkEvidenceIdentity.MedianConfidenceInterval(
            Enumerable.Range(1, count).Select(value => (double)value).Reverse(), 0.95);

        Assert.Equal(lower, interval?.LowerMs);
        Assert.Equal(upper, interval?.UpperMs);
    }

    private static double[] Spread(double start, int count)
        => Enumerable.Range(0, count).Select(index => start + index * 0.1).ToArray();

    private static BenchmarkDeclarationDocument ThreeLibraryDeclarations()
    {
        BenchmarkDeclarationDocument declarations = TestDeclarations();
        declarations.Libraries = [.. declarations.Libraries, new BenchmarkLibraryDeclaration { Id = "EPPlus", Package = "EPPlus", Version = "7.7.0", License = "LicenseRef-PolyForm-Noncommercial", Restriction = BenchmarkLibraryRestriction.Open }];
        declarations.Comparisons[0].Libraries = ["OfficeIMO", "Sep", "EPPlus"];
        return declarations;
    }

    private static BenchmarkEvidenceUpdateRequest PublishRequest(BenchmarkRunResult result, BenchmarkDeclarationDocument declarations)
        => new()
        {
            Result = result,
            ComparisonId = "comparison-a",
            ResultPath = "windows.json",
            Profile = BenchmarkEvidenceProfile.Standard,
            Publish = true,
            Declarations = declarations
        };

    /// <summary>A rotated-runner result measured from raw samples; a null sample list records a crashed cell.</summary>
    private static BenchmarkRunResult MeasuredResult(params (string Library, string Case, double[]? Samples)[] cells)
    {
        DateTimeOffset finished = DateTimeOffset.UtcNow.AddMinutes(-1);
        var samples = new List<BenchmarkSample>();
        foreach ((string library, string scenario, double[]? durations) in cells)
        {
            IEnumerable<(double Duration, BenchmarkSampleStatus Status)> values = durations is null
                ? [(0, BenchmarkSampleStatus.Failed)]
                : durations.Select(value => (value, BenchmarkSampleStatus.Succeeded));
            int iteration = 0;
            foreach ((double duration, BenchmarkSampleStatus status) in values)
            {
                samples.Add(new BenchmarkSample
                {
                    RunId = "run",
                    Suite = "csv",
                    Scenario = scenario,
                    Operation = "Run",
                    Engine = library,
                    Library = library,
                    Host = "dotnet",
                    Os = "Windows",
                    RunMode = "standard",
                    Iteration = iteration++,
                    Status = status,
                    DurationMs = duration,
                    Reason = status == BenchmarkSampleStatus.Failed ? "Process exited with exit code 255." : string.Empty
                });
            }
        }

        return new BenchmarkRunResult
        {
            RunId = "run",
            Suite = "csv",
            StartedUtc = finished.AddMinutes(-5),
            FinishedUtc = finished,
            Samples = samples.ToArray(),
            Summary = new BenchmarkSummaryService().Summarize(samples),
            Runner = new BenchmarkRunnerInfo { Kind = "rotated", Version = "1.0.0", Statistic = "median", Warmups = 3, Iterations = 10, Launches = 1, Ordering = "rotated", InProcess = false },
            Validations = cells.Select(cell => cell.Library).Distinct()
                .Select(library => new BenchmarkOutputValidation { Library = library, Status = BenchmarkValidationStatus.Passed, Detail = "sha256:expected" })
                .ToArray(),
            Environment = new BenchmarkEnvironmentInfo
            {
                OsFamily = "Windows",
                OsDescription = "Windows 11 24H2",
                OsArchitecture = "X64",
                ProcessArchitecture = "X64",
                ProcessorName = "AMD Ryzen 9 7950X",
                LogicalCoreCount = 32,
                RuntimeVersion = ".NET 10.0.1",
                DotNetSdkVersion = "10.0.100",
                Runner = "rotated 1.0.0"
            },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gitSha"] = "0123456789abcdef0123456789abcdef01234567",
                ["gitWorktreeClean"] = "true",
                ["benchmark.package.sep"] = "0.10.0",
                ["benchmark.package.sep.sha256"] = "aa11",
                ["benchmark.package.epplus"] = "7.7.0",
                ["benchmark.package.epplus.sha256"] = "bb22"
            }
        };
    }
}
