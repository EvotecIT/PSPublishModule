namespace PowerForge;

/// <summary>Platform scope for an Apple signing bundle identifier.</summary>
public enum AppStoreConnectBundleIdPlatform
{
    /// <summary>An iOS-family bundle identifier.</summary>
    iOS,
    /// <summary>A native macOS bundle identifier.</summary>
    macOS,
    /// <summary>A bundle identifier shared across Apple's platforms.</summary>
    Universal
}

/// <summary>An identifier registered with Apple for signing and provisioning.</summary>
public sealed class AppStoreConnectBundleIdInfo
{
    /// <summary>Apple's resource id, used by capability and profile relationships.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The registered bundle identifier.</summary>
    public string? Identifier { get; set; }

    /// <summary>The human-readable registration name.</summary>
    public string? Name { get; set; }

    /// <summary>The platform value returned by Apple, including unknown future values.</summary>
    public string? Platform { get; set; }

    /// <summary>The app identifier prefix returned by Apple.</summary>
    public string? SeedId { get; set; }
}
