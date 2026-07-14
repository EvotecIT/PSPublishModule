using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates localized app-level information on the App Store.
/// </summary>
[Cmdlet(VerbsCommon.Set, "AppStoreConnectAppInfoLocalization", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectAppInfoLocalizationInfo))]
public sealed class SetAppStoreConnectAppInfoLocalizationCommand : AsyncPSCmdlet
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

    /// <summary>App Information localization resource id.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string AppInfoLocalizationId { get; set; } = string.Empty;

    /// <summary>Localized App Store name. Empty string clears the field.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>Localized App Store subtitle. Empty string clears the field.</summary>
    [Parameter] public string? Subtitle { get; set; }

    /// <summary>Localized privacy policy URL. Empty string clears the field.</summary>
    [Parameter] public string? PrivacyPolicyUrl { get; set; }

    /// <summary>Localized privacy choices URL. Empty string clears the field.</summary>
    [Parameter] public string? PrivacyChoicesUrl { get; set; }

    /// <summary>Localized privacy policy text. Empty string clears the field.</summary>
    [Parameter] public string? PrivacyPolicyText { get; set; }

    /// <summary>Updates localized app-level App Store information.</summary>
    protected override async Task ProcessRecordAsync()
    {
        if (!ShouldProcess(AppInfoLocalizationId, "Update App Store Connect App Information localization"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var result = await client.UpdateAppInfoLocalizationAsync(AppInfoLocalizationId, new AppStoreConnectAppInfoLocalizationUpdate
        {
            Name = Name,
            Subtitle = Subtitle,
            PrivacyPolicyUrl = PrivacyPolicyUrl,
            PrivacyChoicesUrl = PrivacyChoicesUrl,
            PrivacyPolicyText = PrivacyPolicyText
        }, CancelToken);

        WriteObject(result);
    }
}
