namespace PowerForge.Web;

public sealed class SearchIndexEntry
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
}
