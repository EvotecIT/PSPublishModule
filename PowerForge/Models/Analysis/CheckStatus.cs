namespace PowerForge;

/// <summary>
/// Generic status for project validation/analysis checks.
/// </summary>
public enum CheckStatus
{
    /// <summary>The check passed.</summary>
    Pass,
    /// <summary>The check produced warnings but did not fail.</summary>
    Warning,
    /// <summary>The check failed.</summary>
    Fail
}
