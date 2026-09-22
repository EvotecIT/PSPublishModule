namespace PowerForge;

/// <summary>Execution options for one declared executable target. Dependency acquisition remains explicit.</summary>
public sealed class PowerShellCompilationProjectRunOptions
{
    /// <summary>Target name; required when the project declares more than one target.</summary>
    public string? TargetName { get; set; }

    /// <summary>Exact application arguments, without shell parsing or interpolation.</summary>
    public string[] Arguments { get; set; } = Array.Empty<string>();

    /// <summary>Capture stdout in the result instead of inheriting the caller's stdout handle.</summary>
    public bool CaptureOutput { get; set; }

    /// <summary>Capture stderr in the result instead of inheriting the caller's stderr handle.</summary>
    public bool CaptureError { get; set; }

    /// <summary>Polling interval for content changes during watch; defaults to 500 milliseconds.</summary>
    public int PollIntervalMilliseconds { get; set; } = 500;

    /// <summary>Time without observed input changes before a watched rebuild; defaults to 250 milliseconds.</summary>
    public int DebounceMilliseconds { get; set; } = 250;
}

/// <summary>One build-and-execute attempt. An unsuccessful build never has an executed process.</summary>
public sealed class PowerShellCompilationProjectRunResult
{
    /// <summary>Outcome of the current build, including actionable target diagnostics.</summary>
    public PowerShellCompilationProjectResult Build { get; set; } = new();

    /// <summary>Observed execution result, or null when building or integrity validation failed.</summary>
    public ProcessRunResult? Process { get; set; }

    /// <summary>Build/validation failure, or an empty string after a successful launch.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Application or process-runner exit code, or 1 when no execution result is available.</summary>
    public int ExitCode => Process?.ExitCode ?? 1;
}

/// <summary>Progress notification for source-first development; application streams remain separate.</summary>
public sealed class PowerShellCompilationProjectRunEvent
{
    /// <summary>Lifecycle state: building, starting, running, exited, failed, or changed.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Human-readable progress or actionable failure.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Completed attempt, when this is an exited or failed notification.</summary>
    public PowerShellCompilationProjectRunResult? Result { get; set; }
}
