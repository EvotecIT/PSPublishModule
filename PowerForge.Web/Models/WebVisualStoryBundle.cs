using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Portable manifest for a generated visual-story bundle.</summary>
public sealed class WebVisualStoryBundle
{
    /// <summary>Optional URI of the schema used to author this manifest.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }
    /// <summary>Manifest schema version.</summary>
    public int? SchemaVersion { get; set; }
    /// <summary>Stable story identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Human-readable story title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Accessible description of the demonstrated result.</summary>
    public string Alt { get; set; } = string.Empty;
    /// <summary>Optional visible caption.</summary>
    public string? Caption { get; set; }
    /// <summary>The concrete result promised and shown by the completed artifact.</summary>
    public string Outcome { get; set; } = string.Empty;
    /// <summary>UTC generation timestamp, when known.</summary>
    public DateTimeOffset? GeneratedAtUtc { get; set; }
    /// <summary>Optional producer name and version for provenance.</summary>
    public string? Producer { get; set; }
    /// <summary>Generated artifacts that make up the story.</summary>
    public WebVisualStoryArtifact[] Artifacts { get; set; } = Array.Empty<WebVisualStoryArtifact>();
}

/// <summary>One artifact in a portable visual-story bundle.</summary>
public sealed class WebVisualStoryArtifact
{
    /// <summary>Artifact role: animated, completed, transcript, source, or html.</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Artifact format, such as svg, gif, apng, png, html, or text.</summary>
    public string Format { get; set; } = string.Empty;
    /// <summary>Path relative to the manifest.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Optional media type.</summary>
    public string? MediaType { get; set; }
    /// <summary>Artifact size after staging.</summary>
    public long? Bytes { get; set; }
    /// <summary>Lower-case SHA-256 digest after staging.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>Options for validating and staging a resolved visual-story bundle.</summary>
public sealed class WebVisualStoryStageOptions
{
    /// <summary>Path to the producer-emitted manifest.</summary>
    public string ManifestPath { get; set; } = string.Empty;
    /// <summary>Destination directory for the self-contained staged bundle.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Maximum size of any one artifact.</summary>
    public long MaximumArtifactBytes { get; set; } = 25L * 1024L * 1024L;
    /// <summary>Overwrite an existing destination bundle.</summary>
    public bool Overwrite { get; set; } = true;
}

/// <summary>Result of validating and staging a visual-story bundle.</summary>
public sealed class WebVisualStoryStageResult
{
    /// <summary>Normalized staged manifest path.</summary>
    public string ManifestPath { get; set; } = string.Empty;
    /// <summary>Staged bundle.</summary>
    public WebVisualStoryBundle Bundle { get; set; } = new();
    /// <summary>Number of copied artifacts.</summary>
    public int ArtifactCount { get; set; }
    /// <summary>Total copied bytes.</summary>
    public long TotalBytes { get; set; }
}
