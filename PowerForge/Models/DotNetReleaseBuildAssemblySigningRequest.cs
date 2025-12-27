namespace PowerForge;

/// <summary>
/// Request describing which assemblies should be signed as part of a .NET release build.
/// </summary>
public sealed class DotNetReleaseBuildAssemblySigningRequest
{
    /// <summary>Release output path containing the assemblies.</summary>
    public string ReleasePath { get; set; } = string.Empty;

    /// <summary>Certificate store location used when searching for the signing certificate.</summary>
    public CertificateStoreLocation LocalStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Certificate thumbprint used to select the signing certificate.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>RFC3161 timestamp server URL used during signing.</summary>
    public string TimeStampServer { get; set; } = string.Empty;

    /// <summary>Glob patterns used to select which files should be signed.</summary>
    public string[] IncludePatterns { get; set; } = Array.Empty<string>();
}

