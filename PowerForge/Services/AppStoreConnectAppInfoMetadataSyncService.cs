namespace PowerForge;

/// <summary>
/// Syncs localized app-level App Store information such as the name, subtitle, and privacy policy URL.
/// </summary>
public sealed class AppStoreConnectAppInfoMetadataSyncService
{
    private static readonly HashSet<string> EditableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "PREPARE_FOR_SUBMISSION",
        "READY_FOR_REVIEW",
        "INVALID_BINARY",
        "DEVELOPER_REJECTED",
        "METADATA_REJECTED",
        "REJECTED"
    };

    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes an app information metadata sync service.
    /// </summary>
    public AppStoreConnectAppInfoMetadataSyncService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Applies localized metadata to the editable App Information localization for an app.
    /// </summary>
    public async Task<AppStoreConnectAppInfoMetadataSyncResult> SyncAsync(
        AppStoreConnectAppInfoMetadataSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        var spec = request.Spec ?? throw new ArgumentException("Spec is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(spec.AppId))
            throw new ArgumentException("Spec.AppId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(spec.Locale))
            throw new ArgumentException("Spec.Locale is required.", nameof(request));

        var appInfo = await ResolveAppInfoAsync(spec, cancellationToken).ConfigureAwait(false);
        var localization = (await _client.GetAppInfoLocalizationsAsync(
            appInfo.Id,
            spec.Locale,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"App Information localization '{spec.Locale}' was not found for resource '{appInfo.Id}'.");

        var updated = await _client.UpdateAppInfoLocalizationAsync(
            localization.Id,
            spec.Metadata,
            cancellationToken).ConfigureAwait(false);

        return new AppStoreConnectAppInfoMetadataSyncResult
        {
            AppInfo = appInfo,
            Before = localization,
            After = updated,
            UpdatedFields = AppStoreConnectClient.GetSuppliedAppInfoLocalizationFields(spec.Metadata)
        };
    }

    private async Task<AppStoreConnectAppInformationInfo> ResolveAppInfoAsync(
        AppStoreConnectAppInfoMetadataSpec spec,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(spec.AppInfoId))
        {
            return new AppStoreConnectAppInformationInfo
            {
                Id = spec.AppInfoId!.Trim()
            };
        }

        var appInfos = await _client.GetAppInfosAsync(spec.AppId, limit: 50, cancellationToken).ConfigureAwait(false);
        var editable = appInfos.FirstOrDefault(IsEditable);
        if (editable is not null)
            return editable;
        if (appInfos.Length == 1 && string.IsNullOrWhiteSpace(GetState(appInfos[0])))
            return appInfos[0];

        var states = appInfos.Length == 0
            ? "none"
            : string.Join(", ", appInfos.Select(info => GetState(info) ?? "unknown"));
        throw new InvalidOperationException(
            $"No editable App Information resource was found for app '{spec.AppId}'. Current states: {states}. Create a new App Store version or provide AppInfoId explicitly.");
    }

    private static bool IsEditable(AppStoreConnectAppInformationInfo appInfo)
    {
        var state = GetState(appInfo);
        return state is not null && EditableStates.Contains(state);
    }

    private static string? GetState(AppStoreConnectAppInformationInfo appInfo)
        => string.IsNullOrWhiteSpace(appInfo.State) ? appInfo.AppStoreState : appInfo.State;
}
