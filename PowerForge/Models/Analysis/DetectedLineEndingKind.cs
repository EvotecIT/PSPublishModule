namespace PowerForge;

/// <summary>
/// Line ending kinds detected in project files.
/// </summary>
public enum DetectedLineEndingKind
{
    /// <summary>No line endings were found (empty file or single-line file).</summary>
    None,
    /// <summary>Carriage Return only (\r).</summary>
    CR,
    /// <summary>Line Feed only (\n).</summary>
    LF,
    /// <summary>Carriage Return + Line Feed (\r\n).</summary>
    CRLF,
    /// <summary>More than one line ending kind was found.</summary>
    Mixed,
    /// <summary>Detection failed (e.g., file could not be read).</summary>
    Error
}

