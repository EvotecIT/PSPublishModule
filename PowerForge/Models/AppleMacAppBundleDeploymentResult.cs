namespace PowerForge;

/// <summary>Installation and running-binary evidence for an approved prebuilt macOS app.</summary>
public sealed class AppleMacAppBundleDeploymentResult
{
    /// <summary>Bundle installation result.</summary>
    public AppleMacAppInstallResult? Install { get; set; }
    /// <summary>Launch result, when requested.</summary>
    public AppleMacAppLaunchResult? Launch { get; set; }
    /// <summary>Verified PID from the user's running-app inventory.</summary>
    public int? ProcessId { get; set; }
    /// <summary>Verified running executable path.</summary>
    public string? ExecutablePath { get; set; }
    /// <summary>SHA-256 of the installed executable verified against its running inode.</summary>
    public string? ExecutableSha256 { get; set; }
    /// <summary>Device and inode mapped by the verified process.</summary>
    public string? ExecutableFileIdentity { get; set; }
    /// <summary>True when installation and every requested launch verification succeeded.</summary>
    public bool Succeeded => (Install?.Succeeded ?? false) && (Launch is null || (Launch.Succeeded && ProcessId.HasValue));
}
