using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an App Store Connect screenshot set for an App Store version localization.
/// </summary>
[Cmdlet(VerbsCommon.New, "AppStoreConnectScreenshotSet", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectScreenshotSetInfo))]
public sealed class NewAppStoreConnectScreenshotSetCommand : PSCmdlet
{
    /// <summary>Issuer ID from App Store Connect API keys.</summary>
    [Parameter(Mandatory = true)] public string IssuerId { get; set; } = string.Empty;

    /// <summary>Key ID associated with the private key.</summary>
    [Parameter(Mandatory = true)] public string KeyId { get; set; } = string.Empty;

    /// <summary>Private key text in PEM format.</summary>
    [Parameter] public string? PrivateKey { get; set; }

    /// <summary>Path to a private key file in PEM format.</summary>
    [Parameter] public string? PrivateKeyPath { get; set; }

    /// <summary>Token lifetime in minutes, up to 20.</summary>
    [Parameter] public int TokenLifetimeMinutes { get; set; } = 15;

    /// <summary>App Store version localization id.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string VersionLocalizationId { get; set; } = string.Empty;

    /// <summary>Screenshot display type, for example APP_IPHONE_65.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ScreenshotDisplayType { get; set; } = string.Empty;

    /// <summary>Creates the screenshot set.</summary>
    protected override void ProcessRecord()
    {
        if (!ShouldProcess(VersionLocalizationId, $"Create App Store Connect screenshot set '{ScreenshotDisplayType}'"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var set = client.CreateScreenshotSetAsync(VersionLocalizationId, ScreenshotDisplayType).GetAwaiter().GetResult();
        WriteObject(set);
    }
}
