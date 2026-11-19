using PowerForge;

namespace PowerForge.Tests;

// Catalog tests record lanes against one declared library policy, as producers do, so lane mechanics are
// exercised under the same publish policy that real evidence meets.
public sealed partial class BenchmarkServicesTests
{
    private static BenchmarkDeclarationDocument TestDeclarations(string? comparisonId = null, IEnumerable<BenchmarkComparisonDefinition>? existing = null)
    {
        BenchmarkDeclarationDocument document = BaseDeclarations();
        var comparisons = document.Comparisons.ToList();
        foreach (BenchmarkComparisonDefinition definition in existing ?? [])
        {
            comparisons.RemoveAll(value => value.ComparisonId == definition.ComparisonId);
            comparisons.Add(definition);
        }
        if (comparisonId is not null && BenchmarkEvidenceIdentity.DescribeComparisonIdProblem(comparisonId) is null &&
            !comparisons.Any(value => value.ComparisonId == comparisonId))
            comparisons.Add(new BenchmarkComparisonDefinition { ComparisonId = comparisonId, Area = "csv", Title = comparisonId, Subject = "OfficeIMO", Libraries = ["OfficeIMO", "Sep"] });
        document.Comparisons = comparisons.ToArray();
        return document;
    }

    private static BenchmarkDeclarationDocument BaseDeclarations() => new()
    {
        Libraries =
        [
            new BenchmarkLibraryDeclaration { Id = "OfficeIMO", Package = "OfficeIMO.CSV", Version = "3.0.3", License = "MIT", Restriction = BenchmarkLibraryRestriction.Open },
            new BenchmarkLibraryDeclaration { Id = "Sep", Package = "Sep", Version = "0.10.0", License = "MIT", Restriction = BenchmarkLibraryRestriction.Open }
        ],
        Comparisons =
        [
            new BenchmarkComparisonDefinition { ComparisonId = "comparison-a", Area = "csv", Title = "CSV typed read", Subject = "OfficeIMO", Libraries = ["OfficeIMO", "Sep"] },
            new BenchmarkComparisonDefinition { ComparisonId = "comparison-b", Area = "csv", Title = "CSV typed write", Subject = "OfficeIMO", Libraries = ["OfficeIMO", "Sep"] }
        ]
    };

    private static string TestProfile(string profile)
        => string.Equals(profile, "quick", StringComparison.OrdinalIgnoreCase) ? "diagnostic" : profile;

    private static BenchmarkEvidenceCatalog UpdateDeclared(
        BenchmarkEvidenceCatalogService service,
        BenchmarkEvidenceCatalog? catalog,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string runMode,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null,
        string? platform = null)
        => service.Update(catalog, new BenchmarkEvidenceUpdateRequest
        {
            Result = result,
            ComparisonId = comparisonId,
            ResultPath = resultPath,
            Profile = BenchmarkEvidenceCatalogService.ParseProfile(TestProfile(runMode)),
            Publish = publish,
            ExpectedPlatforms = expectedPlatforms?.ToArray(),
            Platform = platform,
            Declarations = DeclareCases(TestDeclarations(comparisonId, catalog?.Comparisons), comparisonId, result)
        });

    private static BenchmarkEvidenceCatalog UpdateFileDeclared(
        BenchmarkEvidenceCatalogService service,
        string catalogPath,
        BenchmarkRunResult result,
        string comparisonId,
        string resultPath,
        string runMode,
        bool publish,
        IEnumerable<string>? expectedPlatforms = null,
        string? platform = null,
        string? resultArtifactPath = null)
        => service.UpdateFile(catalogPath, new BenchmarkEvidenceUpdateRequest
        {
            Result = result,
            ComparisonId = comparisonId,
            ResultPath = resultPath,
            Profile = BenchmarkEvidenceCatalogService.ParseProfile(TestProfile(runMode)),
            Publish = publish,
            ExpectedPlatforms = expectedPlatforms?.ToArray(),
            Platform = platform,
            ResultArtifactPath = resultArtifactPath,
            Declarations = DeclareCases(TestDeclarations(comparisonId, ExistingComparisons(catalogPath)), comparisonId, result)
        });

    /// <summary>
    /// Declares a comparison's cases from the result being recorded when the fixture declares none, as a declaration
    /// generated from the benchmark code would. Comparisons without cases are not publishable.
    /// </summary>
    private static BenchmarkDeclarationDocument DeclareCases(BenchmarkDeclarationDocument declarations, string comparisonId, BenchmarkRunResult result)
    {
        BenchmarkComparisonDefinition? comparison = declarations.Comparisons
            .FirstOrDefault(value => string.Equals(value.ComparisonId, comparisonId, StringComparison.OrdinalIgnoreCase));
        if (comparison is null || comparison.Cases.Length > 0 || comparison.Cells.Length > 0 || result is null)
            return declarations;
        comparison.Cases = BenchmarkEvidenceCellBuilder.Build(result, comparisonId, BenchmarkPublishPolicy.CreateDefault())
            .Select(cell => cell.Case)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return declarations;
    }

    private static BenchmarkComparisonDefinition[] ExistingComparisons(string catalogPath)
    {
        try
        {
            return File.Exists(catalogPath)
                ? new BenchmarkEvidenceCatalogService().ReadCatalog(catalogPath).Comparisons
                : [];
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            // Tests that write invalid catalogs on purpose expect the service to report them.
            return [];
        }
    }

    /// <summary>Adds what the publish policy needs to a hand-built result: runner block, library identity, samples and validation.</summary>
    private static BenchmarkRunResult Publishable(BenchmarkRunResult result, string library = "OfficeIMO")
    {
        result.Runner ??= new BenchmarkRunnerInfo { Kind = "benchmarkdotnet", Version = "0.15.8", Statistic = "median", Warmups = 6, Iterations = 15, Launches = 1, Ordering = "sequential" };
        result.Environment.OsDescription = string.IsNullOrWhiteSpace(result.Environment.OsDescription) ? result.Environment.OsFamily + " test" : result.Environment.OsDescription;
        result.Environment.LogicalCoreCount ??= 16;
        result.Environment.DotNetSdkVersion = string.IsNullOrWhiteSpace(result.Environment.DotNetSdkVersion) ? "10.0.100" : result.Environment.DotNetSdkVersion;
        foreach (BenchmarkSummaryRow row in result.Summary)
        {
            if (string.IsNullOrWhiteSpace(row.Library))
                row.Library = library;
            if (result.Samples.Length == 0 && string.Equals(row.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                row.SampleCount = Math.Max(row.SampleCount, 10);
                double median = row.MedianMs ?? row.MeanMs ?? 1;
                row.ConfidenceInterval ??= new BenchmarkConfidenceInterval { Level = 0.95, LowerMs = median * 0.98, UpperMs = median * 1.02 };
            }
        }
        foreach (BenchmarkSample sample in result.Samples.Where(sample => string.IsNullOrWhiteSpace(sample.Library)))
            sample.Library = library;
        result.Validations = result.Summary.Select(row => row.Library).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new BenchmarkOutputValidation { Library = id, Status = BenchmarkValidationStatus.Passed, Detail = "sha256:test" })
            .ToArray();
        return result;
    }
}
