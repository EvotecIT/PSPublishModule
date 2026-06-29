namespace PowerForge;

/// <summary>
/// Request used to validate assembly signing prerequisites before mutable release steps run.
/// </summary>
public sealed class DotNetReleaseBuildAssemblySigningPreflightRequest
{
    /// <summary>Certificate store location used when searching for the signing certificate.</summary>
    public CertificateStoreLocation LocalStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Certificate thumbprint used to select the signing certificate.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>RFC3161 timestamp server URL used during signing.</summary>
    public string TimeStampServer { get; set; } = string.Empty;
}
