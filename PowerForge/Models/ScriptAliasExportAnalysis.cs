using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Describes statically resolved script aliases and whether the analyzed alias set is complete.
/// </summary>
public sealed class ScriptAliasExportAnalysis
{
    /// <summary>
    /// Creates a script alias analysis result.
    /// </summary>
    public ScriptAliasExportAnalysis(IEnumerable<string>? aliases, bool isComplete)
    {
        Aliases = (aliases ?? Array.Empty<string>())
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IsComplete = isComplete;
    }

    /// <summary>
    /// Gets the statically resolved module-scope alias names.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// Gets a value indicating whether every encountered module-scope alias declaration was resolved.
    /// </summary>
    public bool IsComplete { get; }
}
