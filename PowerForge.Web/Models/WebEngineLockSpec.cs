using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Pin file used by websites to lock the PowerForge engine checkout ref.</summary>
public sealed class WebEngineLockSpec
{
    /// <summary>Optional JSON schema reference.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>GitHub repository containing the engine sources.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Git ref used in CI (prefer immutable commit SHA).</summary>
    public string Ref { get; set; } = string.Empty;

    /// <summary>Optional release channel label (for example stable/candidate/nightly).</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the latest lock update.</summary>
    public string UpdatedUtc { get; set; } = string.Empty;
}
