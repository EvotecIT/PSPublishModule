namespace PowerForge;

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
    /// <summary>About topics (about_*.help.txt).</summary>
    About,
    /// <summary>PowerShell formatting files (*.Format.ps1xml).</summary>
    Format,
    /// <summary>PowerShell type extension files (*.Types.ps1xml).</summary>
    Types,
    /// <summary>Community health files such as CONTRIBUTING/SECURITY/SUPPORT/CODE_OF_CONDUCT.</summary>
    Community,
    /// <summary>Aggregated releases/changelog overview.</summary>
    Releases,
    /// <summary>A custom file path explicitly requested by the user.</summary>
    Custom
}
