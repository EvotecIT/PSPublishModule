using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads App Store Connect introductory offers for an auto-renewable subscription.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectSubscriptionIntroductoryOffer")]
[OutputType(typeof(AppStoreConnectSubscriptionIntroductoryOfferInfo))]
public sealed class GetAppStoreConnectSubscriptionIntroductoryOfferCommand : PSCmdlet
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

    /// <summary>App Store Connect subscription resource id.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Maximum result count per App Store Connect request.</summary>
    [Parameter] public int Limit { get; set; } = 200;

    /// <summary>Reads subscription introductory offers.</summary>
    protected override void ProcessRecord()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var offers = client.GetSubscriptionIntroductoryOffersAsync(SubscriptionId, Limit).GetAwaiter().GetResult();
        WriteObject(offers, enumerateCollection: true);
    }
}
