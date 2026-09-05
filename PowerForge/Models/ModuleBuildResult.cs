namespace PowerForge;

/// <summary>
/// Result of building a module into a staging directory.
/// </summary>
public sealed class ModuleBuildResult
{
    /// <summary>Staging directory path containing the built module.</summary>
    public string StagingPath { get; }

    /// <summary>Path to the module manifest in staging.</summary>
    public string ManifestPath { get; }

    /// <summary>Exports detected and written into the manifest.</summary>
    public ExportSet Exports { get; }

    internal ModuleOwnerNote[] BuildNotes { get; }

    /// <summary>
    /// Exact finalized module payload selected by an authoritative build producer.
    /// An empty collection means downstream packaging should apply the normal module include policy.
    /// </summary>
    public IReadOnlyList<string> FinalizedPayloadFiles { get; internal set; }

    /// <summary>
    /// Creates a new result instance using the original binary-compatible constructor contract.
    /// </summary>
    public ModuleBuildResult(
        string stagingPath,
        string manifestPath,
        ExportSet exports,
        ModuleOwnerNote[]? buildNotes = null)
        : this(stagingPath, manifestPath, exports, buildNotes, finalizedPayloadFiles: null)
    {
    }

    /// <summary>
    /// Creates a new result instance with an authoritative finalized payload.
    /// </summary>
    public ModuleBuildResult(
        string stagingPath,
        string manifestPath,
        ExportSet exports,
        ModuleOwnerNote[]? buildNotes,
        IReadOnlyList<string>? finalizedPayloadFiles = null)
    {
        StagingPath = stagingPath;
        ManifestPath = manifestPath;
        Exports = exports;
        BuildNotes = buildNotes ?? System.Array.Empty<ModuleOwnerNote>();
        FinalizedPayloadFiles = finalizedPayloadFiles ?? System.Array.Empty<string>();
    }
}
