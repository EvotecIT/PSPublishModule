namespace PSMaintenance;

/// <summary>
/// Logical kinds of documentation recognized by the renderer and discovery code.
/// </summary>
public enum DocumentKind
{
    /// <summary>README files (e.g., README.md).</summary>
    Readme,
    /// <summary>CHANGELOG files (e.g., CHANGELOG.md).</summary>
    Changelog,
    /// <summary>LICENSE files; normalized to <c>license.txt</c> during installation.</summary>
    License,
    /// <summary>Upgrade notes (UPGRADE.md or text supplied via configuration).</summary>
    Upgrade,
    /// <summary>A custom file path explicitly requested by the user.</summary>
    Custom
}
