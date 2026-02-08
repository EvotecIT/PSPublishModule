namespace PowerForge.Web;

/// <summary>Result payload for configuration verification.</summary>
public sealed class WebVerifyResult
{
    /// <summary>Overall verification status.</summary>
    public bool Success { get; set; }
    /// <summary>Verification errors.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>Verification warnings.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();

    /// <summary>Optional path to a verify baseline file used by the CLI/pipeline.</summary>
    public string? BaselinePath { get; set; }
    /// <summary>Total number of baseline warning keys loaded (when a baseline is used).</summary>
    public int BaselineWarningCount { get; set; }
    /// <summary>Total number of warnings considered "new" vs baseline (when a baseline is used).</summary>
    public int NewWarningCount { get; set; }
    /// <summary>Warnings not present in the baseline (when a baseline is used).</summary>
    public string[] NewWarnings { get; set; } = Array.Empty<string>();
}
