using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads localized app-level information from App Store Connect.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectAppInfoLocalization")]
[OutputType(typeof(AppStoreConnectAppInfoLocalizationInfo))]
public sealed class GetAppStoreConnectAppInfoLocalizationCommand : AsyncPSCmdlet
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

    /// <summary>App Information resource id.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string AppInfoId { get; set; } = string.Empty;

    /// <summary>Optional locale filter, for example en-US.</summary>
    [Parameter] public string? Locale { get; set; }

    /// <summary>Maximum result count.</summary>
    [Parameter] public int Limit { get; set; } = 50;

    /// <summary>Reads App Information localizations.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var localizations = await client.GetAppInfoLocalizationsAsync(AppInfoId, Locale, Limit, CancelToken);
        WriteObject(localizations, enumerateCollection: true);
    }
}
