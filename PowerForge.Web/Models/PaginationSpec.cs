namespace PowerForge.Web;

/// <summary>Global pagination configuration for list-like pages.</summary>
public sealed class PaginationSpec
{
    /// <summary>When false, disables generated pagination pages.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Default page size used when collection/taxonomy page size is not set.</summary>
    public int DefaultPageSize { get; set; }

    /// <summary>URL segment used for generated pagination routes (for example /blog/page/2/).</summary>
    public string PathSegment { get; set; } = "page";
}
