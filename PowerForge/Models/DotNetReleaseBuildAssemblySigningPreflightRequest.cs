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

    /// <summary>
    /// Signs a throwaway file during preflight so a hardware token asks for its PIN at the start
    /// of the release rather than when signing begins. Handlers skip it in CI and non-interactive
    /// sessions, and when <c>POWERFORGE_SIGNING_EARLY_UNLOCK</c> is <c>0</c>.
    /// </summary>
    public bool UnlockSigningKey { get; set; }
}
