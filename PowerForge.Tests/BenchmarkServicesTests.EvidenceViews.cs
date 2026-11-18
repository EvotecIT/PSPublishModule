using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void ComparisonView_CarriesLaneFactsEligibilityAvailabilityAndLibraryStates()
    {
        string root = CreateTempRoot();
        string catalogPath = RecordViewCatalog(root);

        BenchmarkComparisonCatalogView view = new BenchmarkComparisonViewBuilder().Build(catalogPath);

        BenchmarkComparisonView read = Assert.Single(view.Comparisons, comparison => comparison.Id == "comparison-a");
        Assert.Equal("read", read.Group);
        Assert.Equal(new[] { "net10.0" }, read.Frameworks);
        Assert.True(read.Eligibility.Publishable);
        Assert.Equal("Windows · net10.0 · standard · " + DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyy-MM-dd"), Assert.Single(read.Availability, value => value.Platform == "windows").Text);
        Assert.Equal("Linux · net10.0 · not measured", Assert.Single(read.Availability, value => value.Platform == "linux").Text);
        BenchmarkLibraryStateView epplus = Assert.Single(read.LibraryStates, state => state.Id == "EPPlus");
        Assert.False(epplus.Published);
        Assert.Equal("publish-restricted", epplus.Restriction);
        Assert.Equal("not published (licence)", epplus.Note);

        BenchmarkRunView run = Assert.Single(read.Runs);
        Assert.Equal("net10.0", run.Framework);
        Assert.Equal("standard", run.Profile);
        Assert.Equal("Standard run", run.ProfileText);
        Assert.Equal("0xff", run.LaneParameters["affinity"]);
        Assert.Equal("Rotated runner 1.0.0 · median · 3 warmups · 10 iterations · 1 launch · rotated · separate processes · affinity 0xff", run.Runner.Text);
        Assert.Equal("complete", run.Completeness);
        Assert.True(run.Comparable);
        BenchmarkNotPublishedView dropped = Assert.Single(run.NotPublished);
        Assert.Equal("EPPlus", dropped.Library);
        Assert.True(dropped.Licence);
        Assert.DoesNotContain(run.Contenders, contender => contender.Library == "EPPlus");

        BenchmarkComparisonView single = Assert.Single(view.Comparisons, comparison => comparison.Id == "single-library");
        Assert.False(single.Eligibility.Publishable);
        Assert.Contains("Single-library", single.Eligibility.Reason, StringComparison.Ordinal);
        Assert.False(single.Measured);
    }

    [Fact]
    public void ComparisonView_WritesAnIndexAndOneFilePerArea()
    {
        string root = CreateTempRoot();
        BenchmarkComparisonCatalogView view = new BenchmarkComparisonViewBuilder().Build(RecordViewCatalog(root));
        string output = Path.Combine(root, "site", "data", "benchmarks");

        string[] written = BenchmarkComparisonViewBuilder.WriteAreaFiles(view, output);

        Assert.Equal(new[] { "index.json", "csv.json", "pdf.json" }, written.Select(Path.GetFileName));
        BenchmarkComparisonIndexView index = BenchmarkJson.Read<BenchmarkComparisonIndexView>(Path.Combine(output, "index.json"));
        BenchmarkAreaIndexView csv = Assert.Single(index.Areas, area => area.Id == "csv");
        Assert.Equal("csv.json", csv.File);
        Assert.Equal(new[] { "read" }, csv.Groups);
        Assert.Equal(1, csv.Measured);
        Assert.Equal("OfficeIMO is 1.96× faster than Sep", Assert.Single(Assert.Single(csv.Comparisons).Headlines));
        BenchmarkAreaView pdf = BenchmarkJson.Read<BenchmarkAreaView>(Path.Combine(output, "pdf.json"));
        Assert.Equal("single-library", Assert.Single(pdf.Comparisons).Id);
    }

    [Fact]
    public void ReadmeRenderer_ShowsPublishedRunsOfSelectedComparisonsAndGroups()
    {
        string root = CreateTempRoot();
        BenchmarkComparisonCatalogView view = new BenchmarkComparisonViewBuilder().Build(RecordViewCatalog(root));
        var renderer = new BenchmarkComparisonMarkdownRenderer();

        string windows = renderer.Render(view, new BenchmarkComparisonMarkdownRequest { Comparisons = ["read"], Platform = "windows" });
        string linux = renderer.Render(view, new BenchmarkComparisonMarkdownRequest { Comparisons = ["comparison-a"], Platform = "linux" });

        Assert.Contains("#### CSV typed read", windows, StringComparison.Ordinal);
        Assert.Contains("| Case | OfficeIMO | Sep |", windows, StringComparison.Ordinal);
        Assert.Contains("- **OfficeIMO is 1.96× faster than Sep**", windows, StringComparison.Ordinal);
        Assert.Contains("- EPPlus: not published (licence).", windows, StringComparison.Ordinal);
        Assert.Contains("- Runner: Rotated runner 1.0.0", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("EPPlus |", windows, StringComparison.Ordinal);
        Assert.Contains("_No published linux run yet._", linux, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            renderer.Render(view, new BenchmarkComparisonMarkdownRequest { Comparisons = ["not-declared"] }));
    }

    [Fact]
    public void UpdateBenchmarkDocument_RendersACatalogBlock()
    {
        string root = CreateTempRoot();
        string catalogPath = RecordViewCatalog(root);
        string readme = Path.Combine(root, "README.md");
        File.WriteAllText(readme, "# Project\n\n<!-- BENCHMARK:csv-read:START -->\nold\n<!-- BENCHMARK:csv-read:END -->\n");
        var state = InitialSessionState.CreateDefault();
        state.Commands.Add(new SessionStateCmdletEntry("Update-BenchmarkDocument", typeof(PSPublishModule.UpdateBenchmarkDocumentCommand), null));
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        using var ps = PowerShell.Create(runspace);

        ps.AddCommand("Update-BenchmarkDocument").AddParameter("Path", readme).AddParameter("BlockId", "csv-read")
            .AddParameter("Catalog", catalogPath).AddParameter("Comparison", new[] { "comparison-a" }).AddParameter("Framework", "net10.0");
        var result = Assert.IsType<BenchmarkDocumentUpdateResult>(Assert.Single(ps.Invoke()).BaseObject);

        Assert.True(result.Changed);
        string text = File.ReadAllText(readme);
        Assert.Contains("| Case | OfficeIMO | Sep |", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nold\n", text.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    /// <summary>A catalog with one published lane (EPPlus licence-restricted) and one declared single-library comparison.</summary>
    private static string RecordViewCatalog(string root)
    {
        string catalogPath = Path.Combine(root, "index.json");
        BenchmarkDeclarationDocument declarations = ThreeLibraryDeclarations();
        declarations.Libraries.Single(value => value.Id == "EPPlus").Restriction = BenchmarkLibraryRestriction.PublishRestricted;
        declarations.Libraries.Single(value => value.Id == "EPPlus").Reason = "Publishing results needs permission.";
        declarations.Comparisons[0].Group = "read";
        declarations.Comparisons[0].Frameworks = ["net10.0"];
        declarations.Comparisons =
        [
            declarations.Comparisons[0],
            new BenchmarkComparisonDefinition { ComparisonId = "single-library", Area = "pdf", Group = "merge", Title = "PDF merge", Subject = "OfficeIMO", Libraries = ["OfficeIMO"] }
        ];
        BenchmarkRunResult result = MeasuredResult(
            ("OfficeIMO", "Read", Spread(10, 10)), ("Sep", "Read", Spread(20, 10)), ("EPPlus", "Read", Spread(5, 10)));
        result.Runner!.Affinity = "0xff";
        new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, PublishRequest(result, declarations));
        return catalogPath;
    }
}
