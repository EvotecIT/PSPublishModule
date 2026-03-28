using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Pin file used by websites to lock published PowerForge.Web runner assets.</summary>
public sealed class WebToolLockSpec
{
    /// <summary>Optional JSON schema reference.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>GitHub repository containing the published assets.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Logical target name, such as PowerForgeWeb.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Exact GitHub release tag to download.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Exact release asset file name to download.</summary>
    public string Asset { get; set; } = string.Empty;

    /// <summary>Optional relative path to the executable inside the extracted asset.</summary>
    public string BinaryPath { get; set; } = string.Empty;
}
