namespace PowerForge;

/// <summary>
/// High-level console rendering mode selection for hosts.
/// Auto picks Standard when interactive, otherwise Ansi.
/// - Standard: live region rendering (progress/spinners).
/// - Ansi: top-to-bottom plain output (CI/term-friendly), no live regions.
/// </summary>
public enum ConsoleView
{
    /// <summary>Choose mode automatically (Standard for interactive terminals, otherwise Ansi).</summary>
    Auto = 0,
    /// <summary>Live region rendering (spinners/progress).</summary>
    Standard = 1,
    /// <summary>Plain, streaming output without live regions (CI-friendly).</summary>
    Ansi = 2,
}

