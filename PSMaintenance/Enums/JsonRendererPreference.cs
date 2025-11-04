namespace PSMaintenance;

/// <summary>
/// Selects which JSON renderer to use for fenced code blocks.
/// </summary>
public enum JsonRendererPreference
{
    /// <summary>
    /// Automatically choose the best available renderer (Spectre for styling, fallback to System.Text.Json on errors).
    /// </summary>
    Auto,
    /// <summary>
    /// Force Spectre.Console.Json to render JSON with colors and formatting.
    /// </summary>
    Spectre,
    /// <summary>
    /// Render JSON via System.Text.Json serialization with minimal styling.
    /// </summary>
    System
}
