namespace PSMaintenance;

/// <summary>
/// Controls how examples are rendered.
/// </summary>
public enum ExamplesLayout
{
    /// <summary>
    /// Code first (fenced), then remarks as paragraphs. Mirrors typical PowerShell help layout.
    /// </summary>
    MamlDefault,
    /// <summary>
    /// Remarks paragraphs first (as introduction), then the code block.
    /// </summary>
    ProseFirst,
    /// <summary>
    /// Render both code and remarks inside a single fenced code block.
    /// </summary>
    AllAsCode
}

