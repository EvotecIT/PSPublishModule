namespace PowerForge;

public sealed partial class AppStoreConnectGovernanceService
{
    private Task ApplyChangeAsync(
        AppStoreConnectGovernanceSpec spec,
        AppStoreConnectGovernanceChange change,
        CancellationToken cancellationToken)
    {
        return change.ResourceType switch
        {
            "AppPriceSchedule" => ApplyPriceScheduleAsync(spec, cancellationToken),
            "AppPrice" => ApplyAppPriceAsync(spec, change, cancellationToken),
            "AppAvailability" => ApplyAvailabilityAsync(spec, cancellationToken),
            "TerritoryAvailability" => ApplyTerritoryAvailabilityAsync(spec, change, cancellationToken),
            "AccessibilityDeclaration" => ApplyAccessibilityAsync(spec, change, cancellationToken),
            "EncryptionDeclaration" => ApplyEncryptionAsync(spec, change, cancellationToken),
            "SubscriptionGroup" => ApplySubscriptionGroupAsync(spec, change, cancellationToken),
            "SubscriptionGroupLocalization" => ApplySubscriptionGroupLocalizationAsync(spec, change, cancellationToken),
            "Subscription" => ApplySubscriptionAsync(spec, change, cancellationToken),
            "SubscriptionLocalization" => ApplySubscriptionLocalizationAsync(spec, change, cancellationToken),
            "SubscriptionPrice" => ApplySubscriptionPriceAsync(spec, change, cancellationToken),
            "SubscriptionPlanAvailability" => ApplySubscriptionAvailabilityAsync(spec, change, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported governance resource type '{change.ResourceType}'.")
        };
    }

    private async Task ApplyPriceScheduleAsync(AppStoreConnectGovernanceSpec spec, CancellationToken cancellationToken)
    {
        _ = await _client.CreateAppPriceScheduleAsync(spec.AppId, spec.Pricing!, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAppPriceAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var price = spec.Pricing!.Prices.Single(item => Same(PriceKey(item), change.Key));
        _ = await _client.CreateAppPriceScheduleAsync(
            spec.AppId,
            new AppStoreConnectAppPricingSpec { BaseTerritoryId = spec.Pricing.BaseTerritoryId, Prices = new[] { price } },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAvailabilityAsync(AppStoreConnectGovernanceSpec spec, CancellationToken cancellationToken)
    {
        _ = await _client.CreateAppAvailabilityAsync(spec.AppId, spec.Availability!, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyTerritoryAvailabilityAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var territory = spec.Availability!.Territories.Single(item => Same(item.TerritoryId, change.Key));
        _ = await _client.UpdateTerritoryAvailabilityAsync(RequiredId(change), territory, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyAccessibilityAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var declaration = spec.Accessibility.Single(item => Same(item.DeviceFamily, change.Key));
        if (change.Action == AppStoreConnectGovernanceChangeAction.Create)
            _ = await _client.CreateAccessibilityDeclarationAsync(spec.AppId, declaration, cancellationToken).ConfigureAwait(false);
        else
            _ = await _client.UpdateAccessibilityDeclarationAsync(RequiredId(change), declaration, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyEncryptionAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var declaration = spec.EncryptionDeclarations.Single(item => Same(EncryptionKey(item), change.Key));
        _ = await _client.CreateEncryptionDeclarationAsync(spec.AppId, declaration, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplySubscriptionGroupAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var group = spec.SubscriptionGroups.Single(item => Same(GroupKey(item), change.Key));
        if (change.Action == AppStoreConnectGovernanceChangeAction.Create)
            _ = await _client.CreateSubscriptionGroupAsync(spec.AppId, group.ReferenceName, cancellationToken).ConfigureAwait(false);
        else
            _ = await _client.UpdateSubscriptionGroupAsync(RequiredId(change), group.ReferenceName, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplySubscriptionGroupLocalizationAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var pair = spec.SubscriptionGroups
            .SelectMany(group => group.Localizations.Select(localization => (Group: group, Localization: localization)))
            .Single(item => Same(GroupLocalizationKey(item.Group, item.Localization.Locale), change.Key));
        if (change.Action == AppStoreConnectGovernanceChangeAction.Create)
            _ = await _client.CreateSubscriptionGroupLocalizationAsync(RequiredParentId(change), pair.Localization, cancellationToken).ConfigureAwait(false);
        else
            _ = await _client.UpdateSubscriptionGroupLocalizationAsync(RequiredId(change), pair.Localization, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplySubscriptionAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var subscription = FindSubscription(spec, change.Key);
        if (change.Action == AppStoreConnectGovernanceChangeAction.Create)
            _ = await _client.CreateSubscriptionAsync(RequiredParentId(change), subscription, cancellationToken).ConfigureAwait(false);
        else
            _ = await _client.UpdateSubscriptionAsync(RequiredId(change), subscription, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplySubscriptionLocalizationAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var pair = spec.SubscriptionGroups.SelectMany(group => group.Subscriptions)
            .SelectMany(subscription => subscription.Localizations.Select(localization => (Subscription: subscription, Localization: localization)))
            .Single(item => Same(SubscriptionChildKey(item.Subscription.ProductId, item.Localization.Locale), change.Key));
        if (change.Action == AppStoreConnectGovernanceChangeAction.Create)
            _ = await _client.CreateSubscriptionLocalizationAsync(RequiredParentId(change), pair.Localization, cancellationToken).ConfigureAwait(false);
        else
            _ = await _client.UpdateSubscriptionLocalizationAsync(RequiredId(change), pair.Localization, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplySubscriptionPriceAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var pair = spec.SubscriptionGroups.SelectMany(group => group.Subscriptions)
            .SelectMany(subscription => subscription.Prices.Select(price => (Subscription: subscription, Price: price)))
            .Single(item => Same(SubscriptionPriceKey(item.Subscription.ProductId, item.Price), change.Key));
        _ = await _client.CreateSubscriptionPriceAsync(RequiredParentId(change), pair.Price, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplySubscriptionAvailabilityAsync(AppStoreConnectGovernanceSpec spec, AppStoreConnectGovernanceChange change, CancellationToken cancellationToken)
    {
        var subscription = spec.SubscriptionGroups.SelectMany(group => group.Subscriptions)
            .Single(item => item.Availabilities.Any(availability => Same(SubscriptionChildKey(item.ProductId, availability.PlanType), change.Key)));
        var availability = subscription.Availabilities.Single(item => Same(SubscriptionChildKey(subscription.ProductId, item.PlanType), change.Key));
        if (change.Action == AppStoreConnectGovernanceChangeAction.Create)
            _ = await _client.CreateSubscriptionPlanAvailabilityAsync(RequiredParentId(change), availability, cancellationToken).ConfigureAwait(false);
        else
            _ = await _client.UpdateSubscriptionPlanAvailabilityAsync(RequiredId(change), availability, cancellationToken).ConfigureAwait(false);
    }

    private static AppStoreConnectSubscriptionSpec FindSubscription(AppStoreConnectGovernanceSpec spec, string productId) =>
        spec.SubscriptionGroups.SelectMany(group => group.Subscriptions).Single(subscription => Same(subscription.ProductId, productId));

    private static string RequiredId(AppStoreConnectGovernanceChange change) =>
        !string.IsNullOrWhiteSpace(change.ResourceId) ? change.ResourceId! : throw new InvalidOperationException($"Governance change '{change.Key}' has no resource id.");

    private static string RequiredParentId(AppStoreConnectGovernanceChange change) =>
        !string.IsNullOrWhiteSpace(change.ParentId) ? change.ParentId! : throw new InvalidOperationException($"Governance change '{change.Key}' has no parent resource id.");
}
