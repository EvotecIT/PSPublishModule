namespace PowerForge;

/// <summary>
/// Behavior used when a managed block is missing from an existing Markdown document.
/// </summary>
public enum ManagedMarkdownMissingBlockBehavior
{
    /// <summary>Fail without modifying the document.</summary>
    Fail,

    /// <summary>Append a new marker-delimited block to the document.</summary>
    Append
}

/// <summary>
/// Marker syntax accepted by a managed Markdown update.
/// </summary>
public enum ManagedMarkdownMarkerFormat
{
    /// <summary>Require namespaced markers such as <c>POWERFORGE:sponsors:START</c>.</summary>
    Namespaced,

    /// <summary>Require legacy two-part markers such as <c>sponsors:start</c>.</summary>
    LegacyBlockId,

    /// <summary>Accept either namespaced or legacy markers, but never both for the same block.</summary>
    NamespacedOrLegacyBlockId
}

/// <summary>
/// Request for creating or updating a marker-delimited Markdown block.
/// </summary>
public sealed class ManagedMarkdownUpdateRequest
{
    /// <summary>Marker namespace used before the block identifier.</summary>
    public string MarkerNamespace { get; set; } = "POWERFORGE";

    /// <summary>Marker syntax accepted for this update. Namespaced markers are required by default.</summary>
    public ManagedMarkdownMarkerFormat MarkerFormat { get; set; } = ManagedMarkdownMarkerFormat.Namespaced;

    /// <summary>Markdown document path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Stable identifier used in the managed block markers.</summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>Markdown written between the managed block markers.</summary>
    public string Markdown { get; set; } = string.Empty;

    /// <summary>Whether a missing document may be created.</summary>
    public bool CreateIfMissing { get; set; }

    /// <summary>Behavior used when the document exists but the managed block does not.</summary>
    public ManagedMarkdownMissingBlockBehavior MissingBlockBehavior { get; set; } = ManagedMarkdownMissingBlockBehavior.Fail;

    /// <summary>Optional level-one heading added when a new document is created.</summary>
    public string? NewDocumentTitle { get; set; }
}

/// <summary>
/// Result of a managed Markdown block update.
/// </summary>
public sealed class ManagedMarkdownUpdateResult
{
    /// <summary>Absolute document path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Updated block identifier.</summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>Whether the file content changed.</summary>
    public bool Changed { get; set; }

    /// <summary>Whether a new document was created.</summary>
    public bool Created { get; set; }

    /// <summary>Whether a new block was appended to an existing document.</summary>
    public bool Appended { get; set; }
}
