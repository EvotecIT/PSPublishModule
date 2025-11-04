namespace PSMaintenance;

/// <summary>
/// Controls how Examples are sourced for command help rendering.
/// </summary>
public enum ExamplesMode
{
    /// <summary>
    /// Try <see cref="Raw"/> first (EXAMPLES section from Get-Help text). If not found, fall back to <see cref="Maml"/>.
    /// </summary>
    Auto,
    /// <summary>
    /// Parse the EXAMPLES section from the raw Get-Help text (Out-String) and render each example verbatim as a single code block.
    /// </summary>
    Raw,
    /// <summary>
    /// Use structured MAML help for examples (Code and Remarks), potentially affected by help engine whitespace normalization.
    /// </summary>
    Maml
}
