using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates localized metadata fields on an App Store version localization.
/// </summary>
[Cmdlet(VerbsCommon.Set, "AppStoreConnectVersionLocalization", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectVersionLocalizationInfo))]
public sealed class SetAppStoreConnectVersionLocalizationCommand : PSCmdlet
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

    /// <summary>Localized App Store description. Empty string clears the field.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Localized App Store keywords. Empty string clears the field.</summary>
    [Parameter] public string? Keywords { get; set; }

    /// <summary>Localized marketing URL. Empty string clears the field.</summary>
    [Parameter] public string? MarketingUrl { get; set; }

    /// <summary>Localized promotional text. Empty string clears the field.</summary>
    [Parameter] public string? PromotionalText { get; set; }

    /// <summary>Localized support URL. Empty string clears the field.</summary>
    [Parameter] public string? SupportUrl { get; set; }

    /// <summary>Localized release notes / what's new text. Empty string clears the field.</summary>
    [Parameter] public string? WhatsNew { get; set; }

    /// <summary>Updates localized App Store metadata fields.</summary>
    protected override void ProcessRecord()
    {
        if (!ShouldProcess(VersionLocalizationId, "Update App Store Connect version localization"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var result = client.UpdateVersionLocalizationAsync(VersionLocalizationId, new AppStoreConnectVersionLocalizationUpdate
        {
            Description = Description,
            Keywords = Keywords,
            MarketingUrl = MarketingUrl,
            PromotionalText = PromotionalText,
            SupportUrl = SupportUrl,
            WhatsNew = WhatsNew
        }).GetAwaiter().GetResult();

        WriteObject(result);
    }
}
