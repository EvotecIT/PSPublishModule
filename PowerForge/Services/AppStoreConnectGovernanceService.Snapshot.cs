namespace PowerForge;

public sealed partial class AppStoreConnectGovernanceService
{
    /// <summary>Exports current Apple state as a reviewable declaration; it never mutates Apple.</summary>
    public async Task<AppStoreConnectGovernanceSpec> SnapshotAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId)) throw new ArgumentException("App id is required.", nameof(appId));
        appId = appId.Trim();
        var pricingTask = _client.GetAppPriceScheduleAsync(appId, cancellationToken);
        var availabilityTask = _client.GetAppAvailabilityAsync(appId, cancellationToken);
        var accessibilityTask = _client.GetAccessibilityDeclarationsAsync(appId, cancellationToken);
        var encryptionTask = _client.GetEncryptionDeclarationsAsync(appId, cancellationToken);
        var groupsTask = _client.GetSubscriptionGroupsAsync(appId, cancellationToken: cancellationToken);
        await Task.WhenAll(pricingTask, availabilityTask, accessibilityTask, encryptionTask, groupsTask).ConfigureAwait(false);

        var groups = new List<AppStoreConnectSubscriptionGroupSpec>();
        foreach (var group in groupsTask.Result)
            groups.Add(await SnapshotGroupAsync(group, cancellationToken).ConfigureAwait(false));
        var pricing = pricingTask.Result;
        var availability = availabilityTask.Result;
        return new AppStoreConnectGovernanceSpec
        {
            SchemaVersion = 1,
            AppId = appId,
            Pricing = pricing is null ? null : new AppStoreConnectAppPricingSpec
            {
                BaseTerritoryId = RequireObserved(pricing.BaseTerritoryId, "app price schedule base territory"),
                Prices = pricing.Prices.Select(price => new AppStoreConnectAppPriceSpec
                {
                    AppPricePointId = RequireObserved(price.AppPricePointId, $"app price '{price.Id}' price point"),
                    TerritoryId = RequireObserved(price.TerritoryId, $"app price '{price.Id}' territory"),
                    StartDate = NormalizeObserved(price.StartDate),
                    EndDate = NormalizeObserved(price.EndDate)
                }).ToArray()
            },
            Availability = availability is null ? null : new AppStoreConnectAppAvailabilitySpec
            {
                AvailableInNewTerritories = availability.AvailableInNewTerritories,
                Territories = availability.Territories.Select(item => new AppStoreConnectTerritoryAvailabilitySpec
                {
                    TerritoryId = RequireObserved(item.TerritoryId, $"territory availability '{item.Id}' territory"),
                    Available = item.Available == true,
                    ReleaseDate = NormalizeObserved(item.ReleaseDate),
                    PreOrderEnabled = item.PreOrderEnabled
                }).ToArray()
            },
            Accessibility = accessibilityTask.Result.Select(item => new AppStoreConnectAccessibilityDeclarationSpec
            {
                DeviceFamily = RequireObserved(item.DeviceFamily, $"accessibility declaration '{item.Id}' device family"),
                Publish = Same(item.State, "PUBLISHED"),
                SupportsAudioDescriptions = item.SupportsAudioDescriptions,
                SupportsCaptions = item.SupportsCaptions,
                SupportsDarkInterface = item.SupportsDarkInterface,
                SupportsDifferentiateWithoutColorAlone = item.SupportsDifferentiateWithoutColorAlone,
                SupportsLargerText = item.SupportsLargerText,
                SupportsReducedMotion = item.SupportsReducedMotion,
                SupportsSufficientContrast = item.SupportsSufficientContrast,
                SupportsVoiceControl = item.SupportsVoiceControl,
                SupportsVoiceover = item.SupportsVoiceover
            }).ToArray(),
            EncryptionDeclarations = encryptionTask.Result.Select(item => new AppStoreConnectEncryptionDeclarationSpec
            {
                AppDescription = RequireObserved(item.AppDescription, $"encryption declaration '{item.Id}' app description"),
                ContainsProprietaryCryptography = item.ContainsProprietaryCryptography == true,
                ContainsThirdPartyCryptography = item.ContainsThirdPartyCryptography == true,
                AvailableOnFrenchStore = item.AvailableOnFrenchStore == true
            }).ToArray(),
            SubscriptionGroups = groups.ToArray()
        };
    }

    private async Task<AppStoreConnectSubscriptionGroupSpec> SnapshotGroupAsync(
        AppStoreConnectSubscriptionGroupInfo group,
        CancellationToken cancellationToken)
    {
        var localizationsTask = _client.GetSubscriptionGroupLocalizationsAsync(group.Id, cancellationToken);
        var subscriptionsTask = _client.GetSubscriptionsAsync(group.Id, cancellationToken: cancellationToken);
        await Task.WhenAll(localizationsTask, subscriptionsTask).ConfigureAwait(false);
        var subscriptions = new List<AppStoreConnectSubscriptionSpec>();
        foreach (var subscription in subscriptionsTask.Result)
            subscriptions.Add(await SnapshotSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false));
        return new AppStoreConnectSubscriptionGroupSpec
        {
            Id = group.Id,
            ReferenceName = RequireObserved(group.ReferenceName, $"subscription group '{group.Id}' reference name"),
            Localizations = localizationsTask.Result.Select(item => new AppStoreConnectSubscriptionGroupLocalizationSpec
            {
                Locale = RequireObserved(item.Locale, $"subscription group localization '{item.Id}' locale"),
                Name = RequireObserved(item.Name, $"subscription group localization '{item.Id}' name"),
                CustomAppName = item.CustomAppName
            }).ToArray(),
            Subscriptions = subscriptions.ToArray()
        };
    }

    private async Task<AppStoreConnectSubscriptionSpec> SnapshotSubscriptionAsync(
        AppStoreConnectSubscriptionInfo subscription,
        CancellationToken cancellationToken)
    {
        var localizationsTask = _client.GetSubscriptionLocalizationsAsync(subscription.Id, cancellationToken);
        var pricesTask = _client.GetSubscriptionPricesAsync(subscription.Id, cancellationToken);
        var availabilitiesTask = _client.GetSubscriptionPlanAvailabilitiesAsync(subscription.Id, cancellationToken);
        await Task.WhenAll(localizationsTask, pricesTask, availabilitiesTask).ConfigureAwait(false);
        var productId = RequireObserved(subscription.ProductId, $"subscription '{subscription.Id}' product id");
        return new AppStoreConnectSubscriptionSpec
        {
            ProductId = productId,
            Name = RequireObserved(subscription.Name, $"subscription '{productId}' reference name"),
            FamilySharable = subscription.FamilySharable,
            SubscriptionPeriod = RequireObserved(subscription.SubscriptionPeriod, $"subscription '{productId}' period"),
            ReviewNote = subscription.ReviewNote,
            GroupLevel = subscription.GroupLevel,
            Localizations = localizationsTask.Result.Select(item => new AppStoreConnectSubscriptionLocalizationSpec
            {
                Locale = RequireObserved(item.Locale, $"subscription localization '{item.Id}' locale"),
                Name = RequireObserved(item.Name, $"subscription localization '{item.Id}' name"),
                Description = item.Description
            }).ToArray(),
            Prices = pricesTask.Result.Select(item => new AppStoreConnectSubscriptionPriceSpec
            {
                TerritoryId = RequireObserved(item.TerritoryId, $"subscription price '{item.Id}' territory"),
                SubscriptionPricePointId = RequireObserved(item.SubscriptionPricePointId, $"subscription price '{item.Id}' price point"),
                StartDate = NormalizeObserved(item.StartDate),
                PlanType = item.PlanType
            }).ToArray(),
            Availabilities = availabilitiesTask.Result.Select(item => new AppStoreConnectSubscriptionAvailabilitySpec
            {
                PlanType = RequireObserved(item.PlanType, $"subscription plan availability '{item.Id}' plan type"),
                AvailableInNewTerritories = item.AvailableInNewTerritories == true,
                TerritoryIds = item.TerritoryIds
            }).ToArray()
        };
    }

    private static string RequireObserved(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value!.Trim()
            : throw new InvalidOperationException($"App Store Connect omitted {field}; refusing to export an incomplete governance declaration.");

    private static string? NormalizeObserved(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
