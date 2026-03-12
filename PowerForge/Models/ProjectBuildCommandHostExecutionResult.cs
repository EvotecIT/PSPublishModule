namespace PowerForge;

/// <summary>
/// Result returned by the PowerShell-host fallback for project build plan/build execution.
/// </summary>
public sealed class ProjectBuildCommandHostExecutionResult
{
    /// <summary>Exit code returned by the PowerShell host.</summary>
    public int ExitCode { get; set; }

    /// <summary>Total execution duration.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Captured standard output.</summary>
    public string StandardOutput { get; set; } = string.Empty;

    /// <summary>Captured standard error.</summary>
    public string StandardError { get; set; } = string.Empty;

    /// <summary>PowerShell executable that was used.</summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>Whether the command exited successfully.</summary>
    public bool Succeeded => ExitCode == 0;
}
