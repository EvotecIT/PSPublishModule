namespace PowerForge;

/// <summary>
/// Offline bundle metadata written after managed save operations.
/// </summary>
public sealed class ManagedModuleBundleMetadata
{
    /// <summary>
    /// Metadata schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// UTC time when the metadata was written.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>
    /// Module root that contains the saved bundle.
    /// </summary>
    public string ModuleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Saved module and dependency entries.
    /// </summary>
    public IReadOnlyList<ManagedModuleBundleEntry> Modules { get; set; } = Array.Empty<ManagedModuleBundleEntry>();
}
