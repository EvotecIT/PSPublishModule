namespace PowerForge;

/// <summary>
/// Host-facing signing settings resolved for Authenticode and NuGet signing workflows.
/// </summary>
public sealed class ReleaseSigningHostSettings
{
    /// <summary>Whether signing is configured well enough to run.</summary>
    public bool IsConfigured { get; set; }

    /// <summary>Resolved signing certificate thumbprint.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Resolved certificate store name.</summary>
    public string StoreName { get; set; } = "CurrentUser";

    /// <summary>Resolved timestamp server URL.</summary>
    public string TimeStampServer { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Resolved PSPublishModule path used for shared PowerShell-host signing commands.</summary>
    public string ModulePath { get; set; } = string.Empty;

    /// <summary>Failure message when signing is not configured.</summary>
    public string? MissingConfigurationMessage { get; set; }
}
