namespace PowerForge;

/// <summary>
/// Constants and helpers for the Microsoft Artifact Registry PowerShell repository.
/// </summary>
public static class MicrosoftArtifactRegistryRepository
{
    /// <summary>Default PSResourceGet repository name for Microsoft Artifact Registry.</summary>
    public const string DefaultName = "MAR";

    /// <summary>Microsoft Artifact Registry base URI used by PSResourceGet.</summary>
    public const string DefaultUri = "https://mcr.microsoft.com";

    /// <summary>Returns true when the repository name represents the default Microsoft Artifact Registry registration.</summary>
    public static bool IsDefaultName(string? name)
        => string.Equals(name?.Trim(), DefaultName, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when the URI points at the Microsoft Artifact Registry endpoint.</summary>
    public static bool IsDefaultUri(string? uri)
        => string.Equals(uri?.Trim().TrimEnd('/'), DefaultUri, System.StringComparison.OrdinalIgnoreCase);
}
