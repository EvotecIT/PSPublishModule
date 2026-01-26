namespace PowerForge.Web;

/// <summary>Configuration for edit‑on‑GitHub links.</summary>
public sealed class EditLinksSpec
{
    /// <summary>Enables edit links.</summary>
    public bool Enabled { get; set; }
    /// <summary>Template for edit URLs.</summary>
    public string? Template { get; set; }
    /// <summary>Optional base path for source files.</summary>
    public string? PathBase { get; set; }
}
