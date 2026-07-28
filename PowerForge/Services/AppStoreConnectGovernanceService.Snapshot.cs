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
                AvailableInNewTerritories = RequireObserved(availability.AvailableInNewTerritories, "app availability availableInNewTerritories"),
                Territories = availability.Territories.Select(item => new AppStoreConnectTerritoryAvailabilitySpec
                {
                    TerritoryId = RequireObserved(item.TerritoryId, $"territory availability '{item.Id}' territory"),
                    Available = RequireObserved(item.Available, $"territory availability '{item.Id}' available"),
                    ReleaseDate = NormalizeObserved(item.ReleaseDate),
                    PreOrderEnabled = item.PreOrderEnabled
                }).ToArray()
            },
            Accessibility = accessibilityTask.Result.Select(item => new AppStoreConnectAccessibilityDeclarationSpec
            {
                DeviceFamily = RequireObserved(item.DeviceFamily, $"accessibility declaration '{item.Id}' device family"),
                Publish = Same(RequireObserved(item.State, $"accessibility declaration '{item.Id}' state"), "PUBLISHED"),
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
                ContainsProprietaryCryptography = RequireObserved(item.ContainsProprietaryCryptography, $"encryption declaration '{item.Id}' containsProprietaryCryptography"),
                ContainsThirdPartyCryptography = RequireObserved(item.ContainsThirdPartyCryptography, $"encryption declaration '{item.Id}' containsThirdPartyCryptography"),
                AvailableOnFrenchStore = RequireObserved(item.AvailableOnFrenchStore, $"encryption declaration '{item.Id}' availableOnFrenchStore")
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
        var introductoryOffersTask = _client.GetSubscriptionIntroductoryOffersAsync(subscription.Id, cancellationToken: cancellationToken);
        var availabilitiesTask = _client.GetSubscriptionPlanAvailabilitiesAsync(subscription.Id, cancellationToken);
        await Task.WhenAll(localizationsTask, pricesTask, introductoryOffersTask, availabilitiesTask).ConfigureAwait(false);
        var productId = RequireObserved(subscription.ProductId, $"subscription '{subscription.Id}' product id");
        var introductoryOffers = introductoryOffersTask.Result.Select(item => new
        {
            Duration = RequireObserved(item.Duration, $"subscription introductory offer '{item.Id}' duration"),
            OfferMode = RequireObserved(item.OfferMode, $"subscription introductory offer '{item.Id}' mode"),
            NumberOfPeriods = RequireObserved(item.NumberOfPeriods, $"subscription introductory offer '{item.Id}' numberOfPeriods"),
            TerritoryId = RequireObserved(item.TerritoryId, $"subscription introductory offer '{item.Id}' territory"),
            StartDate = NormalizeObserved(item.StartDate),
            EndDate = NormalizeObserved(item.EndDate),
            SubscriptionPricePointId = NormalizeObserved(item.SubscriptionPricePointId)
        }).GroupBy(item => SubscriptionIntroductoryOfferShapeKey(item.Duration, item.OfferMode, item.NumberOfPeriods, item.StartDate, item.EndDate, item.SubscriptionPricePointId))
          .Select(group =>
          {
              var first = group.First();
              return new AppStoreConnectSubscriptionIntroductoryOfferSpec
              {
                  Duration = first.Duration,
                  OfferMode = first.OfferMode,
                  NumberOfPeriods = first.NumberOfPeriods,
                  TerritoryIds = group.Select(item => item.TerritoryId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                  StartDate = first.StartDate,
                  EndDate = first.EndDate,
                  SubscriptionPricePointId = first.SubscriptionPricePointId
              };
          }).ToArray();
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
                PreserveCurrentPrice = item.Preserved,
                PlanType = item.PlanType
            }).ToArray(),
            IntroductoryOffers = introductoryOffers,
            Availabilities = availabilitiesTask.Result.Select(item => new AppStoreConnectSubscriptionAvailabilitySpec
            {
                PlanType = RequireObserved(item.PlanType, $"subscription plan availability '{item.Id}' plan type"),
                AvailableInNewTerritories = RequireObserved(item.AvailableInNewTerritories, $"subscription plan availability '{item.Id}' availableInNewTerritories"),
                TerritoryIds = item.TerritoryIds
            }).ToArray()
        };
    }

    private static string RequireObserved(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value!.Trim()
            : throw new InvalidOperationException($"App Store Connect omitted {field}; refusing to export an incomplete governance declaration.");

    private static bool RequireObserved(bool? value, string field) =>
        value ?? throw new InvalidOperationException($"App Store Connect omitted {field}; refusing to export an incomplete governance declaration.");

    private static int RequireObserved(int? value, string field) =>
        value ?? throw new InvalidOperationException($"App Store Connect omitted {field}; refusing to export an incomplete governance declaration.");

    private static string? NormalizeObserved(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
