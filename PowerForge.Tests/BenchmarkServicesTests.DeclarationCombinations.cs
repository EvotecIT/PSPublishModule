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
