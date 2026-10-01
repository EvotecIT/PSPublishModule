namespace PowerForge;

/// <summary>
/// Declares a repository file whose embedded version follows a resolved project version.
/// </summary>
public sealed class ProjectVersionBinding
{
    /// <summary>Repository-relative path of the file to update.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Project name whose resolved release version is used.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Regular expression that must match exactly once in the target file.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Replacement text containing the <c>{Version}</c> token.</summary>
    public string Replacement { get; set; } = "{Version}";

    /// <summary>
    /// Regenerates existing Codex and Claude compatibility files when this binding targets
    /// a canonical agent-plugin <c>plugin.json</c>. Generated updates join the release transaction.
    /// Run <c>agent-plugin sync</c> once before enabling this setting for a new plugin.
    /// </summary>
    public bool SyncAgentPluginCompatibility { get; set; }
}
