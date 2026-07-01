namespace PowerForge;

/// <summary>
/// Named module build profiles that emit common PowerForge module-pipeline configuration.
/// </summary>
public enum ModuleBuildProfileKind
{
    /// <summary>
    /// Standard script-module defaults for formatting, documentation, validation, compatibility checks, import, and build.
    /// </summary>
    Standard,

    /// <summary>
    /// Standard defaults plus binary PowerShell module build settings.
    /// </summary>
    Binary
}
