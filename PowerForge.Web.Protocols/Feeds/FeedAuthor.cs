namespace PowerForge.Web.Protocols.Feeds;

/// <summary>A publisher-supplied author identity, without inferred attribution.</summary>
public sealed class FeedAuthor
{
    /// <summary>Author display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional author profile URL.</summary>
    public string? Url { get; set; }
}
