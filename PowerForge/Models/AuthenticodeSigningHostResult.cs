namespace PowerForge;

/// <summary>
/// Result returned by the shared Authenticode signing host service.
/// </summary>
public sealed class AuthenticodeSigningHostResult
{
    /// <summary>
    /// Exit code returned by the PowerShell process.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Execution duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Captured standard output.
    /// </summary>
    public string StandardOutput { get; set; } = string.Empty;

    /// <summary>
    /// Captured standard error.
    /// </summary>
    public string StandardError { get; set; } = string.Empty;

    /// <summary>
    /// PowerShell executable used for the run.
    /// </summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>
    /// True when <see cref="ExitCode"/> is zero.
    /// </summary>
    public bool Succeeded => ExitCode == 0;
}
