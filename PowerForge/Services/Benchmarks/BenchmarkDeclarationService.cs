namespace PowerForge;

/// <summary>
/// Reads, validates and applies benchmark declarations: the library policy and comparison matrix.
/// </summary>
public sealed partial class BenchmarkDeclarationService
{
    /// <summary>Reads a schema 5 declaration document.</summary>
    /// <param name="path">Declaration JSON path.</param>
    /// <returns>Declaration document.</returns>
    public BenchmarkDeclarationDocument Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Declaration path is required.", nameof(path));
        string text = File.ReadAllText(path);
        if (text.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Benchmark declaration '{path}' is a bare comparison array from an older format. " +
                "Write a schema 5 declaration document with 'schemaVersion': 5, 'libraries' and 'comparisons'.");
        }
        BenchmarkDeclarationDocument document = BenchmarkJson.Read<BenchmarkDeclarationDocument>(path);
        if (document.SchemaVersion != BenchmarkEvidenceCatalogService.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Benchmark declaration '{path}' uses schema {document.SchemaVersion}. Only schema " +
                $"{BenchmarkEvidenceCatalogService.CurrentSchemaVersion} is supported.");
        }
        return document;
    }

    /// <summary>Validates library and comparison declarations.</summary>
    /// <param name="document">Declaration document.</param>
    /// <returns>Findings; errors have severity <c>error</c>.</returns>
    public BenchmarkDeclarationIssue[] Validate(BenchmarkDeclarationDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var issues = new List<BenchmarkDeclarationIssue>();
        if (document.SchemaVersion != BenchmarkEvidenceCatalogService.CurrentSchemaVersion)
            Add(issues, "declaration", "schema", $"Schema {document.SchemaVersion} is not supported; use schema {BenchmarkEvidenceCatalogService.CurrentSchemaVersion}.");

        var libraries = new Dictionary<string, BenchmarkLibraryDeclaration>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkLibraryDeclaration library in document.Libraries ?? Array.Empty<BenchmarkLibraryDeclaration>())
        {
            string id = library?.Id?.Trim() ?? string.Empty;
            if (library is null || id.Length == 0)
            {
                Add(issues, "library", "library-id", "Every library needs an id.");
                continue;
            }
            if (libraries.ContainsKey(id))
                Add(issues, id, "duplicate-library", "The library is declared more than once.");
            libraries[id] = library;
            if (string.IsNullOrWhiteSpace(library.Package))
                Add(issues, id, "library-package", "The library needs the package it is measured from.");
            if (string.IsNullOrWhiteSpace(library.Version) || library.Version.IndexOfAny(new[] { '*', '[', '(', ',', ' ' }) >= 0)
                Add(issues, id, "library-version", "The library needs one pinned package version, not a range or wildcard.");
            if (string.IsNullOrWhiteSpace(library.License) && string.IsNullOrWhiteSpace(library.LicenseUrl))
                Add(issues, id, "library-license", "The library needs a licence (SPDX expression or LicenseRef) or a licence URL.");
            if (library.Restriction is null)
                Add(issues, id, "library-restriction", "The library needs an explicit restriction class: open, publish-restricted, acceptance-required or excluded.");
            else if (library.Restriction != BenchmarkLibraryRestriction.Open && string.IsNullOrWhiteSpace(library.Reason))
                Add(issues, id, "library-reason", $"A {Format(library.Restriction)} library needs a reason.");
        }

        var comparisonIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkComparisonDefinition comparison in document.Comparisons ?? Array.Empty<BenchmarkComparisonDefinition>())
        {
            if (comparison is null)
                continue;
            string id = comparison.ComparisonId?.Trim() ?? string.Empty;
            string subject = id.Length == 0 ? "comparison" : id;
            string? idProblem = BenchmarkEvidenceIdentity.DescribeComparisonIdProblem(id);
            if (idProblem is not null)
                Add(issues, subject, "comparison-id", idProblem);
            else if (!comparisonIds.Add(id))
                Add(issues, subject, "duplicate-comparison", "The comparison is declared more than once.");
            if (string.IsNullOrWhiteSpace(comparison.Area))
                Add(issues, subject, "comparison-area", "The comparison needs an area.");
            if (string.IsNullOrWhiteSpace(comparison.Title))
                Add(issues, subject, "comparison-title", "The comparison needs a title.");
            string[] comparisonLibraries = comparison.Libraries ?? Array.Empty<string>();
            if (comparisonLibraries.Length == 0)
                Add(issues, subject, "comparison-libraries", "The comparison needs at least one library.");
            foreach (string library in comparisonLibraries.Where(value => !libraries.ContainsKey(value?.Trim() ?? string.Empty)))
                Add(issues, subject, "undeclared-library", $"Library '{library}' is not in the library policy.");
            if (string.IsNullOrWhiteSpace(comparison.Subject) ||
                !comparisonLibraries.Contains(comparison.Subject.Trim(), StringComparer.OrdinalIgnoreCase))
                Add(issues, subject, "comparison-subject", "The comparison subject must be one of its libraries.");
            if ((comparison.ExpectedPlatforms ?? Array.Empty<string>()).Length == 0)
                Add(issues, subject, "comparison-platforms", "The comparison needs expected platforms.");
            foreach (string framework in (comparison.Frameworks ?? Array.Empty<string>()).Where(value => !BenchmarkEvidenceIdentity.IsTargetFramework(value)))
                Add(issues, subject, "comparison-framework", $"'{framework}' is not a target framework moniker.");
            var axisNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BenchmarkComparisonAxis axis in comparison.Axes ?? Array.Empty<BenchmarkComparisonAxis>())
            {
                if (axis is null || string.IsNullOrWhiteSpace(axis.Name) || !axisNames.Add(axis.Name.Trim()))
                    Add(issues, subject, "comparison-axis", "Axes need unique names.");
                else if ((axis.Values ?? Array.Empty<string>()).Length == 0)
                    Add(issues, subject, "comparison-axis", $"Axis '{axis.Name}' needs at least one value.");
                else if (axis.Values!.Any(string.IsNullOrWhiteSpace))
                    Add(issues, subject, "comparison-axis", $"Axis '{axis.Name}' values can't be blank.");
            }
            if (comparisonLibraries.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != comparisonLibraries.Length)
                Add(issues, subject, "duplicate-library-reference", "The comparison lists a library more than once (library ids ignore case).");
            string[] cases = comparison.Cases ?? Array.Empty<string>();
            if (cases.Any(string.IsNullOrWhiteSpace))
                Add(issues, subject, "comparison-case", "Case names can't be blank.");
            if (cases.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != cases.Count(value => !string.IsNullOrWhiteSpace(value)))
                Add(issues, subject, "duplicate-case", "Declared cases must be unique.");
            ValidateExplicitCells(issues, subject, comparison);
            if (comparison.Eligibility is { Publishable: false } && string.IsNullOrWhiteSpace(comparison.Eligibility.Reason))
                Add(issues, subject, "eligibility-reason", "A comparison declared not publishable needs a reason.");
        }

        return issues.ToArray();
    }

    /// <summary>Why a comparison without declared cases or cells is not published.</summary>
    public const string NoCasesReason = "Declare the comparison's cases (or cells) to publish it: without them a run can't be checked for missing combinations.";

    /// <summary>
    /// Derives a comparison's eligibility from its declaration. An author's own exclusion always wins, a
    /// single-library comparison is never publishable, and neither is one without declared cases or cells. Restricted libraries do not make a comparison
    /// ineligible: the publish policy drops their cells and publishes the rest.
    /// </summary>
    /// <param name="comparison">Comparison declaration.</param>
    /// <param name="libraries">Library policy.</param>
    /// <param name="policy">Publish policy.</param>
    /// <returns>Derived eligibility.</returns>
    public BenchmarkEligibility DeriveEligibility(
        BenchmarkComparisonDefinition comparison,
        IReadOnlyCollection<BenchmarkLibraryDeclaration> libraries,
        BenchmarkPublishPolicy policy)
    {
        if (comparison is null) throw new ArgumentNullException(nameof(comparison));
        if (libraries is null) throw new ArgumentNullException(nameof(libraries));
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        if (comparison.Eligibility is { Publishable: false })
            return new BenchmarkEligibility { Publishable = false, Reason = comparison.Eligibility.Reason };
        if ((comparison.Libraries ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            return new BenchmarkEligibility { Publishable = false, Reason = "Single-library comparison: there is nothing to compare against." };
        // Without declared cases or cells, a lane can't be checked for completeness, so it is held back rather than
        // published as if it were complete. It is still recorded and shown.
        if (DeclaredCaseNames(comparison).Length == 0)
            return new BenchmarkEligibility { Publishable = false, Reason = NoCasesReason };
        return new BenchmarkEligibility { Publishable = true };
    }

    /// <summary>
    /// Selects the libraries a runner may execute. Selection is generic: include and exclude lists plus
    /// explicit per-library acceptance. Acceptance is never assumed.
    /// </summary>
    /// <param name="libraries">Library policy.</param>
    /// <param name="include">Library ids to run; empty runs every selectable library.</param>
    /// <param name="exclude">Library ids not to run.</param>
    /// <param name="accept">Library ids whose licence the person running the benchmark explicitly accepted.</param>
    /// <returns>Selection with the reason for every skipped library.</returns>
    public BenchmarkLibrarySelection SelectLibraries(
        IReadOnlyCollection<BenchmarkLibraryDeclaration> libraries,
        IEnumerable<string>? include = null,
        IEnumerable<string>? exclude = null,
        IEnumerable<string>? accept = null)
    {
        if (libraries is null) throw new ArgumentNullException(nameof(libraries));
        string[] includeIds = Normalize(include);
        string[] excludeIds = Normalize(exclude);
        string[] acceptIds = Normalize(accept);
        string[] unknown = includeIds.Concat(excludeIds).Concat(acceptIds)
            .Where(id => !libraries.Any(library => string.Equals(library.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"Unknown benchmark libraries: {string.Join(", ", unknown)}. Add them to the library policy first.");

        var selected = new List<string>();
        var accepted = new List<string>();
        var skipped = new List<BenchmarkLibrarySkip>();
        foreach (BenchmarkLibraryDeclaration library in libraries.OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase))
        {
            string? reason =
                library.Restriction is null ? "The library policy does not declare its restriction class." :
                library.Restriction == BenchmarkLibraryRestriction.Excluded ? $"Excluded by the library policy: {library.Reason}" :
                excludeIds.Contains(library.Id, StringComparer.OrdinalIgnoreCase) ? "Excluded for this run." :
                includeIds.Length > 0 && !includeIds.Contains(library.Id, StringComparer.OrdinalIgnoreCase) ? "Not included in this run." :
                library.Restriction == BenchmarkLibraryRestriction.AcceptanceRequired &&
                !acceptIds.Contains(library.Id, StringComparer.OrdinalIgnoreCase)
                    ? $"Requires explicit licence acceptance ({library.Reason}). Accept it for this run to include it."
                    : null;
            if (reason is not null)
            {
                skipped.Add(new BenchmarkLibrarySkip { Id = library.Id, Reason = reason });
                continue;
            }
            selected.Add(library.Id);
            if (library.Restriction == BenchmarkLibraryRestriction.AcceptanceRequired)
                accepted.Add(library.Id);
        }

        return new BenchmarkLibrarySelection
        {
            Selected = selected.ToArray(),
            Accepted = accepted.ToArray(),
            Skipped = skipped.ToArray()
        };
    }

    /// <summary>
    /// Compares the cases, libraries, axis values and their combinations in a result with the comparison
    /// declaration. Any finding means the declaration drifted from the benchmark code: a case, library, axis value
    /// or combination that ran without being declared, or a declared one with no cell at all. A case that ran and
    /// failed is a failed cell, not a missing one, and is never reported here.
    /// </summary>
    /// <param name="comparison">Comparison declaration.</param>
    /// <param name="libraries">Library policy.</param>
    /// <param name="result">Benchmark result.</param>
    /// <returns>Drift findings.</returns>
    public BenchmarkDeclarationIssue[] CheckDrift(
        BenchmarkComparisonDefinition comparison,
        IReadOnlyCollection<BenchmarkLibraryDeclaration> libraries,
        BenchmarkRunResult result)
    {
        if (comparison is null) throw new ArgumentNullException(nameof(comparison));
        if (result is null) throw new ArgumentNullException(nameof(result));
        BenchmarkEvidenceCell[] cells = BenchmarkEvidenceCellBuilder.Build(result, comparison.ComparisonId, BenchmarkPublishPolicy.CreateDefault());
        return CheckDrift(comparison, libraries, cells);
    }

    internal static BenchmarkDeclarationIssue[] CheckDrift(
        BenchmarkComparisonDefinition comparison,
        IReadOnlyCollection<BenchmarkLibraryDeclaration> libraries,
        IReadOnlyCollection<BenchmarkEvidenceCell> cells)
    {
        var issues = new List<BenchmarkDeclarationIssue>();
        string subject = comparison.ComparisonId;
        // With explicit cells, the cases and axis values that must have run come from the cells that apply to the
        // libraries that ran; values only another library supports are not missing when that library did not run.
        string[] declaredCases = DeclaredCaseNames(comparison);
        if (declaredCases.Length > 0)
        {
            string[] measuredCases = cells.Select(cell => cell.Case).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string unexpected in measuredCases.Where(value => !declaredCases.Contains(value, StringComparer.OrdinalIgnoreCase)).OrderBy(value => value, StringComparer.Ordinal))
                Add(issues, subject, "unexpected-case", $"Case '{unexpected}' ran but is not declared.");
            foreach (string missing in RequiredCaseNames(comparison, cells, declaredCases).Where(value => !measuredCases.Contains(value, StringComparer.OrdinalIgnoreCase)).OrderBy(value => value, StringComparer.Ordinal))
                Add(issues, subject, "missing-case", $"Declared case '{missing}' did not run.");
        }

        string[] declaredLibraries = comparison.Libraries ?? Array.Empty<string>();
        foreach (string library in cells.Select(cell => cell.Library).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(library))
                Add(issues, subject, "missing-library-identity", "Some cells have no library identity. Set it in the importer or runner.");
            else if (!declaredLibraries.Contains(library, StringComparer.OrdinalIgnoreCase))
                Add(issues, subject, "unexpected-library", $"Library '{library}' ran but is not declared for this comparison.");
            else if (!libraries.Any(value => string.Equals(value.Id, library, StringComparison.OrdinalIgnoreCase)))
                Add(issues, subject, "undeclared-library", $"Library '{library}' is not in the library policy.");
        }

        foreach (BenchmarkComparisonAxis axis in comparison.Axes ?? Array.Empty<BenchmarkComparisonAxis>())
        {
            string[] declaredValues = (axis.Values ?? Array.Empty<string>()).Select(value => (value ?? string.Empty).Trim()).ToArray();
            int withoutAxis = cells.Count(cell => !cell.Parameters.TryGetValue(axis.Name, out string? value) || value is null);
            if (withoutAxis > 0)
                Add(issues, subject, "missing-axis-parameter", $"{withoutAxis} cell(s) do not record axis '{axis.Name}'.");
            string[] measured = cells
                .Select(cell => cell.Parameters.TryGetValue(axis.Name, out string? value) ? value : null)
                .Where(value => value is not null)
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (string value in measured.Where(value => !declaredValues.Contains(value, StringComparer.OrdinalIgnoreCase)).OrderBy(value => value, StringComparer.Ordinal))
                Add(issues, subject, "unexpected-axis-value", $"Axis '{axis.Name}' value '{value}' ran but is not declared.");
            foreach (string value in RequiredAxisValues(comparison, axis, cells, declaredValues).Where(value => !measured.Contains(value, StringComparer.OrdinalIgnoreCase)).OrderBy(value => value, StringComparer.Ordinal))
                Add(issues, subject, "missing-axis-value", $"Declared axis '{axis.Name}' value '{value}' did not run.");
        }

        CheckCombinations(issues, subject, comparison, cells);
        return issues.ToArray();
    }

    internal static void ValidateOrThrow(BenchmarkDeclarationDocument document)
    {
        BenchmarkDeclarationIssue[] issues = new BenchmarkDeclarationService().Validate(document);
        if (issues.Length > 0)
        {
            throw new InvalidOperationException(
                "Benchmark declarations are invalid: " + string.Join(" ", issues.Select(issue => issue.ToString())));
        }
    }

    private static string[] Normalize(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static string Format(BenchmarkLibraryRestriction? restriction)
        => restriction is { } value ? BenchmarkKebabCaseEnumConverter<BenchmarkLibraryRestriction>.Format(value) : "unclassified";

    private static void Add(List<BenchmarkDeclarationIssue> issues, string subject, string code, string message)
        => issues.Add(new BenchmarkDeclarationIssue { Subject = subject, Code = code, Message = message });
}
