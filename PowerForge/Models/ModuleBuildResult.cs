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

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ModuleBuildResult(string stagingPath, string manifestPath, ExportSet exports)
    {
        StagingPath = stagingPath;
        ManifestPath = manifestPath;
        Exports = exports;
    }
}

