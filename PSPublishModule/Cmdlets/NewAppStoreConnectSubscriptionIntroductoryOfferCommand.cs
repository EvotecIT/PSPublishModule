using System;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an App Store Connect introductory offer for an auto-renewable subscription.
/// </summary>
[Cmdlet(VerbsCommon.New, "AppStoreConnectSubscriptionIntroductoryOffer", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
[OutputType(typeof(AppStoreConnectSubscriptionIntroductoryOfferInfo))]
public sealed class NewAppStoreConnectSubscriptionIntroductoryOfferCommand : AsyncPSCmdlet
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

    /// <summary>
    /// Subscription price point resource id for a paid offer. Paid offers accept one territory per invocation
    /// because App Store Connect price points are territory-specific.
    /// </summary>
    [Parameter] public string? SubscriptionPricePointId { get; set; }

    /// <summary>
    /// Territory resource ids for the offer. Free trials may target multiple territories; paid offers accept one.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] TerritoryId { get; set; } = Array.Empty<string>();

    /// <summary>Creates a subscription introductory offer.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var territoryIds = TerritoryId
            .Where(static territoryId => !string.IsNullOrWhiteSpace(territoryId))
            .Select(static territoryId => territoryId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var isPaidOffer = OfferMode != AppStoreConnectSubscriptionOfferMode.FreeTrial;
        if (isPaidOffer && territoryIds.Length > 1)
        {
            throw new PSArgumentException(
                "Paid introductory offers require a territory-specific subscription price point. " +
                "Create one territory per command invocation with its matching SubscriptionPricePointId.");
        }
        if (isPaidOffer && string.IsNullOrWhiteSpace(SubscriptionPricePointId))
        {
            throw new PSArgumentException(
                "SubscriptionPricePointId is required for paid introductory offers.",
                nameof(SubscriptionPricePointId));
        }

        territoryIds = territoryIds
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
            var offer = await client.CreateSubscriptionIntroductoryOfferAsync(
                SubscriptionId,
                Duration,
                OfferMode,
                territoryId,
                NumberOfPeriods,
                StartDate,
                EndDate,
                SubscriptionPricePointId,
                CancelToken);
            WriteObject(offer);
        }
    }
}
