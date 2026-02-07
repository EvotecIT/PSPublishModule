namespace PowerForge.Web;

/// <summary>Resolved pagination state for the currently rendered page.</summary>
public sealed class PaginationRuntime
{
    /// <summary>Current page number (1-based).</summary>
    public int Page { get; init; } = 1;
    /// <summary>Total number of available pages.</summary>
    public int TotalPages { get; init; } = 1;
    /// <summary>Configured page size (items per page).</summary>
    public int PageSize { get; init; }
    /// <summary>Total number of items before pagination slicing.</summary>
    public int TotalItems { get; init; }
    /// <summary>True when a previous page exists.</summary>
    public bool HasPrevious { get; init; }
    /// <summary>True when a next page exists.</summary>
    public bool HasNext { get; init; }
    /// <summary>URL of the previous page, or empty when unavailable.</summary>
    public string PreviousUrl { get; init; } = string.Empty;
    /// <summary>URL of the next page, or empty when unavailable.</summary>
    public string NextUrl { get; init; } = string.Empty;
    /// <summary>URL of the first page.</summary>
    public string FirstUrl { get; init; } = string.Empty;
    /// <summary>URL of the last page.</summary>
    public string LastUrl { get; init; } = string.Empty;
    /// <summary>Route segment used for pagination URLs.</summary>
    public string PathSegment { get; init; } = "page";
}
