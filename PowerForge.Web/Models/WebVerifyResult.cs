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
}
