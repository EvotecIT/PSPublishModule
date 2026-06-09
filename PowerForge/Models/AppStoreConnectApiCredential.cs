namespace PowerForge;

/// <summary>
/// App Store Connect API credential material used to create read-only API clients.
/// </summary>
public sealed class AppStoreConnectApiCredential
{
    /// <summary>Issuer ID from App Store Connect API keys.</summary>
    public string IssuerId { get; set; } = string.Empty;

    /// <summary>Key ID associated with the private key.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Private key text in PEM format.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>JWT token lifetime. Defaults to 15 minutes.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
