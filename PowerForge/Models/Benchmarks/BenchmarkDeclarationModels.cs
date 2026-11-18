namespace PowerForge;

/// <summary>
/// Licence restriction class of a benchmarked library. It decides whether a runner may select the
/// library and whether its cells can be published.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(BenchmarkKebabCaseEnumConverter<BenchmarkLibraryRestriction>))]
public enum BenchmarkLibraryRestriction
{
    /// <summary>Licence allows running and publishing benchmark results.</summary>
    Open,

    /// <summary>Can run locally; results are not publishable without the licensor's permission.</summary>
    PublishRestricted,

    /// <summary>Needs an explicit licence acceptance or key from the person running it. Never accepted on their behalf.</summary>
    AcceptanceRequired,

    /// <summary>Never selected or published.</summary>
    Excluded
}

/// <summary>
/// Benchmark declaration document (schema 5): the library policy and the comparisons a project measures.
/// Generate it from the benchmark code where possible so it cannot drift from what actually runs.
/// </summary>
public sealed class BenchmarkDeclarationDocument
{
    /// <summary>Declaration schema version. Only schema 5 is accepted.</summary>
    public int SchemaVersion { get; set; } = BenchmarkEvidenceCatalogService.CurrentSchemaVersion;

    /// <summary>Library policy: one entry per benchmarked library.</summary>
    public BenchmarkLibraryDeclaration[] Libraries { get; set; } = Array.Empty<BenchmarkLibraryDeclaration>();

    /// <summary>Declared comparisons.</summary>
    public BenchmarkComparisonDefinition[] Comparisons { get; set; } = Array.Empty<BenchmarkComparisonDefinition>();
}

/// <summary>One library in the library policy.</summary>
public sealed class BenchmarkLibraryDeclaration
{
    /// <summary>Library identifier used by result cells and comparison declarations, such as OfficeIMO or EPPlus.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Package identifier, such as a NuGet package id.</summary>
    public string Package { get; set; } = string.Empty;

    /// <summary>Pinned package version measured by the runner.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Licence as an SPDX expression, or LicenseRef-* for non-SPDX licences.</summary>
    public string License { get; set; } = string.Empty;

    /// <summary>URL of the licence text.</summary>
    public string LicenseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Restriction class. It must be declared; a library without one is neither selected for runs nor published.
    /// </summary>
    public BenchmarkLibraryRestriction? Restriction { get; set; }

    /// <summary>True when the library's class is one the policy publishes.</summary>
    /// <param name="policy">Publish policy.</param>
    /// <returns>True when its cells may be published.</returns>
    public bool IsPublishableUnder(BenchmarkPublishPolicy policy)
        => Restriction is { } restriction && (policy ?? throw new ArgumentNullException(nameof(policy))).PublishableRestrictions.Contains(restriction);

    /// <summary>Why the library has its restriction class; required for every class except open.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A workload declaration independent of whether evidence has been collected.</summary>
public sealed class BenchmarkComparisonDefinition
{
    /// <summary>Stable identifier shared with measured evidence lanes. It never encodes framework or affinity.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Product area identifier, such as pdf or excel.</summary>
    public string Area { get; set; } = string.Empty;

    /// <summary>Group within the area, such as read or write.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Human-readable equivalent-work comparison title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Library the comparison is about; it must be one of <see cref="Libraries"/>.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Identifiers of the libraries exercised by the comparison.</summary>
    public string[] Libraries { get; set; } = Array.Empty<string>();

    /// <summary>Scenario matrix axes, such as rows = 1000, 100000 or mode = streaming, dom.</summary>
    public BenchmarkComparisonAxis[] Axes { get; set; } = Array.Empty<BenchmarkComparisonAxis>();

    /// <summary>
    /// Case names the benchmark code defines. When present, results with unexpected or missing cases are
    /// reported as declaration drift.
    /// </summary>
    public string[] Cases { get; set; } = Array.Empty<string>();

    /// <summary>Platforms expected for this comparison.</summary>
    public string[] ExpectedPlatforms { get; set; } = { "windows", "linux", "macos" };

    /// <summary>Target frameworks the comparison runs on, such as net8.0 and net10.0.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Whether the comparison can be published. Catalogs derive it from the declaration and the libraries'
    /// restriction classes; an author can only make it stricter.
    /// </summary>
    public BenchmarkEligibility Eligibility { get; set; } = new();
}

/// <summary>One axis of a comparison's scenario matrix.</summary>
public sealed class BenchmarkComparisonAxis
{
    /// <summary>Parameter name as it appears on result cells.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Declared values.</summary>
    public string[] Values { get; set; } = Array.Empty<string>();
}

/// <summary>Publication eligibility of a comparison.</summary>
public sealed class BenchmarkEligibility
{
    /// <summary>True when the comparison can appear as publishable evidence.</summary>
    public bool Publishable { get; set; } = true;

    /// <summary>Why it cannot be published.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Result of selecting libraries for a run from the library policy.</summary>
public sealed class BenchmarkLibrarySelection
{
    /// <summary>Library identifiers the runner may execute.</summary>
    public string[] Selected { get; set; } = Array.Empty<string>();

    /// <summary>Library identifiers whose licence acceptance was given explicitly for this run.</summary>
    public string[] Accepted { get; set; } = Array.Empty<string>();

    /// <summary>Libraries that will not run, with the reason.</summary>
    public BenchmarkLibrarySkip[] Skipped { get; set; } = Array.Empty<BenchmarkLibrarySkip>();
}

/// <summary>A library left out of a run.</summary>
public sealed class BenchmarkLibrarySkip
{
    /// <summary>Library identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Why it does not run.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Finding from validating declarations or checking a result against them.</summary>
public sealed class BenchmarkDeclarationIssue
{
    /// <summary>Comparison or library the finding is about.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Stable finding code, such as unexpected-case.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string ToString() => $"{Subject}: {Message}";
}
