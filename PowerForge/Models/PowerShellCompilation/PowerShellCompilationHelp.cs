using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Authored comment-based help retained for a compiled PowerShell command.
/// </summary>
public sealed class PowerShellCompilationHelp
{
    /// <summary>Short command summary.</summary>
    public string Synopsis { get; set; } = string.Empty;

    /// <summary>Full command description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Free-form command notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Parameter help keyed by PowerShell parameter name.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Authored examples in declaration order.</summary>
    public string[] Examples { get; set; } = Array.Empty<string>();

    /// <summary>Related link text or URI values.</summary>
    public string[] Links { get; set; } = Array.Empty<string>();

    /// <summary>Authored input type descriptions.</summary>
    public string[] Inputs { get; set; } = Array.Empty<string>();

    /// <summary>Authored output type descriptions.</summary>
    public string[] Outputs { get; set; } = Array.Empty<string>();
}
