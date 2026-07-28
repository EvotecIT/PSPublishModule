using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>
    /// Reads the release-critical control-plane inventory for an app and optional version.
    /// </summary>
    public async Task<AppStoreConnectControlPlaneState> GetControlPlaneStateAsync(
        string appId,
        string? versionId = null,
        CancellationToken cancellationToken = default)
        => await GetControlPlaneStateAsync(appId, versionId, buildId: null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Reads the release-critical control-plane inventory and scopes beta feedback to one exact build.
    /// </summary>
    public async Task<AppStoreConnectControlPlaneState> GetControlPlaneStateAsync(
        string appId,
        string? versionId,
        string? buildId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("App id is required.", nameof(appId));

        appId = appId.Trim();
        versionId = string.IsNullOrWhiteSpace(versionId) ? null : versionId!.Trim();
        buildId = string.IsNullOrWhiteSpace(buildId) ? null : buildId!.Trim();
        var appInfos = await GetAppInfosAsync(appId, limit: 50, cancellationToken).ConfigureAwait(false);
        var ageRatingDeclared = false;
        foreach (var appInfo in appInfos)
        {
            using var ageRating = await GetJsonAsync(
                $"appInfos/{Uri.EscapeDataString(appInfo.Id)}/ageRatingDeclaration",
                cancellationToken,
                returnNullOnNotFound: true).ConfigureAwait(false);
            if (HasSingleResource(ageRating))
            {
                ageRatingDeclared = true;
                break;
            }
        }

        using var reviewDetails = versionId is null
            ? null
            : await GetJsonAsync(
                $"appStoreVersions/{Uri.EscapeDataString(versionId)}/appStoreReviewDetail",
                cancellationToken,
                returnNullOnNotFound: true).ConfigureAwait(false);
        using var phasedRelease = versionId is null
            ? null
            : await GetJsonAsync(
                $"appStoreVersions/{Uri.EscapeDataString(versionId)}/appStoreVersionPhasedRelease",
                cancellationToken,
                returnNullOnNotFound: true).ConfigureAwait(false);
        using var encryption = await GetJsonAsync(
            $"appEncryptionDeclarations?filter%5Bapp%5D={Uri.EscapeDataString(appId)}&limit=1",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        using var accessibility = await GetJsonAsync(
            $"apps/{Uri.EscapeDataString(appId)}/accessibilityDeclarations?limit=1",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        using var priceSchedule = await GetJsonAsync(
            $"apps/{Uri.EscapeDataString(appId)}/appPriceSchedule",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        using var availability = await GetJsonAsync(
            $"apps/{Uri.EscapeDataString(appId)}/appAvailabilityV2",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        using var inAppPurchases = await GetJsonAsync(
            $"apps/{Uri.EscapeDataString(appId)}/inAppPurchasesV2?limit=1",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        var webhooks = await GetWebhooksAsync(appId, limit: 200, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var crashFeedback = buildId is null
            ? null
            : await GetJsonAsync(
                $"apps/{Uri.EscapeDataString(appId)}/betaFeedbackCrashSubmissions?filter%5Bbuild%5D={Uri.EscapeDataString(buildId)}&limit=3&sort=-createdDate",
                cancellationToken,
                returnNullOnNotFound: true).ConfigureAwait(false);
        using var screenshotFeedback = buildId is null
            ? null
            : await GetJsonAsync(
                $"apps/{Uri.EscapeDataString(appId)}/betaFeedbackScreenshotSubmissions?filter%5Bbuild%5D={Uri.EscapeDataString(buildId)}&limit=3&sort=-createdDate",
                cancellationToken,
                returnNullOnNotFound: true).ConfigureAwait(false);
        using var customerReviews = await GetJsonAsync(
            $"apps/{Uri.EscapeDataString(appId)}/customerReviews?limit=1",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);

        var subscriptions = await GetSubscriptionsForAppAsync(appId, limit: 200, cancellationToken).ConfigureAwait(false);
        var review = GetReviewDetailsCompleteness(reviewDetails);
        return new AppStoreConnectControlPlaneState
        {
            AppId = appId,
            VersionId = versionId,
            ReviewDetailsExist = review.Exists,
            ReviewContactConfigured = review.ContactConfigured,
            ReviewDemoAccountRequirementDeclared = review.RequirementDeclared,
            ReviewDemoAccountRequired = review.DemoAccountRequired,
            ReviewDemoCredentialsConfigured = review.DemoCredentialsConfigured,
            ReviewDetailsConfigured = review.Complete,
            AgeRatingDeclared = ageRatingDeclared,
            EncryptionDeclarationCount = GetResourceCount(encryption),
            AccessibilityDeclarationCount = GetResourceCount(accessibility),
            PriceScheduleConfigured = HasSingleResource(priceSchedule),
            AvailabilityConfigured = HasSingleResource(availability),
            PhasedReleaseState = GetResourceAttribute(phasedRelease, "phasedReleaseState"),
            InAppPurchaseCount = GetResourceCount(inAppPurchases),
            SubscriptionCount = subscriptions.Length,
            WebhookCount = webhooks.Count(static webhook => webhook.Enabled == true),
            BetaCrashFeedbackCount = GetResourceCount(crashFeedback),
            BetaScreenshotFeedbackCount = GetResourceCount(screenshotFeedback),
            RecentCrashFeedback = GetFeedbackItems(crashFeedback, "crash"),
            RecentScreenshotFeedback = GetFeedbackItems(screenshotFeedback, "screenshot"),
            CustomerReviewCount = GetResourceCount(customerReviews)
        };
    }

    private static bool HasSingleResource(JsonDocument? document)
        => document is not null &&
           document.RootElement.TryGetProperty("data", out var data) &&
           data.ValueKind == JsonValueKind.Object;

    private static (
        bool Exists,
        bool ContactConfigured,
        bool RequirementDeclared,
        bool? DemoAccountRequired,
        bool DemoCredentialsConfigured,
        bool Complete) GetReviewDetailsCompleteness(JsonDocument? document)
    {
        if (document is null ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("attributes", out var attributes) ||
            attributes.ValueKind != JsonValueKind.Object)
        {
            return (false, false, false, null, false, false);
        }

        var contactConfigured = new[]
            {
                "contactFirstName", "contactLastName", "contactPhone", "contactEmail"
            }
            .All(name => !string.IsNullOrWhiteSpace(GetString(attributes, name)));
        var requirementDeclared = attributes.TryGetProperty("demoAccountRequired", out var requiredElement) &&
                                  (requiredElement.ValueKind == JsonValueKind.True ||
                                   requiredElement.ValueKind == JsonValueKind.False);
        bool? demoAccountRequired = requirementDeclared ? requiredElement.GetBoolean() : null;
        var demoCredentialsConfigured = requirementDeclared &&
                                        (demoAccountRequired == false ||
                                         (!string.IsNullOrWhiteSpace(GetString(attributes, "demoAccountName")) &&
                                          !string.IsNullOrWhiteSpace(GetString(attributes, "demoAccountPassword"))));
        return (
            true,
            contactConfigured,
            requirementDeclared,
            demoAccountRequired,
            demoCredentialsConfigured,
            contactConfigured && requirementDeclared && demoCredentialsConfigured);
    }

    private static int GetResourceCount(JsonDocument? document)
    {
        if (document is null)
            return 0;
        var root = document.RootElement;
        if (root.TryGetProperty("meta", out var meta) &&
            meta.TryGetProperty("paging", out var paging) &&
            paging.TryGetProperty("total", out var total) &&
            total.TryGetInt32(out var totalValue))
            return totalValue;
        if (!root.TryGetProperty("data", out var data))
            return 0;
        return data.ValueKind switch
        {
            JsonValueKind.Array => data.GetArrayLength(),
            JsonValueKind.Object => 1,
            _ => 0
        };
    }

    private static string? GetResourceAttribute(JsonDocument? document, string name)
    {
        if (document is null ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("attributes", out var attributes))
            return null;
        return GetString(attributes, name);
    }

    private static AppStoreConnectBetaFeedbackItem[] GetFeedbackItems(JsonDocument? document, string kind)
    {
        if (document is null ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            return Array.Empty<AppStoreConnectBetaFeedbackItem>();

        return data.EnumerateArray().Select(item =>
        {
            var attributes = item.TryGetProperty("attributes", out var value) ? value : default;
            var comment = attributes.ValueKind == JsonValueKind.Object ? GetString(attributes, "comment") : null;
            if (comment?.Length > 240)
                comment = comment.Substring(0, 240) + "…";
            return new AppStoreConnectBetaFeedbackItem
            {
                Id = GetString(item, "id") ?? string.Empty,
                Kind = kind,
                CreatedAt = attributes.ValueKind == JsonValueKind.Object ? GetDateTimeOffset(attributes, "createdDate") : null,
                Comment = comment,
                DeviceModel = attributes.ValueKind == JsonValueKind.Object ? GetString(attributes, "deviceModel") : null,
                OsVersion = attributes.ValueKind == JsonValueKind.Object ? GetString(attributes, "osVersion") : null,
                AppPlatform = attributes.ValueKind == JsonValueKind.Object ? GetString(attributes, "appPlatform") : null,
                BuildBundleId = attributes.ValueKind == JsonValueKind.Object ? GetString(attributes, "buildBundleId") : null
            };
        }).ToArray();
    }
}
