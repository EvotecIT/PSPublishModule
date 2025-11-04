namespace PSMaintenance;

/// <summary>
/// Selection policy for standard tabs (README/CHANGELOG/LICENSE) when both Local and Remote exist.
/// </summary>
public enum DocumentationMode
{
    /// <summary>Pick Local over Remote when both exist (collapse identical by default).</summary>
    PreferLocal,
    /// <summary>Pick Remote over Local when both exist (collapse identical by default).</summary>
    PreferRemote,
    /// <summary>Show both Local and Remote when they differ; collapse identical by default.</summary>
    All
}

