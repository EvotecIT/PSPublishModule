using System.Globalization;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Lists existing team users, optionally filtering by Apple Account username. Does not modify access.</summary>
    public Task<AppStoreConnectUserInfo[]> GetUsersAsync(
        string? username = null, int limit = 200, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(username)) query["filter[username]"] = username!.Trim();
        return GetArrayAsync("users" + BuildQuery(query), ParseUser, cancellationToken);
    }

    /// <summary>Lists apps explicitly visible to an existing user. Does not change user roles or app access.</summary>
    public Task<AppStoreConnectAppInfo[]> GetVisibleAppsForUserAsync(
        string userId, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));
        return GetArrayAsync(
            $"users/{Uri.EscapeDataString(userId.Trim())}/visibleApps?limit={ClampLimit(limit).ToString(CultureInfo.InvariantCulture)}",
            ParseApp, cancellationToken);
    }

    /// <summary>Lists apps associated with a beta tester, for verifying the scope of an existing tester identity.</summary>
    public Task<AppStoreConnectAppInfo[]> GetAppsForBetaTesterAsync(
        string betaTesterId, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(betaTesterId))
            throw new ArgumentException("Beta tester id is required.", nameof(betaTesterId));
        return GetArrayAsync(
            $"betaTesters/{Uri.EscapeDataString(betaTesterId.Trim())}/apps?limit={ClampLimit(limit).ToString(CultureInfo.InvariantCulture)}",
            ParseApp, cancellationToken);
    }

    private static AppStoreConnectUserInfo ParseUser(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectUserInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Username = GetString(attrs, "username"),
            AllAppsVisible = GetBool(attrs, "allAppsVisible"),
            Roles = attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                ? roles.EnumerateArray().Where(static value => value.ValueKind == JsonValueKind.String).Select(static value => value.GetString()!).ToArray()
                : Array.Empty<string>()
        };
    }
}
