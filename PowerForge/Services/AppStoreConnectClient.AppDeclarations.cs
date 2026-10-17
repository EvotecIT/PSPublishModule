using System.Net.Http;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Reads one App Information resource and its category identifiers.</summary>
    public Task<AppStoreConnectAppInformationInfo?> GetAppInformationAsync(
        string appInfoId, CancellationToken cancellationToken = default)
    {
        RequireValue(appInfoId, nameof(appInfoId), "App Information id");
        return GetSingleAsync($"appInfos/{Uri.EscapeDataString(appInfoId.Trim())}?include=primaryCategory,secondaryCategory", ParseAppInformation, cancellationToken);
    }

    /// <summary>Sets supplied category identifiers on one exact App Information resource; null leaves a category unchanged.</summary>
    public async Task<AppStoreConnectAppInformationInfo> UpdateAppCategoriesAsync(
        string appInfoId, string? primaryCategoryId = null, string? secondaryCategoryId = null,
        CancellationToken cancellationToken = default)
    {
        var relationships = new Dictionary<string, object>(StringComparer.Ordinal);
        AddCategory("primaryCategory", primaryCategoryId);
        AddCategory("secondaryCategory", secondaryCategoryId);
        if (relationships.Count == 0) throw new ArgumentException("Supply at least one category identifier.");
        return await UpdateAppCategoryRelationshipsAsync(appInfoId, relationships, cancellationToken).ConfigureAwait(false);

        void AddCategory(string name, string? category)
        {
            if (category is null) return;
            RequireValue(category, nameof(category), "Category identifier");
            relationships.Add(name, new { data = new { type = "appCategories", id = category.Trim() } });
        }
    }

    /// <summary>Explicitly removes the optional secondary category while leaving the primary category and other app information unchanged.</summary>
    public Task<AppStoreConnectAppInformationInfo> ClearAppSecondaryCategoryAsync(
        string appInfoId, CancellationToken cancellationToken = default)
        => UpdateAppCategoryRelationshipsAsync(appInfoId,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["secondaryCategory"] = new { data = (object?)null }
            }, cancellationToken);

    private async Task<AppStoreConnectAppInformationInfo> UpdateAppCategoryRelationshipsAsync(
        string appInfoId, Dictionary<string, object> relationships, CancellationToken cancellationToken)
    {
        RequireValue(appInfoId, nameof(appInfoId), "App Information id");
        var id = appInfoId.Trim();
        using var document = await SendJsonAsync(new HttpMethod("PATCH"), $"appInfos/{Uri.EscapeDataString(id)}",
            new { data = new { type = "appInfos", id, relationships } }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Apple returned no App Information response.");
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Apple returned no App Information data.");
        // PATCH responses can contain relationship links without category linkage data.
        return await GetAppInformationAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Apple returned no updated App Information resource.");
    }

    /// <summary>Records the publisher's explicit content-rights declaration without changing other app attributes.</summary>
    public async Task<AppStoreConnectAppInfo> UpdateAppContentRightsAsync(
        string appId, bool usesThirdPartyContent, CancellationToken cancellationToken = default)
    {
        RequireValue(appId, nameof(appId), "App id");
        var id = appId.Trim();
        var attributes = new { contentRightsDeclaration = usesThirdPartyContent ? "USES_THIRD_PARTY_CONTENT" : "DOES_NOT_USE_THIRD_PARTY_CONTENT" };
        using var document = await SendJsonAsync(new HttpMethod("PATCH"), $"apps/{Uri.EscapeDataString(id)}",
            new { data = new { type = "apps", id, attributes } }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Apple returned no app declaration response.");
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Apple returned no app declaration data.");
        return ParseApp(data);
    }
}
