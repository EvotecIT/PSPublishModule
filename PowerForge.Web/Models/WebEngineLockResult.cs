namespace PowerForge.Web;

/// <summary>Result payload for engine lock read/verify/update operations.</summary>
public sealed class WebEngineLockResult
{
    /// <summary>Resolved lock file path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Execution mode (show, verify, update).</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>True when the lock file existed before running the command.</summary>
    public bool Exists { get; set; }

    /// <summary>Resolved repository from lock data.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Resolved ref from lock data.</summary>
    public string Ref { get; set; } = string.Empty;

    /// <summary>Resolved channel from lock data.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Lock file timestamp (UTC string).</summary>
    public string UpdatedUtc { get; set; } = string.Empty;

    /// <summary>True when verify mode detected drift versus expected values.</summary>
    public bool DriftDetected { get; set; }

    /// <summary>Human-readable drift reasons.</summary>
    public string[] DriftReasons { get; set; } = Array.Empty<string>();
}
