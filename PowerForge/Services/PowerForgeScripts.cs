namespace PowerForge;

/// <summary>
/// Public loader for embedded PowerShell scripts shipped with PowerForge.
/// </summary>
public static class PowerForgeScripts
{
    /// <summary>
    /// Loads an embedded script by relative path (for example <c>Scripts/Cmdlets/Invoke-SendGitHubRelease.ps1</c>).
    /// </summary>
    public static string Load(string relativePath) => EmbeddedScripts.Load(relativePath);
}

