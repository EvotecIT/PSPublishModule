namespace PowerForge;

/// <summary>
/// Importance of an owner-facing pipeline note.
/// </summary>
public enum ModuleOwnerNoteSeverity
{
    /// <summary>Informational note.</summary>
    Info,
    /// <summary>Warning note that may need action.</summary>
    Warning
}

/// <summary>
/// Structured note shown to module owners in the final pipeline summary.
/// </summary>
public sealed class ModuleOwnerNote
{
    /// <summary>Short note title.</summary>
    public string Title { get; }

    /// <summary>Importance of the note.</summary>
    public ModuleOwnerNoteSeverity Severity { get; }

    /// <summary>Short summary of what happened.</summary>
    public string Summary { get; }

    /// <summary>Why the module owner should care.</summary>
    public string WhyItMatters { get; }

    /// <summary>Suggested next step for the module owner.</summary>
    public string NextStep { get; }

    /// <summary>Optional highlights shown as bullets.</summary>
    public string[] Details { get; }

    /// <summary>
     /// Creates a new owner note.
     /// </summary>
    public ModuleOwnerNote(
        string title,
        ModuleOwnerNoteSeverity severity,
        string? summary = null,
        string? whyItMatters = null,
        string? nextStep = null,
        string[]? details = null)
    {
        Title = title ?? string.Empty;
        Severity = severity;
        Summary = summary ?? string.Empty;
        WhyItMatters = whyItMatters ?? string.Empty;
        NextStep = nextStep ?? string.Empty;
        Details = details ?? System.Array.Empty<string>();
    }
}
