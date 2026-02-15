using System;

namespace PowerForge.Web;

/// <summary>Result payload for navigation export.</summary>
public sealed class WebNavExportResult
{
    /// <summary>Overall success status.</summary>
    public bool Success { get; set; }

    /// <summary>Resolved output path.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>True when the output file content changed.</summary>
    public bool Changed { get; set; }

    /// <summary>Optional informational message.</summary>
    public string? Message { get; set; }

    /// <summary>UTC timestamp for when the export ran.</summary>
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

