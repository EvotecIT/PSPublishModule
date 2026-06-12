namespace PowerForge;

/// <summary>
/// Windows Installer package identity read from a built MSI file.
/// </summary>
public sealed class DotNetPublishMsiPackageMetadata
{
    /// <summary>MSI file path this metadata describes.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Windows Installer ProductCode property.</summary>
    public string? ProductCode { get; set; }

    /// <summary>Windows Installer ProductName property.</summary>
    public string? ProductName { get; set; }

    /// <summary>Windows Installer ProductVersion property.</summary>
    public string? ProductVersion { get; set; }

    /// <summary>Windows Installer Manufacturer property.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Windows Installer UpgradeCode property.</summary>
    public string? UpgradeCode { get; set; }

    /// <summary>Error captured when package metadata could not be read.</summary>
    public string? ReadError { get; set; }
}
