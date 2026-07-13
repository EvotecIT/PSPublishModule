using System;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads App Store Connect auto-renewable subscription products for an app.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectSubscription")]
[OutputType(typeof(AppStoreConnectSubscriptionInfo))]
public sealed class GetAppStoreConnectSubscriptionCommand : AsyncPSCmdlet
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

    /// <summary>App Store Connect app id.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Optional StoreKit product id filter.</summary>
    [Parameter] public string[] ProductId { get; set; } = Array.Empty<string>();

    /// <summary>Maximum result count per App Store Connect request.</summary>
    [Parameter] public int Limit { get; set; } = 200;

    /// <summary>Reads auto-renewable subscription products.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var subscriptions = await client.GetSubscriptionsForAppAsync(AppId, Limit, CancelToken);

        var productIds = ProductId
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (productIds.Count > 0)
        {
            subscriptions = subscriptions
                .Where(subscription => subscription.ProductId is not null && productIds.Contains(subscription.ProductId))
                .ToArray();
        }

        WriteObject(subscriptions, enumerateCollection: true);
    }
}
