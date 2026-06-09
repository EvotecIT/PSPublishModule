namespace PowerForge.Web;

/// <summary>Aggregated taxonomy index metadata for the current taxonomy page.</summary>
public sealed class TaxonomyIndexRuntime
{
    /// <summary>Taxonomy key (for example tags or categories).</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Language code for this taxonomy page.</summary>
    public string Language { get; init; } = string.Empty;
    /// <summary>Total number of discovered terms.</summary>
    public int TotalTerms { get; init; }
    /// <summary>Total number of content items across all terms.</summary>
    public int TotalItems { get; init; }
    /// <summary>Per-term metadata entries.</summary>
    public TaxonomyTermRuntime[] Terms { get; init; } = Array.Empty<TaxonomyTermRuntime>();
}

/// <summary>Metadata for a single taxonomy term.</summary>
public sealed class TaxonomyTermRuntime
{
    /// <summary>Term display name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Term route URL.</summary>
    public string Url { get; init; } = string.Empty;
    /// <summary>Number of matching content items.</summary>
    public int Count { get; init; }
    /// <summary>Most recent publish date across matching items (UTC).</summary>
    public DateTime? LatestDateUtc { get; init; }
}

/// <summary>Summary metadata for the currently rendered taxonomy term page.</summary>
public sealed class TaxonomyTermSummaryRuntime
{
    /// <summary>Term display name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Number of matching content items.</summary>
    public int Count { get; init; }
    /// <summary>Most recent publish date across matching items (UTC).</summary>
    public DateTime? LatestDateUtc { get; init; }
}
