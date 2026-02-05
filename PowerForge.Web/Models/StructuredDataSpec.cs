namespace PowerForge.Web;

/// <summary>Structured data configuration (JSON-LD).</summary>
public sealed class StructuredDataSpec
{
    /// <summary>When true, emit structured data.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>When true, emit breadcrumb structured data.</summary>
    public bool Breadcrumbs { get; set; } = true;
}
