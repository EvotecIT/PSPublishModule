namespace PowerForge.Web;

/// <summary>Context passed to shortcode renderers.</summary>
public sealed class ShortcodeContext
{
    /// <summary>Shortcode name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Attribute map supplied in the shortcode.</summary>
    public Dictionary<string, string> Attrs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional data payload for the shortcode.</summary>
    public object? Data { get; set; }
}
