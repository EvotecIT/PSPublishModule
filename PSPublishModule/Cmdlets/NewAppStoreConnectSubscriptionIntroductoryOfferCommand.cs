using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an App Store Connect introductory offer for an auto-renewable subscription.
/// </summary>
[Cmdlet(VerbsCommon.New, "AppStoreConnectSubscriptionIntroductoryOffer", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
[OutputType(typeof(AppStoreConnectSubscriptionIntroductoryOfferInfo))]
public sealed class NewAppStoreConnectSubscriptionIntroductoryOfferCommand : PSCmdlet
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

    /// <summary>Introductory-offer duration.</summary>
    [Parameter(Mandatory = true)]
    public AppStoreConnectSubscriptionOfferDuration Duration { get; set; }

    /// <summary>Introductory-offer payment mode.</summary>
    [Parameter(Mandatory = true)]
    public AppStoreConnectSubscriptionOfferMode OfferMode { get; set; }

    /// <summary>Number of offer periods.</summary>
    [Parameter] public int NumberOfPeriods { get; set; } = 1;

    /// <summary>Optional offer start date.</summary>
    [Parameter] public DateTime? StartDate { get; set; }

    /// <summary>Optional offer end date.</summary>
    [Parameter] public DateTime? EndDate { get; set; }

    /// <summary>Required subscription price point resource id for paid offers.</summary>
    [Parameter] public string? SubscriptionPricePointId { get; set; }

    /// <summary>Territory resource ids for the offer.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] TerritoryId { get; set; } = Array.Empty<string>();

    /// <summary>Creates a subscription introductory offer.</summary>
    protected override void ProcessRecord()
    {
        var territoryIds = TerritoryId
            .Where(static territoryId => !string.IsNullOrWhiteSpace(territoryId))
            .Select(static territoryId => territoryId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(territoryId => ShouldProcess(
                $"{SubscriptionId}/{territoryId}",
                $"Create {Duration} {OfferMode} introductory offer"))
            .ToArray();
        if (territoryIds.Length == 0)
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        foreach (var territoryId in territoryIds)
        {
            var offer = client.CreateSubscriptionIntroductoryOfferAsync(
                SubscriptionId,
                Duration,
                OfferMode,
                territoryId,
                NumberOfPeriods,
                StartDate,
                EndDate,
                SubscriptionPricePointId).GetAwaiter().GetResult();
            WriteObject(offer);
        }
    }
}
