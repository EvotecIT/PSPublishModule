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
        "WAITING_FOR_REVIEW",
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
    /// Applies localized metadata to the editable App Information localization for an app,
    /// creates the localization when missing, or proves convergence without mutating locked resources.
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

        if (spec.Metadata is null)
            throw new ArgumentException("Spec.Metadata is required.", nameof(request));

        var appInfos = await _client.GetAppInfosAsync(spec.AppId, limit: 50, cancellationToken).ConfigureAwait(false);
        var appInfo = ResolveAppInfo(spec, appInfos);
        var localizations = await LoadAppInfoLocalizationsAsync(spec, appInfos, cancellationToken).ConfigureAwait(false);
        AssertLockedAppInfoConverged(spec, appInfos, localizations, appInfo);
        if (appInfo is null || !IsEditable(appInfo))
            return CreateConvergedResult(appInfo ?? appInfos[0], localizations);

        var localization = localizations[appInfo.Id];

        var updatedFields = localization is null
            ? AppStoreConnectClient.GetSuppliedAppInfoLocalizationFields(spec.Metadata)
            : GetChangedFields(localization, spec.Metadata);
        var updated = localization is null
            ? await _client.CreateAppInfoLocalizationAsync(appInfo.Id, spec.Locale, spec.Metadata, cancellationToken).ConfigureAwait(false)
            : updatedFields.Length == 0
                ? localization
                : await _client.UpdateAppInfoLocalizationAsync(localization.Id, spec.Metadata, cancellationToken).ConfigureAwait(false);

        return new AppStoreConnectAppInfoMetadataSyncResult
        {
            AppInfo = appInfo,
            Before = localization,
            CreatedLocalization = localization is null,
            After = updated,
            UpdatedFields = updatedFields
        };
    }

    private static AppStoreConnectAppInformationInfo? ResolveAppInfo(
        AppStoreConnectAppInfoMetadataSpec spec,
        AppStoreConnectAppInformationInfo[] appInfos)
    {
        if (!string.IsNullOrWhiteSpace(spec.AppInfoId))
        {
            var requestedAppInfoId = spec.AppInfoId!.Trim();
            return appInfos.FirstOrDefault(info => string.Equals(info.Id, requestedAppInfoId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"App Information resource '{requestedAppInfoId}' does not belong to app '{spec.AppId}'.");
        }

        var editable = appInfos.FirstOrDefault(IsEditable);
        if (editable is not null)
            return editable;
        if (appInfos.Length == 1 && string.IsNullOrWhiteSpace(GetState(appInfos[0])))
            return appInfos[0];

        return null;
    }

    private async Task<Dictionary<string, AppStoreConnectAppInfoLocalizationInfo?>> LoadAppInfoLocalizationsAsync(
        AppStoreConnectAppInfoMetadataSpec spec,
        AppStoreConnectAppInformationInfo[] appInfos,
        CancellationToken cancellationToken)
    {
        var localizations = new Dictionary<string, AppStoreConnectAppInfoLocalizationInfo?>(StringComparer.OrdinalIgnoreCase);
        foreach (var appInfo in appInfos)
        {
            var localization = (await _client.GetAppInfoLocalizationsAsync(
                    appInfo.Id,
                    spec.Locale,
                    limit: 10,
                    cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();
            localizations.Add(appInfo.Id, localization);
        }
        return localizations;
    }

    private static void AssertLockedAppInfoConverged(
        AppStoreConnectAppInfoMetadataSpec spec,
        AppStoreConnectAppInformationInfo[] appInfos,
        IReadOnlyDictionary<string, AppStoreConnectAppInfoLocalizationInfo?> localizations,
        AppStoreConnectAppInformationInfo? selected)
    {
        if (appInfos.Length == 0)
            throw CreateNoEditableAppInfoException(spec.AppId, appInfos);

        var mismatched = appInfos
            .Where(appInfo => !IsEditable(appInfo))
            // A previous live version is not the target of an editable version's new metadata.
            // Locked in-flight reviews still have to converge before shared changes proceed.
            .Where(appInfo => selected is null || !IsEditable(selected) || !IsReleased(appInfo))
            .Where(appInfo => localizations[appInfo.Id] is not { } current || GetChangedFields(current, spec.Metadata).Length > 0)
            .ToArray();
        if (mismatched.Length > 0)
            throw CreateNoEditableAppInfoException(spec.AppId, appInfos);
    }

    private static AppStoreConnectAppInfoMetadataSyncResult CreateConvergedResult(
        AppStoreConnectAppInformationInfo retained,
        IReadOnlyDictionary<string, AppStoreConnectAppInfoLocalizationInfo?> localizations)
    {
        var localization = localizations[retained.Id]
            ?? throw new InvalidOperationException("The retained App Information localization is missing.");
        return new AppStoreConnectAppInfoMetadataSyncResult
        {
            AppInfo = retained,
            Before = localization,
            After = localization,
            UpdatedFields = Array.Empty<string>()
        };
    }

    private static InvalidOperationException CreateNoEditableAppInfoException(
        string appId,
        AppStoreConnectAppInformationInfo[] appInfos)
    {

        var states = appInfos.Length == 0
            ? "none"
            : string.Join(", ", appInfos.Select(info => GetState(info) ?? "unknown"));
        return new InvalidOperationException(
            $"App Information metadata cannot converge for app '{appId}' because one or more locked resources do not already match the requested metadata. Current states: {states}. Create an editable App Store version, or wait for in-review resources to settle, before applying shared App Information changes.");
    }

    private static string[] GetChangedFields(
        AppStoreConnectAppInfoLocalizationInfo current,
        AppStoreConnectAppInfoLocalizationUpdate desired)
    {
        var changed = new List<string>();
        AddChanged(changed, "name", desired.Name, current.Name);
        AddChanged(changed, "subtitle", desired.Subtitle, current.Subtitle);
        AddChanged(changed, "privacyPolicyUrl", desired.PrivacyPolicyUrl, current.PrivacyPolicyUrl);
        AddChanged(changed, "privacyChoicesUrl", desired.PrivacyChoicesUrl, current.PrivacyChoicesUrl);
        AddChanged(changed, "privacyPolicyText", desired.PrivacyPolicyText, current.PrivacyPolicyText);
        return changed.ToArray();
    }

    private static void AddChanged(List<string> changed, string field, string? desired, string? current)
    {
        if (desired is not null && !string.Equals(desired, current, StringComparison.Ordinal))
            changed.Add(field);
    }

    private static bool IsEditable(AppStoreConnectAppInformationInfo appInfo)
    {
        var state = GetState(appInfo);
        return state is not null && EditableStates.Contains(state);
    }

    private static bool IsReleased(AppStoreConnectAppInformationInfo appInfo)
        => GetState(appInfo) is { } state &&
           (string.Equals(state, "READY_FOR_SALE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "READY_FOR_DISTRIBUTION", StringComparison.OrdinalIgnoreCase));

    private static string? GetState(AppStoreConnectAppInformationInfo appInfo)
        => string.IsNullOrWhiteSpace(appInfo.State) ? appInfo.AppStoreState : appInfo.State;
}
