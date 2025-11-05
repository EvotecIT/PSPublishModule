namespace PowerForge;

/// <summary>
/// Result of formatting a single file.
/// </summary>
public sealed class FormatterResult
{
    /// <summary>Path of the file that was processed.</summary>
    public string Path { get; }
    /// <summary>True if the file content was modified.</summary>
    public bool Changed { get; }
    /// <summary>Additional information (e.g., Unchanged, Error).</summary>
    public string Message { get; }
    /// <summary>
    /// Creates a new formatter result instance.
    /// </summary>
    public FormatterResult(string path, bool changed, string message)
    { Path = path; Changed = changed; Message = message; }
}

/// <summary>
/// Formats PowerShell scripts according to configured rules.
/// </summary>
public interface IFormatter
{
    /// <summary>
    /// Formats the provided files. Implementations should be resilient and return
    /// a result for each input file.
    /// </summary>
    /// <param name="files">Files to format.</param>
    /// <param name="timeout">Optional timeout for the entire formatting run.</param>
    IReadOnlyList<FormatterResult> FormatFiles(IEnumerable<string> files, TimeSpan? timeout = null);
    /// <summary>
    /// Formats files with an optional settings JSON payload compatible with PSScriptAnalyzer settings.
    /// </summary>
    IReadOnlyList<FormatterResult> FormatFilesWithSettings(IEnumerable<string> files, string? settingsJson, TimeSpan? timeout = null);
}
