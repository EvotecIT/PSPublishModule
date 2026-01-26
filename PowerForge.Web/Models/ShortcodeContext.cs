namespace PowerForge.Web;

public sealed class ShortcodeContext
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Attrs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public object? Data { get; set; }
}
