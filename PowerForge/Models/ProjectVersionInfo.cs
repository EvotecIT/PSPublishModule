namespace PowerForge;

/// <summary>
/// Represents a discovered version entry in a project file.
/// </summary>
public sealed class ProjectVersionInfo
{
    /// <summary>
    /// Gets the discovered version string.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the source file path.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the kind of source file.
    /// </summary>
    public ProjectVersionSourceKind Kind { get; }

    /// <summary>
    /// Gets a legacy display label for the source kind.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Creates a discovered project version entry.
    /// </summary>
    public ProjectVersionInfo(string version, string source, ProjectVersionSourceKind kind)
    {
        Version = version;
        Source = source;
        Kind = kind;
        Type = kind switch
        {
            ProjectVersionSourceKind.Csproj => "C# Project",
            ProjectVersionSourceKind.PowerShellModule => "PowerShell Module",
            ProjectVersionSourceKind.BuildScript => "Build Script",
            _ => kind.ToString(),
        };
    }
}
