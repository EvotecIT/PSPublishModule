namespace PowerForge.Web;

/// <summary>
/// Exposes a benchmark evidence catalog to templates as a comparison view, under <c>data.&lt;Name&gt;</c>.
/// </summary>
/// <remarks>
/// The view is built at build time by <see cref="PowerForge.BenchmarkComparisonViewBuilder"/> from the catalog
/// and the result files beside it: every comparison, every run, each contender and case side by side, with
/// relative times and the subject library against each competitor. Templates render it as plain HTML.
/// </remarks>
public sealed class BenchmarkEvidenceDataSpec
{
    /// <summary>Data key the view is published under, such as "benchmarks".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Path to the evidence catalog (index.json), relative to the site root.</summary>
    public string Catalog { get; set; } = string.Empty;
}

/// <summary>Template engine limits.</summary>
public sealed class TemplateLimitsSpec
{
    /// <summary>
    /// Maximum loop iterations Scriban allows while rendering one template; 0 removes the limit.
    /// Scriban's default is 1000, which data-heavy pages such as benchmark tables exceed.
    /// </summary>
    public int? LoopLimit { get; set; }
}
