using System;

namespace PowerForge;

/// <summary>
/// Selects which JSON renderer to use for fenced code blocks.
/// </summary>
public enum JsonRendererPreference
{
    /// <summary>
    /// Automatically choose the best available renderer (currently <see cref="Highlighted"/>).
    /// </summary>
    Auto = 0,
    /// <summary>
    /// Show the JSON as written, with syntax highlighting.
    /// </summary>
    Highlighted = 1,
    /// <summary>
    /// Former name of <see cref="Highlighted"/>.
    /// </summary>
    [Obsolete("Use Highlighted. JSON is no longer rendered by Spectre.Console.")]
    Spectre = Highlighted,
    /// <summary>
    /// Reformat JSON via System.Text.Json serialization with minimal styling.
    /// </summary>
    System = 2
}
