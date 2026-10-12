namespace PowerForge;

/// <summary>App Store provisioning profile types supported by the profile creation API.</summary>
public enum AppStoreConnectAppStoreProfileType
{
    /// <summary>An iOS App Store profile.</summary>
    iOS,
    /// <summary>A native macOS App Store profile.</summary>
    macOS,
    /// <summary>A Mac Catalyst App Store profile.</summary>
    MacCatalyst
}

/// <summary>A public signing certificate registered with Apple; contains no private key.</summary>
public sealed class AppStoreConnectSigningCertificateInfo
{
    /// <summary>Certificate resource id used when creating profiles.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Certificate name.</summary>
    public string? Name { get; set; }
    /// <summary>Certificate type returned by Apple.</summary>
    public string? CertificateType { get; set; }
    /// <summary>Certificate serial number.</summary>
    public string? SerialNumber { get; set; }
    /// <summary>Certificate expiration date.</summary>
    public DateTimeOffset? ExpirationDate { get; set; }
    /// <summary>Base64-encoded public DER certificate, when returned by Apple.</summary>
    public string? CertificateContent { get; set; }
}

/// <summary>A provisioning profile registered with Apple.</summary>
public sealed class AppStoreConnectProvisioningProfileInfo
{
    /// <summary>Profile resource id.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Profile UUID.</summary>
    public string? Uuid { get; set; }
    /// <summary>Profile name.</summary>
    public string? Name { get; set; }
    /// <summary>Profile type returned by Apple.</summary>
    public string? ProfileType { get; set; }
    /// <summary>Profile platform returned by Apple.</summary>
    public string? Platform { get; set; }
    /// <summary>Profile state returned by Apple.</summary>
    public string? ProfileState { get; set; }
    /// <summary>Profile expiration date.</summary>
    public DateTimeOffset? ExpirationDate { get; set; }
    /// <summary>Base64-encoded signed provisioning profile, when returned by Apple.</summary>
    public string? ProfileContent { get; set; }
}
