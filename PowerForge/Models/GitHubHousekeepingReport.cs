using System;

namespace PowerForge;

/// <summary>
/// Portable report payload for a GitHub housekeeping run.
/// </summary>
public sealed class GitHubHousekeepingReport
{
    /// <summary>
    /// Report schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Command identifier that produced the report.
    /// </summary>
    public string Command { get; set; } = "github.housekeeping";

    /// <summary>
    /// Whether the housekeeping run succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Process exit code associated with the report.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Error text when the run failed before producing a result.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// UTC timestamp when the report was generated.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Housekeeping result payload when available.
    /// </summary>
    public GitHubHousekeepingResult? Result { get; set; }
}
