namespace PowerForge.Web;

public sealed class CollectionSpec
{
    public string Name { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string? DefaultLayout { get; set; }
    public string[] Include { get; set; } = Array.Empty<string>();
    public string[] Exclude { get; set; } = Array.Empty<string>();
    public string? SortBy { get; set; }
    public SortOrder? SortOrder { get; set; }
}
