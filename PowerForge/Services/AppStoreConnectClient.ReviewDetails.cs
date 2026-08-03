using System.Text.Json;
using System.Net.Http;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Reads App Review details for one exact App Store version.</summary>
    public async Task<AppStoreConnectReviewDetailsInfo?> GetReviewDetailsAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("Version id is required.", nameof(versionId));

        using var document = await GetJsonAsync(
            $"appStoreVersions/{Uri.EscapeDataString(versionId.Trim())}/appStoreReviewDetail",
            cancellationToken,
            returnNullOnNotFound: true).ConfigureAwait(false);
        if (document is null ||
            !document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
            return null;

        return ParseReviewDetails(data);
    }

    /// <summary>Creates App Review details for one exact App Store version.</summary>
    public Task<AppStoreConnectReviewDetailsInfo> CreateReviewDetailsAsync(
        string versionId,
        AppStoreConnectReviewDetailsInfo details,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("Version id is required.", nameof(versionId));
        ValidateReviewDetails(details);

        var body = new
        {
            data = new
            {
                type = "appStoreReviewDetails",
                attributes = CreateReviewAttributes(details),
                relationships = new
                {
                    appStoreVersion = new
                    {
                        data = new { type = "appStoreVersions", id = versionId.Trim() }
                    }
                }
            }
        };
        return PostSingleAsync("appStoreReviewDetails", body, ParseReviewDetails, cancellationToken);
    }

    /// <summary>Updates App Review contact settings without changing review notes.</summary>
    public async Task<AppStoreConnectReviewDetailsInfo> UpdateReviewDetailsAsync(
        string reviewDetailsId,
        AppStoreConnectReviewDetailsInfo details,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewDetailsId))
            throw new ArgumentException("Review details id is required.", nameof(reviewDetailsId));
        ValidateReviewDetails(details);

        var id = reviewDetailsId.Trim();
        var body = new
        {
            data = new
            {
                type = "appStoreReviewDetails",
                id,
                attributes = CreateReviewAttributes(details)
            }
        };
        using var document = await SendJsonAsync(
            new HttpMethod("PATCH"),
            $"appStoreReviewDetails/{Uri.EscapeDataString(id)}",
            body,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("App Store Connect API request returned no response body.");
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("App Store Connect API request returned no data.");
        return ParseReviewDetails(data);
    }

    private static object CreateReviewAttributes(AppStoreConnectReviewDetailsInfo details)
        => new
        {
            contactFirstName = details.ContactFirstName.Trim(),
            contactLastName = details.ContactLastName.Trim(),
            contactPhone = details.ContactPhone.Trim(),
            contactEmail = details.ContactEmail.Trim(),
            demoAccountRequired = details.DemoAccountRequired!.Value,
            demoAccountName = details.DemoAccountRequired == true ? details.DemoAccountName?.Trim() : null,
            demoAccountPassword = details.DemoAccountRequired == true ? details.DemoAccountPassword : null
        };

    private static AppStoreConnectReviewDetailsInfo ParseReviewDetails(JsonElement data)
    {
        var attributes = GetAttributes(data);
        return new AppStoreConnectReviewDetailsInfo
        {
            Id = GetString(data, "id") ?? string.Empty,
            ContactFirstName = GetString(attributes, "contactFirstName") ?? string.Empty,
            ContactLastName = GetString(attributes, "contactLastName") ?? string.Empty,
            ContactPhone = GetString(attributes, "contactPhone") ?? string.Empty,
            ContactEmail = GetString(attributes, "contactEmail") ?? string.Empty,
            DemoAccountRequired = attributes.TryGetProperty("demoAccountRequired", out var required) &&
                                  required.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? required.GetBoolean()
                : null,
            DemoAccountName = GetString(attributes, "demoAccountName"),
            DemoAccountPassword = GetString(attributes, "demoAccountPassword")
        };
    }

    private static void ValidateReviewDetails(AppStoreConnectReviewDetailsInfo? details)
    {
        if (details is null)
            throw new ArgumentNullException(nameof(details));
        if (new[] { details.ContactFirstName, details.ContactLastName, details.ContactPhone, details.ContactEmail }
            .Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("All App Review contact fields are required.", nameof(details));
        if (!details.DemoAccountRequired.HasValue)
            throw new ArgumentException("App Review demo-account requirement must be declared.", nameof(details));
        if (details.DemoAccountRequired == true &&
            (string.IsNullOrWhiteSpace(details.DemoAccountName) || string.IsNullOrWhiteSpace(details.DemoAccountPassword)))
            throw new ArgumentException("Demo-account name and password are required when App Review needs a demo account.", nameof(details));
    }
}
