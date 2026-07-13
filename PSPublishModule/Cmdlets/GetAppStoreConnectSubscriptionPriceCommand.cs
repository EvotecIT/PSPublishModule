using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads App Store Connect prices configured for an auto-renewable subscription.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectSubscriptionPrice")]
[OutputType(typeof(AppStoreConnectSubscriptionPriceInfo))]
public sealed class GetAppStoreConnectSubscriptionPriceCommand : PSCmdlet
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

    /// <summary>Reads configured subscription prices.</summary>
    protected override void ProcessRecord()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var prices = client.GetSubscriptionPricesAsync(SubscriptionId, Limit).GetAwaiter().GetResult();
        WriteObject(prices, enumerateCollection: true);
    }
}
