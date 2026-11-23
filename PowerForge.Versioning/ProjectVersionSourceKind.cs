namespace PowerForge;

/// <summary>
/// Identifies the file type that contributed a discovered project version.
/// </summary>
public enum ProjectVersionSourceKind
{
    /// <summary>
    /// Version discovered from a <c>.csproj</c> file.
    /// </summary>
    Csproj,

    /// <summary>
    /// Version discovered from a PowerShell module manifest.
    /// </summary>
    PowerShellModule,

    /// <summary>
    /// Version discovered from a build script.
    /// </summary>
    BuildScript,
}
