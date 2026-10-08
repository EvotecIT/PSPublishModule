namespace PowerForge;

/// <summary>A bounded foreground validation session using one exact Apple simulator.</summary>
public sealed class AppleSimulatorSessionRequest
{
    /// <summary>Exact simulator UUID; names, 'booted', and 'all' are not accepted.</summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>Owning chat or CI run reference, without credentials.</summary>
    public string Owner { get; set; } = string.Empty;
    /// <summary>Explicit simctl device set; null selects Apple's default user device set.</summary>
    public string? DeviceSetPath { get; set; }
    /// <summary>Shared machine-local coordination directory; null uses the default for this user.</summary>
    public string? StateRoot { get; set; }
    /// <summary>Foreground command with a finite positive timeout. Descendants in its owned process scope are released.</summary>
    public ProcessRunRequest? Command { get; set; }
    /// <summary>Maximum duration of each simulator discovery, boot, and readiness command.</summary>
    public TimeSpan SimulatorTimeout { get; set; } = TimeSpan.FromMinutes(2);
    /// <summary>Maximum duration of the entire independent shutdown and verification attempt.</summary>
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Compact persisted ownership evidence; excludes child arguments, environment, and captured output.</summary>
public sealed class AppleSimulatorSessionReceipt
{
    /// <summary>Receipt format version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Unique ownership session identifier.</summary>
    public string SessionId { get; set; } = string.Empty;
    /// <summary>Owning chat or CI run reference.</summary>
    public string Owner { get; set; } = string.Empty;
    /// <summary>Wrapper process identifier, paired with its start time to prevent PID-reuse confusion.</summary>
    public int OwnerProcessId { get; set; }
    /// <summary>Wrapper process start time in UTC.</summary>
    public DateTime OwnerProcessStartedUtc { get; set; }
    /// <summary>Receipt creation time.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }
    /// <summary>Final recording time; null means the wrapper did not finish its release path.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }
    /// <summary>Exact simulator UUID.</summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>Explicit device set passed to simctl; null selects the default.</summary>
    public string? DeviceSetPath { get; set; }
    /// <summary>Observed state before any boot command.</summary>
    public string InitialState { get; set; } = string.Empty;
    /// <summary>Whether a boot was requested after observing Shutdown.</summary>
    public bool BootRequested { get; set; }
    /// <summary>Whether simctl confirmed that this session's boot command succeeded.</summary>
    public bool BootSucceeded { get; set; }
    /// <summary>Validation process identifier when the command reached process start.</summary>
    public int? CommandProcessId { get; set; }
    /// <summary>Validation process start time paired with its PID; null before process start.</summary>
    public DateTime? CommandProcessStartedUtc { get; set; }
    /// <summary>Validation exit code when a process result was available.</summary>
    public int? CommandExitCode { get; set; }
    /// <summary>Observed state after release, or Unknown when discovery failed.</summary>
    public string FinalState { get; set; } = "Unknown";
    /// <summary>Whether owned shutdown was verified or a pre-existing booted session was preserved.</summary>
    public bool CleanupSucceeded { get; set; }
    /// <summary>Compact outcome: running, succeeded, failed, canceled, cleanup-incomplete, or acknowledged.</summary>
    public string Outcome { get; set; } = "running";
}

/// <summary>Validation outcome and separately visible cleanup evidence.</summary>
public sealed class AppleSimulatorSessionResult
{
    /// <summary>Final persisted receipt.</summary>
    public AppleSimulatorSessionReceipt Receipt { get; set; } = new();
    /// <summary>Exact persisted receipt location.</summary>
    public string ReceiptPath { get; set; } = string.Empty;
    /// <summary>Bounded command output, returned to the caller but never persisted in the ownership receipt.</summary>
    public ProcessRunResult? CommandResult { get; set; }
    /// <summary>Primary session diagnostic, separate from cleanup failure.</summary>
    public string? Error { get; set; }
    /// <summary>Shutdown or verification diagnostic.</summary>
    public string? CleanupError { get; set; }
    /// <summary>Whether the foreground command exited normally with code zero; bounded log truncation is informational.</summary>
    public bool ValidationSucceeded => CommandResult is { ExitCode: 0, TimedOut: false, StartFailed: false };
    /// <summary>True only when validation and release both succeeded.</summary>
    public bool Success => Error is null && ValidationSucceeded && Receipt.CleanupSucceeded;
    /// <summary>Preserves primary failure/cancellation codes; cleanup-only failure returns 1.</summary>
    public int ExitCode => Success ? 0 : CommandResult is { ExitCode: not 0 } ? CommandResult.ExitCode :
        Receipt.Outcome == "canceled" ? 130 : 1;
}

/// <summary>Read-only reconciliation of the latest receipt with owner identity and simulator state.</summary>
public sealed class AppleSimulatorSessionStatus
{
    /// <summary>Latest receipt, or null before the first session.</summary>
    public AppleSimulatorSessionReceipt? Receipt { get; set; }
    /// <summary>Exact receipt location.</summary>
    public string ReceiptPath { get; set; } = string.Empty;
    /// <summary>active, exited, unknown, or none; process age alone does not settle ownership.</summary>
    public string OwnerState { get; set; } = "none";
    /// <summary>active, exited, unknown, or none for the recorded validation process.</summary>
    public string CommandState { get; set; } = "none";
    /// <summary>Observed device state; null when there is no receipt.</summary>
    public string? DeviceState { get; set; }
    /// <summary>Whether an incomplete receipt prevents another cooperating session from starting.</summary>
    public bool NeedsAttention { get; set; }
}
