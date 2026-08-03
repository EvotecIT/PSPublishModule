using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

/// <summary>Plans and converges explicit App Store commercial and compliance state.</summary>
public sealed partial class AppStoreConnectGovernanceService
{
    private readonly AppStoreConnectClient _client;
    private readonly AppStoreConnectGovernanceConfiguration _configuration = new();

    /// <summary>Creates a governance service over an authenticated App Store Connect client.</summary>
    public AppStoreConnectGovernanceService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>Reads Apple state and returns a non-mutating drift plan.</summary>
    public async Task<AppStoreConnectGovernancePlan> PlanAsync(
        AppStoreConnectGovernanceSpec spec,
        CancellationToken cancellationToken = default)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var findings = _configuration.Validate(spec).ToList();
        var plan = new AppStoreConnectGovernancePlan
        {
            AppId = spec.AppId?.Trim() ?? string.Empty,
            SpecSha256 = ComputeSpecSha256(spec),
            CheckedAtUtc = DateTimeOffset.UtcNow,
            Findings = findings.ToArray()
        };
        if (findings.Any(finding => finding.IsError)) return plan;

        var changes = new List<AppStoreConnectGovernanceChange>();
        if (spec.Pricing is not null)
            await PlanPricingAsync(spec, changes, cancellationToken).ConfigureAwait(false);
        if (spec.Availability is not null)
            await PlanAvailabilityAsync(spec, changes, cancellationToken).ConfigureAwait(false);
        if (spec.Accessibility.Length > 0)
            await PlanAccessibilityAsync(spec, changes, cancellationToken).ConfigureAwait(false);
        if (spec.EncryptionDeclarations.Length > 0)
            await PlanEncryptionAsync(spec, changes, cancellationToken).ConfigureAwait(false);
        if (spec.SubscriptionGroups.Length > 0)
            await PlanSubscriptionsAsync(spec, changes, cancellationToken).ConfigureAwait(false);
        plan.Changes = changes.ToArray();
        return plan;
    }

    /// <summary>Converges a reviewed declaration one idempotent change at a time.</summary>
    public async Task<AppStoreConnectGovernanceApplyResult> ApplyAsync(
        AppStoreConnectGovernanceApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (!request.ConfirmApply)
            throw new InvalidOperationException("Governance apply requires explicit ConfirmApply=true. Generate and review a plan first.");
        if (request.MaximumChanges is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(request.MaximumChanges), "MaximumChanges must be between 1 and 1000.");

        var started = DateTimeOffset.UtcNow;
        var applied = new List<AppStoreConnectGovernanceChange>();
        var executed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remainingReviewedChanges = request.ReviewedPlan?.Changes.ToList();
        AppStoreConnectGovernancePlan plan = new();
        try
        {
            for (var index = 0; index < request.MaximumChanges; index++)
            {
                plan = await PlanAsync(request.Spec, cancellationToken).ConfigureAwait(false);
                if (plan.Findings.Any(finding => finding.IsError))
                    return Failure(request.Spec.AppId, started, applied, plan, "Correct governance configuration errors, then generate a new plan.");
                var repeated = plan.Changes.FirstOrDefault(change => executed.Contains(ChangeFingerprint(change)));
                if (repeated is not null)
                {
                    return Failure(
                        request.Spec.AppId,
                        started,
                        applied,
                        plan,
                        "Apple still reports a change already applied in this run. PowerForge stopped to prevent a duplicate mutation; wait for App Store Connect consistency, then generate a new plan.");
                }
                if (request.ReviewedPlan is not null &&
                    !MatchesReviewedPlan(plan, request.ReviewedPlan, remainingReviewedChanges!, applied))
                {
                    return Failure(
                        request.Spec.AppId,
                        started,
                        applied,
                        plan,
                        "Current App Store Connect state no longer matches the remaining reviewed governance plan. Generate and approve a new plan before applying any further mutation.");
                }
                if (!plan.CanApply)
                    return Failure(request.Spec.AppId, started, applied, plan, "Resolve every blocked Apple API constraint shown in the plan before applying any governance mutation.");

                var next = plan.Changes.FirstOrDefault(change => change.Action != AppStoreConnectGovernanceChangeAction.Blocked);
                if (next is null)
                {
                    if (plan.BlockedCount > 0)
                        return Failure(request.Spec.AppId, started, applied, plan, "Resolve blocked Apple API constraints shown in the final plan, then rerun governance.");
                    return new AppStoreConnectGovernanceApplyResult
                    {
                        AppId = request.Spec.AppId.Trim(),
                        StartedAtUtc = started,
                        CompletedAtUtc = DateTimeOffset.UtcNow,
                        Success = true,
                        AppliedChanges = applied.ToArray(),
                        FinalPlan = plan
                    };
                }

                var mutationEffects = GetMutationEffects(plan.Changes, next);
                if (applied.Count + mutationEffects.Length > request.MaximumChanges)
                {
                    return Failure(
                        request.Spec.AppId,
                        started,
                        applied,
                        plan,
                        $"The next Apple mutation represents {mutationEffects.Length} reviewed changes and would exceed the configured maximum of {request.MaximumChanges}. Increase the limit only after reviewing the full plan.");
                }

                var fingerprint = ChangeFingerprint(next);
                if (!executed.Add(fingerprint))
                {
                    return Failure(
                        request.Spec.AppId,
                        started,
                        applied,
                        plan,
                        "Apple still reports a change already applied in this run. PowerForge stopped to prevent a duplicate mutation; wait for App Store Connect consistency, then generate a new plan.");
                }

                await ApplyChangeAsync(request.Spec, next, cancellationToken).ConfigureAwait(false);
                foreach (var effect in mutationEffects)
                {
                    executed.Add(ChangeFingerprint(effect));
                    applied.Add(effect);
                }
                if (remainingReviewedChanges is not null)
                    remainingReviewedChanges.RemoveRange(0, mutationEffects.Length);
            }

            plan = await PlanAsync(request.Spec, cancellationToken).ConfigureAwait(false);
            if (request.ReviewedPlan is not null &&
                !MatchesReviewedPlan(plan, request.ReviewedPlan, remainingReviewedChanges!, applied))
            {
                return Failure(
                    request.Spec.AppId,
                    started,
                    applied,
                    plan,
                    "Current App Store Connect state no longer matches the remaining reviewed governance plan. Generate and approve a new plan before applying any further mutation.");
            }
            if (plan.IsConverged)
            {
                return new AppStoreConnectGovernanceApplyResult
                {
                    AppId = request.Spec.AppId.Trim(),
                    StartedAtUtc = started,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Success = true,
                    AppliedChanges = applied.ToArray(),
                    FinalPlan = plan
                };
            }
            return Failure(request.Spec.AppId, started, applied, plan, $"Stopped after the configured maximum of {request.MaximumChanges} changes. Review the receipt before continuing.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(
                request.Spec.AppId,
                started,
                applied,
                plan,
                $"Apple rejected or failed the next governance change: {ex.Message} Run governance plan/Doctor again after correcting the reported cause.");
        }
    }

    private static bool MatchesReviewedPlan(
        AppStoreConnectGovernancePlan current,
        AppStoreConnectGovernancePlan reviewed,
        IReadOnlyList<AppStoreConnectGovernanceChange> remainingReviewedChanges,
        IReadOnlyList<AppStoreConnectGovernanceChange> appliedChanges)
    {
        if (!string.Equals(current.AppId, reviewed.AppId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(current.SpecSha256) ||
            !string.Equals(current.SpecSha256, reviewed.SpecSha256, StringComparison.OrdinalIgnoreCase) ||
            current.Changes.Length != remainingReviewedChanges.Count ||
            current.Findings.Length != reviewed.Findings.Length)
        {
            return false;
        }

        for (var index = 0; index < current.Changes.Length; index++)
        {
            var left = current.Changes[index];
            var right = remainingReviewedChanges[index];
            if (!string.Equals(left.Section, right.Section, StringComparison.Ordinal) ||
                !string.Equals(left.ResourceType, right.ResourceType, StringComparison.Ordinal) ||
                !string.Equals(left.Key, right.Key, StringComparison.Ordinal) ||
                !MatchesReviewedResourceId(left, right, appliedChanges) ||
                !string.Equals(left.ParentId, right.ParentId, StringComparison.Ordinal) ||
                left.Action != right.Action ||
                !string.Equals(left.Summary, right.Summary, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var index = 0; index < current.Findings.Length; index++)
        {
            var left = current.Findings[index];
            var right = reviewed.Findings[index];
            if (!string.Equals(left.Code, right.Code, StringComparison.Ordinal) ||
                !string.Equals(left.Path, right.Path, StringComparison.Ordinal) ||
                !string.Equals(left.Message, right.Message, StringComparison.Ordinal) ||
                left.IsError != right.IsError)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesReviewedResourceId(
        AppStoreConnectGovernanceChange current,
        AppStoreConnectGovernanceChange reviewed,
        IReadOnlyList<AppStoreConnectGovernanceChange> appliedChanges)
    {
        if (string.Equals(current.ResourceId, reviewed.ResourceId, StringComparison.Ordinal))
            return true;

        return string.IsNullOrWhiteSpace(reviewed.ResourceId) &&
               !string.IsNullOrWhiteSpace(current.ResourceId) &&
               string.Equals(current.ResourceType, "AccessibilityDeclaration", StringComparison.Ordinal) &&
               current.Action == AppStoreConnectGovernanceChangeAction.Publish &&
               reviewed.Action == AppStoreConnectGovernanceChangeAction.Publish &&
               appliedChanges.Any(change =>
                   string.Equals(change.ResourceType, "AccessibilityDeclaration", StringComparison.Ordinal) &&
                   string.Equals(change.Key, current.Key, StringComparison.Ordinal) &&
                   change.Action == AppStoreConnectGovernanceChangeAction.Create);
    }

    internal static string ComputeSpecSha256(AppStoreConnectGovernanceSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var canonical = new
        {
            spec.SchemaVersion,
            spec.AppId,
            spec.Pricing,
            spec.Availability,
            spec.Accessibility,
            spec.EncryptionDeclarations,
            spec.SubscriptionGroups
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(canonical);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(payload)).Replace("-", string.Empty);
    }

    private static string ChangeFingerprint(AppStoreConnectGovernanceChange change)
        => $"{change.Action}|{change.ResourceType}|{change.Key}";

    private static AppStoreConnectGovernanceChange[] GetMutationEffects(
        IReadOnlyList<AppStoreConnectGovernanceChange> changes,
        AppStoreConnectGovernanceChange next)
    {
        if (!string.Equals(next.ResourceType, "AppPriceSchedule", StringComparison.Ordinal))
        {
            if (!string.Equals(next.ResourceType, "AccessibilityDeclaration", StringComparison.Ordinal) ||
                next.Action != AppStoreConnectGovernanceChangeAction.Update)
            {
                return new[] { next };
            }

            return changes
                .TakeWhile(change =>
                    ReferenceEquals(change, next) ||
                    (string.Equals(change.ResourceType, "AccessibilityDeclaration", StringComparison.Ordinal) &&
                     string.Equals(change.Key, next.Key, StringComparison.Ordinal) &&
                     change.Action == AppStoreConnectGovernanceChangeAction.Publish))
                .ToArray();
        }

        return changes
            .TakeWhile(change =>
                ReferenceEquals(change, next) ||
                (string.Equals(change.Section, "Pricing", StringComparison.Ordinal) &&
                 string.Equals(change.ResourceType, "AppPrice", StringComparison.Ordinal)))
            .ToArray();
    }

    private async Task PlanPricingAsync(
        AppStoreConnectGovernanceSpec spec,
        List<AppStoreConnectGovernanceChange> changes,
        CancellationToken cancellationToken)
    {
        var desired = spec.Pricing!;
        var current = await _client.GetAppPriceScheduleAsync(spec.AppId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            Add(changes, "Pricing", "AppPriceSchedule", "schedule", AppStoreConnectGovernanceChangeAction.Create,
                "Create the declared app price schedule and manual prices.");
            return;
        }

        if (!Same(current.BaseTerritoryId, desired.BaseTerritoryId))
        {
            Add(changes, "Pricing", "AppPriceSchedule", "schedule", AppStoreConnectGovernanceChangeAction.Update,
                $"Change base territory from '{current.BaseTerritoryId}' to '{desired.BaseTerritoryId}'.", current.Id);
            foreach (var price in desired.Prices)
            {
                var existing = current.Prices.FirstOrDefault(item => PriceMatches(item, price));
                var key = PriceKey(price);
                Add(
                    changes,
                    "Pricing",
                    "AppPrice",
                    key,
                    existing is null ? AppStoreConnectGovernanceChangeAction.Create : AppStoreConnectGovernanceChangeAction.Update,
                    existing is null
                        ? $"Add app price for territory '{price.TerritoryId}' starting '{DisplayDate(price.StartDate)}' as part of the base-territory schedule replacement."
                        : $"Reapply app price for territory '{price.TerritoryId}' starting '{DisplayDate(price.StartDate)}' as part of the base-territory schedule replacement.",
                    current.Id);
            }
            return;
        }

        foreach (var price in desired.Prices)
        {
            if (!current.Prices.Any(existing => PriceMatches(existing, price)))
            {
                var key = PriceKey(price);
                Add(changes, "Pricing", "AppPrice", key, AppStoreConnectGovernanceChangeAction.Create,
                    $"Add app price for territory '{price.TerritoryId}' starting '{DisplayDate(price.StartDate)}'.", current.Id);
            }
        }
    }

    private async Task PlanAvailabilityAsync(
        AppStoreConnectGovernanceSpec spec,
        List<AppStoreConnectGovernanceChange> changes,
        CancellationToken cancellationToken)
    {
        var desired = spec.Availability!;
        var current = await _client.GetAppAvailabilityAsync(spec.AppId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            Add(changes, "Availability", "AppAvailability", "availability", AppStoreConnectGovernanceChangeAction.Create,
                "Create explicit app territory availability.");
            return;
        }

        if (current.AvailableInNewTerritories != desired.AvailableInNewTerritories)
        {
            Add(changes, "Availability", "AppAvailability", "availableInNewTerritories", AppStoreConnectGovernanceChangeAction.Blocked,
                "Apple's published API does not expose an update operation for availableInNewTerritories after creation; change this in App Store Connect or recreate it under human supervision.", current.Id);
        }

        foreach (var territory in desired.Territories)
        {
            var existing = current.Territories.FirstOrDefault(item => Same(item.TerritoryId, territory.TerritoryId));
            if (existing is null)
            {
                Add(changes, "Availability", "TerritoryAvailability", territory.TerritoryId, AppStoreConnectGovernanceChangeAction.Blocked,
                    $"Territory '{territory.TerritoryId}' is not present in Apple's availability relationship and cannot be added independently by the published API.", current.Id);
            }
            else if (existing.Available != territory.Available ||
                     !SameDate(existing.ReleaseDate, territory.ReleaseDate) ||
                     (territory.PreOrderEnabled.HasValue && existing.PreOrderEnabled != territory.PreOrderEnabled))
            {
                Add(changes, "Availability", "TerritoryAvailability", territory.TerritoryId, AppStoreConnectGovernanceChangeAction.Update,
                    $"Update availability for territory '{territory.TerritoryId}'.", existing.Id, current.Id);
            }
        }
    }

    private async Task PlanAccessibilityAsync(
        AppStoreConnectGovernanceSpec spec,
        List<AppStoreConnectGovernanceChange> changes,
        CancellationToken cancellationToken)
    {
        var current = await _client.GetAccessibilityDeclarationsAsync(spec.AppId, cancellationToken).ConfigureAwait(false);
        foreach (var desired in spec.Accessibility)
        {
            var existing = current.FirstOrDefault(item => Same(item.DeviceFamily, desired.DeviceFamily));
            if (existing is null)
            {
                Add(changes, "Accessibility", "AccessibilityDeclaration", desired.DeviceFamily, AppStoreConnectGovernanceChangeAction.Create,
                    $"Create reviewed accessibility facts for '{desired.DeviceFamily}'.");
                if (desired.Publish)
                {
                    Add(changes, "Accessibility", "AccessibilityDeclaration", desired.DeviceFamily, AppStoreConnectGovernanceChangeAction.Publish,
                        $"Publish the reviewed accessibility declaration for '{desired.DeviceFamily}'.");
                }
                continue;
            }
            if (!AccessibilityMatches(existing, desired))
            {
                Add(changes, "Accessibility", "AccessibilityDeclaration", desired.DeviceFamily, AppStoreConnectGovernanceChangeAction.Update,
                    $"Update reviewed accessibility facts for '{desired.DeviceFamily}'.", existing.Id);
            }
            if (desired.Publish && !Same(existing.State, "PUBLISHED"))
            {
                Add(changes, "Accessibility", "AccessibilityDeclaration", desired.DeviceFamily, AppStoreConnectGovernanceChangeAction.Publish,
                    $"Publish the reviewed accessibility declaration for '{desired.DeviceFamily}'.", existing.Id);
            }
        }
    }

    private async Task PlanEncryptionAsync(
        AppStoreConnectGovernanceSpec spec,
        List<AppStoreConnectGovernanceChange> changes,
        CancellationToken cancellationToken)
    {
        var current = await _client.GetEncryptionDeclarationsAsync(spec.AppId, cancellationToken).ConfigureAwait(false);
        foreach (var desired in spec.EncryptionDeclarations)
        {
            var matching = current.Where(existing => EncryptionMatches(existing, desired)).ToArray();
            if (matching.Length == 0)
            {
                Add(changes, "Encryption", "EncryptionDeclaration", EncryptionKey(desired), AppStoreConnectGovernanceChangeAction.Create,
                    "Create the missing human-reviewed export-compliance declaration.");
                continue;
            }
            if (!matching.Any(IsUsableEncryptionDeclaration))
            {
                var unusable = matching[0];
                var state = string.IsNullOrWhiteSpace(unusable.State) ? "UNKNOWN" : unusable.State!.Trim().ToUpperInvariant();
                Add(changes, "Encryption", "EncryptionDeclaration", EncryptionKey(desired), AppStoreConnectGovernanceChangeAction.Blocked,
                    $"Matching export-compliance declaration '{unusable.Id}' is in unusable state '{state}'. Resolve or replace it in App Store Connect, then replan.", unusable.Id);
            }
        }
    }

    private async Task PlanSubscriptionsAsync(
        AppStoreConnectGovernanceSpec spec,
        List<AppStoreConnectGovernanceChange> changes,
        CancellationToken cancellationToken)
    {
        var currentGroups = await _client.GetSubscriptionGroupsAsync(spec.AppId, cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var desiredGroup in spec.SubscriptionGroups)
        {
            var hasExplicitId = !string.IsNullOrWhiteSpace(desiredGroup.Id);
            var group = hasExplicitId
                ? currentGroups.FirstOrDefault(item => Same(item.Id, desiredGroup.Id))
                : currentGroups.FirstOrDefault(item => Same(item.ReferenceName, desiredGroup.ReferenceName));
            var groupKey = GroupKey(desiredGroup);
            if (hasExplicitId && group is null)
            {
                var sameName = currentGroups.FirstOrDefault(item => Same(item.ReferenceName, desiredGroup.ReferenceName));
                var detail = sameName is null
                    ? $"No subscription group has explicit id '{desiredGroup.Id}'."
                    : $"Reference name '{desiredGroup.ReferenceName}' belongs to group '{sameName.Id}', not explicit id '{desiredGroup.Id}'.";
                Add(changes, "Subscriptions", "SubscriptionGroup", groupKey, AppStoreConnectGovernanceChangeAction.Blocked,
                    $"{detail} Correct the reviewed id before applying; PowerForge will not create a duplicate group.", desiredGroup.Id);
                continue;
            }
            if (group is null)
            {
                Add(changes, "Subscriptions", "SubscriptionGroup", groupKey, AppStoreConnectGovernanceChangeAction.Create,
                    $"Create subscription group '{desiredGroup.ReferenceName}'.");
                continue;
            }
            if (!Same(group.ReferenceName, desiredGroup.ReferenceName))
            {
                Add(changes, "Subscriptions", "SubscriptionGroup", groupKey, AppStoreConnectGovernanceChangeAction.Update,
                    $"Rename subscription group to '{desiredGroup.ReferenceName}'.", group.Id);
            }
            await PlanSubscriptionGroupChildrenAsync(desiredGroup, group, changes, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PlanSubscriptionGroupChildrenAsync(
        AppStoreConnectSubscriptionGroupSpec desiredGroup,
        AppStoreConnectSubscriptionGroupInfo group,
        List<AppStoreConnectGovernanceChange> changes,
        CancellationToken cancellationToken)
    {
        var groupLocalizations = await _client.GetSubscriptionGroupLocalizationsAsync(group.Id, cancellationToken).ConfigureAwait(false);
        foreach (var desired in desiredGroup.Localizations)
        {
            var existing = groupLocalizations.FirstOrDefault(item => Same(item.Locale, desired.Locale));
            var key = GroupLocalizationKey(desiredGroup, desired.Locale);
            if (existing is null)
                Add(changes, "Subscriptions", "SubscriptionGroupLocalization", key, AppStoreConnectGovernanceChangeAction.Create, $"Create group localization '{desired.Locale}'.", parentId: group.Id);
            else if (!Same(existing.Name, desired.Name) || !SameOptional(existing.CustomAppName, desired.CustomAppName))
            {
                if (!SubscriptionLocalizationCanUpdate(existing.State))
                {
                    var observedState = string.IsNullOrWhiteSpace(existing.State) ? "unknown" : existing.State;
                    Add(changes, "Subscriptions", "SubscriptionGroupLocalization", key, AppStoreConnectGovernanceChangeAction.Blocked,
                        $"Subscription group localization '{desired.Locale}' is {observedState} and cannot be safely edited. Align governance with the accepted App Store Connect localization.", existing.Id, group.Id);
                }
                else
                {
                    Add(changes, "Subscriptions", "SubscriptionGroupLocalization", key, AppStoreConnectGovernanceChangeAction.Update,
                        $"Update group localization '{desired.Locale}'.", existing.Id, group.Id);
                }
            }
        }

        var subscriptions = await _client.GetSubscriptionsAsync(group.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var desired in desiredGroup.Subscriptions)
        {
            var existing = subscriptions.FirstOrDefault(item => Same(item.ProductId, desired.ProductId));
            if (existing is null)
            {
                Add(changes, "Subscriptions", "Subscription", desired.ProductId, AppStoreConnectGovernanceChangeAction.Create,
                    $"Create subscription '{desired.ProductId}'.", parentId: group.Id);
                continue;
            }
            if (!Same(existing.SubscriptionPeriod, desired.SubscriptionPeriod))
            {
                Add(changes, "Subscriptions", "Subscription", desired.ProductId, AppStoreConnectGovernanceChangeAction.Blocked,
                    $"Subscription period is immutable after creation: Apple has '{existing.SubscriptionPeriod}' while governance declares '{desired.SubscriptionPeriod}'. Correct the declaration or create a deliberately reviewed replacement product.", existing.Id, group.Id);
                continue;
            }
            if (!SubscriptionMutableFactsMatch(existing, desired))
            {
                Add(changes, "Subscriptions", "Subscription", desired.ProductId, AppStoreConnectGovernanceChangeAction.Update,
                    $"Update subscription '{desired.ProductId}'.", existing.Id, group.Id);
            }
            await PlanSubscriptionChildrenAsync(desired, existing, changes, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PlanSubscriptionChildrenAsync(
        AppStoreConnectSubscriptionSpec desired,
        AppStoreConnectSubscriptionInfo subscription,
        List<AppStoreConnectGovernanceChange> changes,
        CancellationToken cancellationToken)
    {
        var localizations = await _client.GetSubscriptionLocalizationsAsync(subscription.Id, cancellationToken).ConfigureAwait(false);
        foreach (var desiredLocalization in desired.Localizations)
        {
            var existing = localizations.FirstOrDefault(item => Same(item.Locale, desiredLocalization.Locale));
            var key = SubscriptionChildKey(desired.ProductId, desiredLocalization.Locale);
            if (existing is null)
                Add(changes, "Subscriptions", "SubscriptionLocalization", key, AppStoreConnectGovernanceChangeAction.Create, $"Create subscription localization '{desiredLocalization.Locale}'.", parentId: subscription.Id);
            else if (!Same(existing.Name, desiredLocalization.Name) || !SameOptional(existing.Description, desiredLocalization.Description))
            {
                if (!SubscriptionLocalizationCanUpdate(existing.State))
                {
                    var observedState = string.IsNullOrWhiteSpace(existing.State) ? "unknown" : existing.State;
                    Add(changes, "Subscriptions", "SubscriptionLocalization", key, AppStoreConnectGovernanceChangeAction.Blocked,
                        $"Subscription localization '{desiredLocalization.Locale}' is {observedState} and cannot be safely edited. Align governance with the accepted App Store Connect localization.", existing.Id, subscription.Id);
                }
                else
                {
                    Add(changes, "Subscriptions", "SubscriptionLocalization", key, AppStoreConnectGovernanceChangeAction.Update,
                        $"Update subscription localization '{desiredLocalization.Locale}'.", existing.Id, subscription.Id);
                }
            }
        }

        var prices = await _client.GetSubscriptionPricesAsync(subscription.Id, cancellationToken).ConfigureAwait(false);
        foreach (var desiredPrice in desired.Prices)
        {
            if (!prices.Any(existing => SubscriptionPriceMatches(existing, desiredPrice)))
                Add(changes, "Subscriptions", "SubscriptionPrice", SubscriptionPriceKey(desired.ProductId, desiredPrice), AppStoreConnectGovernanceChangeAction.Create, $"Add subscription price for '{desiredPrice.TerritoryId}'.", parentId: subscription.Id);
        }

        if (desired.IntroductoryOffers.Length > 0)
        {
            var introductoryOffers = await _client.GetSubscriptionIntroductoryOffersAsync(subscription.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var desiredOffer in desired.IntroductoryOffers)
            {
                foreach (var territoryId in ResolveIntroductoryOfferTerritories(desired, desiredOffer))
                {
                    if (!introductoryOffers.Any(existing => SubscriptionIntroductoryOfferMatches(existing, desiredOffer, territoryId)))
                    {
                        Add(changes, "Subscriptions", "SubscriptionIntroductoryOffer", SubscriptionIntroductoryOfferKey(desired.ProductId, desiredOffer, territoryId),
                            AppStoreConnectGovernanceChangeAction.Create, $"Add {desiredOffer.OfferMode} introductory offer for '{territoryId}'.", parentId: subscription.Id);
                    }
                }
            }
        }

        if (desired.Availabilities.Length > 0)
        {
            var availabilities = await _client.GetSubscriptionPlanAvailabilitiesAsync(subscription.Id, cancellationToken).ConfigureAwait(false);
            foreach (var desiredAvailability in desired.Availabilities)
            {
                var existing = availabilities.FirstOrDefault(item => Same(item.PlanType, desiredAvailability.PlanType));
                var key = SubscriptionChildKey(desired.ProductId, desiredAvailability.PlanType);
                if (existing is null)
                    Add(changes, "Subscriptions", "SubscriptionPlanAvailability", key, AppStoreConnectGovernanceChangeAction.Create, $"Create '{desiredAvailability.PlanType}' plan availability.", parentId: subscription.Id);
                else if (!SubscriptionAvailabilityMatches(existing, desiredAvailability))
                    Add(changes, "Subscriptions", "SubscriptionPlanAvailability", key, AppStoreConnectGovernanceChangeAction.Update, $"Update '{desiredAvailability.PlanType}' plan availability.", existing.Id, subscription.Id);
            }
        }
    }

    private static bool SubscriptionLocalizationCanUpdate(string? state) =>
        Same(state, "PREPARE_FOR_SUBMISSION") || Same(state, "REJECTED");

    private static void Add(List<AppStoreConnectGovernanceChange> changes, string section, string type, string key,
        AppStoreConnectGovernanceChangeAction action, string summary, string? resourceId = null, string? parentId = null) =>
        changes.Add(new AppStoreConnectGovernanceChange { Section = section, ResourceType = type, Key = key, Action = action, Summary = summary, ResourceId = resourceId, ParentId = parentId });

    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameOptional(string? left, string? right) => Same(left ?? string.Empty, right ?? string.Empty);
    private static bool SameDate(string? left, string? right) => SameOptional(left, right);
    private static string DisplayDate(string? value) => string.IsNullOrWhiteSpace(value) ? "immediately" : value!;
    private static string GroupKey(AppStoreConnectSubscriptionGroupSpec group) => string.IsNullOrWhiteSpace(group.Id) ? group.ReferenceName.Trim() : group.Id!.Trim();
    private static string GroupLocalizationKey(AppStoreConnectSubscriptionGroupSpec group, string locale) => GroupKey(group) + "|" + locale.Trim();
    private static string SubscriptionChildKey(string productId, string value) => productId.Trim() + "|" + value.Trim();
    private static string PriceKey(AppStoreConnectAppPriceSpec price) => string.Join("|", price.TerritoryId.Trim(), price.StartDate?.Trim() ?? string.Empty, price.EndDate?.Trim() ?? string.Empty, price.AppPricePointId.Trim());
    private static string SubscriptionPriceKey(string productId, AppStoreConnectSubscriptionPriceSpec price) => string.Join("|", productId.Trim(), price.TerritoryId.Trim(), price.StartDate?.Trim() ?? string.Empty, price.PlanType?.Trim().ToUpperInvariant() ?? string.Empty, price.SubscriptionPricePointId.Trim());
    private static string SubscriptionIntroductoryOfferKey(string productId, AppStoreConnectSubscriptionIntroductoryOfferSpec offer, string territoryId) => string.Join("|", productId.Trim(), territoryId.Trim(), offer.StartDate?.Trim() ?? string.Empty, offer.EndDate?.Trim() ?? string.Empty, offer.Duration.Trim().ToUpperInvariant(), offer.OfferMode.Trim().ToUpperInvariant(), offer.NumberOfPeriods, offer.SubscriptionPricePointId?.Trim() ?? string.Empty);
    private static string SubscriptionIntroductoryOfferShapeKey(string duration, string offerMode, int numberOfPeriods, string? startDate, string? endDate, string? pricePointId) => string.Join("|", duration.Trim().ToUpperInvariant(), offerMode.Trim().ToUpperInvariant(), numberOfPeriods, startDate?.Trim() ?? string.Empty, endDate?.Trim() ?? string.Empty, pricePointId?.Trim() ?? string.Empty);
    private static string[] ResolveIntroductoryOfferTerritories(AppStoreConnectSubscriptionSpec subscription, AppStoreConnectSubscriptionIntroductoryOfferSpec offer) =>
        offer.TerritoryIds is { Length: > 0 }
            ? offer.TerritoryIds
            : subscription.Availabilities.Single(availability => Same(availability.PlanType, offer.TerritoriesFromPlanType)).TerritoryIds;
    private static string EncryptionKey(AppStoreConnectEncryptionDeclarationSpec value) => string.Join("|", value.AppDescription.Trim(), value.ContainsProprietaryCryptography, value.ContainsThirdPartyCryptography, value.AvailableOnFrenchStore);

    private static bool PriceMatches(AppStoreConnectAppPriceInfo actual, AppStoreConnectAppPriceSpec desired) => Same(actual.AppPricePointId, desired.AppPricePointId) && Same(actual.TerritoryId, desired.TerritoryId) && SameDate(actual.StartDate, desired.StartDate) && SameDate(actual.EndDate, desired.EndDate);
    private static bool EncryptionMatches(AppStoreConnectEncryptionDeclarationInfo actual, AppStoreConnectEncryptionDeclarationSpec desired) => Same(actual.AppDescription, desired.AppDescription) && actual.ContainsProprietaryCryptography == desired.ContainsProprietaryCryptography && actual.ContainsThirdPartyCryptography == desired.ContainsThirdPartyCryptography && actual.AvailableOnFrenchStore == desired.AvailableOnFrenchStore;
    private static bool IsUsableEncryptionDeclaration(AppStoreConnectEncryptionDeclarationInfo declaration) => Same(declaration.State, "APPROVED");
    private static bool SubscriptionMutableFactsMatch(AppStoreConnectSubscriptionInfo actual, AppStoreConnectSubscriptionSpec desired) => Same(actual.Name, desired.Name) && (!desired.FamilySharable.HasValue || actual.FamilySharable == desired.FamilySharable) && (desired.ReviewNote is null || SameOptional(actual.ReviewNote, desired.ReviewNote)) && (!desired.GroupLevel.HasValue || actual.GroupLevel == desired.GroupLevel);
    private static bool SubscriptionPriceMatches(AppStoreConnectSubscriptionPriceInfo actual, AppStoreConnectSubscriptionPriceSpec desired) => Same(actual.TerritoryId, desired.TerritoryId) && Same(actual.SubscriptionPricePointId, desired.SubscriptionPricePointId) && SameDate(actual.StartDate, desired.StartDate) && (desired.PlanType is null || SameOptional(actual.PlanType, desired.PlanType)) && (!desired.PreserveCurrentPrice.HasValue || actual.Preserved == desired.PreserveCurrentPrice);
    private static bool SubscriptionIntroductoryOfferMatches(AppStoreConnectSubscriptionIntroductoryOfferInfo actual, AppStoreConnectSubscriptionIntroductoryOfferSpec desired, string territoryId) => Same(actual.TerritoryId, territoryId) && Same(actual.Duration, desired.Duration) && Same(actual.OfferMode, desired.OfferMode) && actual.NumberOfPeriods == desired.NumberOfPeriods && SameDate(actual.StartDate, desired.StartDate) && SameDate(actual.EndDate, desired.EndDate) && SameOptional(actual.SubscriptionPricePointId, desired.SubscriptionPricePointId);
    private static bool SubscriptionAvailabilityMatches(AppStoreConnectSubscriptionAvailabilityInfo actual, AppStoreConnectSubscriptionAvailabilitySpec desired) => actual.AvailableInNewTerritories == desired.AvailableInNewTerritories && new HashSet<string>(actual.TerritoryIds, StringComparer.OrdinalIgnoreCase).SetEquals(desired.TerritoryIds);

    private static bool AccessibilityMatches(AppStoreConnectAccessibilityDeclarationInfo actual, AppStoreConnectAccessibilityDeclarationSpec desired) =>
        (!desired.SupportsAudioDescriptions.HasValue || actual.SupportsAudioDescriptions == desired.SupportsAudioDescriptions) &&
        (!desired.SupportsCaptions.HasValue || actual.SupportsCaptions == desired.SupportsCaptions) &&
        (!desired.SupportsDarkInterface.HasValue || actual.SupportsDarkInterface == desired.SupportsDarkInterface) &&
        (!desired.SupportsDifferentiateWithoutColorAlone.HasValue || actual.SupportsDifferentiateWithoutColorAlone == desired.SupportsDifferentiateWithoutColorAlone) &&
        (!desired.SupportsLargerText.HasValue || actual.SupportsLargerText == desired.SupportsLargerText) &&
        (!desired.SupportsReducedMotion.HasValue || actual.SupportsReducedMotion == desired.SupportsReducedMotion) &&
        (!desired.SupportsSufficientContrast.HasValue || actual.SupportsSufficientContrast == desired.SupportsSufficientContrast) &&
        (!desired.SupportsVoiceControl.HasValue || actual.SupportsVoiceControl == desired.SupportsVoiceControl) &&
        (!desired.SupportsVoiceover.HasValue || actual.SupportsVoiceover == desired.SupportsVoiceover);

    private static AppStoreConnectGovernanceApplyResult Failure(string appId, DateTimeOffset started,
        List<AppStoreConnectGovernanceChange> applied, AppStoreConnectGovernancePlan plan, string nextAction) =>
        new()
        {
            AppId = appId?.Trim() ?? string.Empty,
            StartedAtUtc = started,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Success = false,
            AppliedChanges = applied.ToArray(),
            FinalPlan = plan,
            NextActions = new[] { nextAction }
        };
}
