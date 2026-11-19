using PowerForge;

namespace PowerForge.Tests;

// Drift is checked per case × library × axis-value combination, so one absent combination cannot hide behind
// other rows that cover each dimension separately.
public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void Declarations_ReportAMissingCombinationAndBlockPublishing()
    {
        BenchmarkDeclarationDocument declarations = CombinationDeclarations();
        BenchmarkRunResult result = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)));

        BenchmarkDeclarationIssue drift = Assert.Single(new BenchmarkDeclarationService()
            .CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(null, PublishRequest(result, declarations)));

        Assert.Equal("missing-combination", drift.Code);
        Assert.Equal("Declared combination case 'Create', library 'Sep', Scale=Normal did not run.", drift.Message);
        Assert.Contains("Declaration drift: Declared combination case 'Create', library 'Sep', Scale=Normal did not run.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Declarations_ReportACombinationTheExplicitCellsDoNotList()
    {
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        BenchmarkRunResult result = ScaledResult(
            ("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)), ("Sep", "Normal", Spread(22, 10)));

        BenchmarkDeclarationIssue drift = Assert.Single(new BenchmarkDeclarationService()
            .CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));

        Assert.Equal("unexpected-combination", drift.Code);
        Assert.Equal("Combination case 'Create', library 'Sep', Scale=Normal ran but is not declared.", drift.Message);
    }

    [Fact]
    public void Declarations_AcceptASparseMatrixListedAsExplicitCells()
    {
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        BenchmarkRunResult result = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)));

        Assert.Empty(new BenchmarkDeclarationService().Validate(declarations));
        Assert.Empty(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService().Update(null, PublishRequest(result, declarations)).Entries);
        Assert.True(entry.Publish);
    }

    [Fact]
    public void Declarations_CountAFailedCellAsAPresentCombination()
    {
        BenchmarkDeclarationDocument declarations = CombinationDeclarations();
        BenchmarkRunResult result = ScaledResult(
            ("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)), ("Sep", "Normal", null));

        Assert.Empty(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
    }

    [Fact]
    public void Declarations_RejectExplicitCellsOutsideTheDeclaredMatrix()
    {
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        declarations.Comparisons[0].Cells =
        [
            new BenchmarkExpectedCell { Case = "Create", Library = "Sep", Parameters = new() { ["Scale"] = "Huge" } },
            new BenchmarkExpectedCell { Case = "Create", Parameters = new() }
        ];

        string[] messages = new BenchmarkDeclarationService().Validate(declarations)
            .Where(issue => issue.Code == "comparison-cell").Select(issue => issue.Message).ToArray();

        Assert.Equal(new[]
        {
            "Cell value 'Huge' is not declared for axis 'Scale'.",
            "A cell for case 'Create' needs a value for axis 'Scale'."
        }, messages);
    }

    [Fact]
    public void Declarations_HoldBackComparisonsWithoutCasesWithoutCallingItDrift()
    {
        string root = CreateTempRoot();
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkDeclarationDocument declarations = TestDeclarations();
        BenchmarkRunResult result = MeasuredResult(("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)));
        var request = new BenchmarkEvidenceUpdateRequest
        {
            Result = result,
            ComparisonId = "comparison-a",
            ResultPath = "windows.json",
            Profile = BenchmarkEvidenceProfile.Standard,
            Publish = true,
            Declarations = declarations
        };

        Assert.Empty(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request));
        request.Publish = false;
        BenchmarkEvidenceCatalog catalog = new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request);
        BenchmarkComparisonView view = Assert.Single(new BenchmarkComparisonViewBuilder().Build(catalogPath).Comparisons, comparison => comparison.Id == "comparison-a");

        Assert.Contains(BenchmarkDeclarationService.NoCasesReason, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Declaration drift", refused.Message, StringComparison.Ordinal);
        Assert.False(Assert.Single(catalog.Entries).Publish);
        Assert.False(view.Eligibility.Publishable);
        Assert.Equal(BenchmarkDeclarationService.NoCasesReason, view.Eligibility.Reason);
        Assert.Single(view.Runs);
    }

    [Fact]
    public void Declarations_KeepExplicitCellsThroughCatalogUpdatesAndMerges()
    {
        string root = CreateTempRoot();
        string source = Path.Combine(root, "source", "index.json");
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        BenchmarkEvidenceUpdateRequest request = PublishRequest(
            ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10))), declarations);
        request.ResultArtifactPath = Path.Combine(root, "source", "windows.json");
        new BenchmarkEvidenceCatalogService().UpdateFile(source, request);

        BenchmarkComparisonDefinition stored = new BenchmarkEvidenceCatalogService().ReadCatalog(source).Comparisons.Single(value => value.ComparisonId == "comparison-a");
        // Merging without the declaration document re-checks the lane against the catalog's own copy.
        BenchmarkEvidenceCatalog merged = new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [source]);

        Assert.Equal(2, stored.Cells.Length);
        Assert.Equal("Normal", stored.Cells.Single(cell => cell.Library == "OfficeIMO").Parameters["Scale"]);
        Assert.True(Assert.Single(merged.Entries).Publish);
    }

    [Fact]
    public void Declarations_CompareAxisValuesWithoutSurroundingWhitespace()
    {
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        declarations.Comparisons[0].Axes = [new BenchmarkComparisonAxis { Name = "Scale", Values = [" Easy ", "Normal "] }];
        declarations.Comparisons[0].Cells[1].Parameters["Scale"] = " Normal";
        BenchmarkRunResult result = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)));

        Assert.Empty(new BenchmarkDeclarationService().Validate(declarations));
        Assert.Empty(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
    }

    [Fact]
    public void Declarations_CountAFailedSampleTheSummaryLeftOutAsPresent()
    {
        BenchmarkDeclarationDocument declarations = CombinationDeclarations();
        BenchmarkRunResult result = ScaledResult(
            ("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)), ("Sep", "Normal", null));
        // A producer that summarizes only successful cases leaves Sep at Normal out of the summary.
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples.Where(sample => sample.Status == BenchmarkSampleStatus.Succeeded));
        // Publishing requires a summary that matches the raw samples, so such a result is recorded unpublished.
        BenchmarkEvidenceUpdateRequest request = PublishRequest(result, declarations);
        request.Publish = false;

        Assert.Empty(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
        BenchmarkEvidenceEntry entry = Assert.Single(new BenchmarkEvidenceCatalogService().Update(null, request).Entries);
        BenchmarkEvidenceCell failed = Assert.Single(entry.Cells, cell => cell.Library == "Sep" && cell.Parameters["Scale"] == "Normal");
        Assert.Equal("Failed", failed.Status);
    }

    [Fact]
    public void Declarations_RequireOnlyTheCellsOfLibrariesThatRan()
    {
        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        BenchmarkComparisonDefinition comparison = declarations.Comparisons[0];
        comparison.Cases = ["Create"];
        comparison.Axes = [new BenchmarkComparisonAxis { Name = "Scale", Values = ["Easy", "Normal"] }];
        // Only EPPlus has a Normal scale.
        comparison.Cells =
        [
            new BenchmarkExpectedCell { Case = "Create", Parameters = new() { ["Scale"] = "Easy" } },
            new BenchmarkExpectedCell { Case = "Create", Library = "EPPlus", Parameters = new() { ["Scale"] = "Normal" } }
        ];
        BenchmarkRunResult withoutEpplus = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("Sep", "Easy", Spread(12, 10)));
        BenchmarkRunResult epplusWithoutNormal = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("Sep", "Easy", Spread(12, 10)), ("EPPlus", "Easy", Spread(14, 10)));

        Assert.Empty(new BenchmarkDeclarationService().CheckDrift(comparison, declarations.Libraries, withoutEpplus));
        BenchmarkDeclarationIssue missing = Assert.Single(new BenchmarkDeclarationService().CheckDrift(comparison, declarations.Libraries, epplusWithoutNormal));
        Assert.Equal("missing-axis-value", missing.Code);
    }

    [Fact]
    public void Declarations_TakeCaseNamesFromCellsWhenNoCasesAreDeclared()
    {
        string root = CreateTempRoot();
        string source = Path.Combine(root, "source", "index.json");
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        declarations.Comparisons[0].Cases = [];
        BenchmarkRunResult result = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)));
        BenchmarkRunResult otherCase = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10)));
        foreach (BenchmarkSample sample in otherCase.Samples.Where(sample => sample.Library == "Sep"))
            sample.Scenario = "Append";
        otherCase.Summary = new BenchmarkSummaryService().Summarize(otherCase.Samples);
        BenchmarkEvidenceUpdateRequest request = PublishRequest(result, declarations);
        request.ResultArtifactPath = Path.Combine(root, "source", "windows.json");

        Assert.Empty(new BenchmarkDeclarationService().Validate(declarations));
        Assert.Empty(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, result));
        Assert.Contains(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, otherCase),
            issue => issue.Code == "unexpected-case" && issue.Message == "Case 'Append' ran but is not declared.");
        Assert.True(Assert.Single(new BenchmarkEvidenceCatalogService().UpdateFile(source, request).Entries).Publish);
        BenchmarkEvidenceCatalog merged = new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [source]);
        Assert.True(Assert.Single(merged.Entries).Publish);
    }

    [Fact]
    public void Declarations_RejectCellParametersRepeatedInAnotherCase()
    {
        string root = CreateTempRoot();
        string source = Path.Combine(root, "source", "index.json");
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        BenchmarkEvidenceUpdateRequest request = PublishRequest(
            ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10))), declarations);
        request.ResultArtifactPath = Path.Combine(root, "source", "windows.json");
        new BenchmarkEvidenceCatalogService().UpdateFile(source, request);
        BenchmarkDeclarationDocument repeated = SparseCombinationDeclarations();
        repeated.Comparisons[0].Cells[0].Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["Scale"] = "Easy", ["scale"] = "Easy" };
        // A catalog edited or produced elsewhere can carry the repeated name without its declaration document.
        var catalog = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(source))!;
        catalog["comparisons"]![0]!["cells"]![0]!["parameters"]!["scale"] = "Easy";
        File.WriteAllText(source, catalog.ToJsonString());

        string message = Assert.Single(new BenchmarkDeclarationService().Validate(repeated), issue => issue.Code == "comparison-cell").Message;
        InvalidOperationException merge = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [source]));
        InvalidOperationException update = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().Update(null, PublishRequest(request.Result, repeated)));

        Assert.Equal("A cell for case 'Create' gives parameter 'Scale' more than once; parameter names ignore case.", message);
        Assert.Contains("gives parameter 'scale' more than once; parameter names ignore case", merge.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gives parameter 'Scale' more than once", update.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Declarations_DoNotCountBlankCaseNamesAsDeclaredCases()
    {
        BenchmarkDeclarationDocument declarations = TestDeclarations();
        declarations.Comparisons[0].Cases = [" "];
        var service = new BenchmarkDeclarationService();

        BenchmarkDeclarationIssue blank = Assert.Single(service.Validate(declarations), issue => issue.Code == "comparison-case");
        BenchmarkEligibility eligibility = service.DeriveEligibility(declarations.Comparisons[0], declarations.Libraries, BenchmarkPublishPolicy.CreateDefault());

        Assert.Equal("Case names can't be blank.", blank.Message);
        Assert.False(eligibility.Publishable);
        Assert.Equal(BenchmarkDeclarationService.NoCasesReason, eligibility.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Declarations_RequireEveryExplicitCellToNameItsCase(bool withCases)
    {
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        if (!withCases)
            declarations.Comparisons[0].Cases = [];
        foreach (BenchmarkExpectedCell cell in declarations.Comparisons[0].Cells)
            cell.Case = string.Empty;
        var service = new BenchmarkDeclarationService();

        string[] messages = service.Validate(declarations).Where(issue => issue.Code == "comparison-cell").Select(issue => issue.Message).Distinct().ToArray();
        BenchmarkEligibility eligibility = service.DeriveEligibility(declarations.Comparisons[0], declarations.Libraries, BenchmarkPublishPolicy.CreateDefault());

        Assert.Equal(new[] { "Every cell needs a case." }, messages);
        // Without named cases a cells-only declaration can't be checked, so it is held back; with declared cases the
        // validation error alone stops the document from being used.
        Assert.Equal(withCases, eligibility.Publishable);
        if (!withCases)
            Assert.Equal(BenchmarkDeclarationService.NoCasesReason, eligibility.Reason);
    }

    [Fact]
    public void Declarations_RejectBlankAxisValues()
    {
        string root = CreateTempRoot();
        string source = Path.Combine(root, "source", "index.json");
        BenchmarkDeclarationDocument declarations = SparseCombinationDeclarations();
        BenchmarkEvidenceUpdateRequest request = PublishRequest(
            ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "Easy", Spread(12, 10))), declarations);
        request.ResultArtifactPath = Path.Combine(root, "source", "windows.json");
        new BenchmarkEvidenceCatalogService().UpdateFile(source, request);
        BenchmarkDeclarationDocument blank = SparseCombinationDeclarations();
        blank.Comparisons[0].Axes = [new BenchmarkComparisonAxis { Name = "Scale", Values = ["Easy", "Normal", ""] }];
        blank.Comparisons[0].Cells = [.. blank.Comparisons[0].Cells, new BenchmarkExpectedCell { Case = "Create", Parameters = new() { ["Scale"] = " " } }];
        // A catalog edited or produced elsewhere can carry a blank value without its declaration document.
        var catalog = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(source))!;
        catalog["comparisons"]![0]!["axes"]![0]!["values"]!.AsArray().Add("");
        File.WriteAllText(source, catalog.ToJsonString());

        string[] messages = new BenchmarkDeclarationService().Validate(blank)
            .Where(issue => issue.Code is "comparison-axis" or "comparison-cell").Select(issue => issue.Message).ToArray();
        InvalidOperationException merge = Assert.Throws<InvalidOperationException>(() =>
            new BenchmarkEvidenceCatalogService().MergeFiles(Path.Combine(root, "merged", "index.json"), [source]));

        Assert.Equal(new[]
        {
            "Axis 'Scale' values can't be blank.",
            "A cell for case 'Create' has a blank value for axis 'Scale'."
        }, messages);
        Assert.Contains("axis 'Scale' has a blank value", merge.Message, StringComparison.Ordinal);

        // An empty value a runner records is never declared, so it surfaces instead of hiding.
        BenchmarkRunResult emptyScale = ScaledResult(("OfficeIMO", "Easy", Spread(10, 10)), ("OfficeIMO", "Normal", Spread(20, 10)), ("Sep", "", Spread(12, 10)));
        Assert.Contains(new BenchmarkDeclarationService().CheckDrift(declarations.Comparisons[0], declarations.Libraries, emptyScale),
            issue => issue.Code == "unexpected-axis-value" && issue.Message == "Axis 'Scale' value '' ran but is not declared.");
    }

    private static BenchmarkDeclarationDocument CombinationDeclarations()
    {
        BenchmarkDeclarationDocument declarations = TestDeclarations();
        declarations.Comparisons[0].Cases = ["Create"];
        declarations.Comparisons[0].Axes = [new BenchmarkComparisonAxis { Name = "Scale", Values = ["Easy", "Normal"] }];
        return declarations;
    }

    // Sep has no Normal scale, so the valid cells are listed explicitly.
    private static BenchmarkDeclarationDocument SparseCombinationDeclarations()
    {
        BenchmarkDeclarationDocument declarations = CombinationDeclarations();
        declarations.Comparisons[0].Cells =
        [
            new BenchmarkExpectedCell { Case = "Create", Parameters = new() { ["Scale"] = "Easy" } },
            new BenchmarkExpectedCell { Case = "Create", Library = "OfficeIMO", Parameters = new() { ["Scale"] = "Normal" } }
        ];
        return declarations;
    }

    /// <summary>A "Create" result with one cell per library and scale; null samples record a failed cell.</summary>
    private static BenchmarkRunResult ScaledResult(params (string Library, string Scale, double[]? Samples)[] cells)
    {
        BenchmarkRunResult result = MeasuredResult(cells.Select(cell => (cell.Library, "Create", cell.Samples)).ToArray());
        int index = 0;
        foreach ((string _, string scale, double[]? samples) in cells)
        {
            int count = samples?.Length ?? 1;
            for (int sample = 0; sample < count; sample++)
                result.Samples[index++].Variables["Scale"] = scale;
        }
        result.Summary = new BenchmarkSummaryService().Summarize(result.Samples);
        return result;
    }
}
