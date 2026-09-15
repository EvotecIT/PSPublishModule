namespace PowerForge;

/// <summary>
/// Syncs localized App Store version metadata.
/// </summary>
public sealed class AppStoreConnectVersionMetadataSyncService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>
    /// Initializes a metadata sync service.
    /// </summary>
    public AppStoreConnectVersionMetadataSyncService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Applies localized metadata to the matching App Store version localization.
    /// </summary>
    public async Task<AppStoreConnectVersionMetadataSyncResult> SyncAsync(
        AppStoreConnectVersionMetadataSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        var spec = request.Spec ?? throw new ArgumentException("Spec is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(spec.AppId))
            throw new ArgumentException("Spec.AppId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(spec.VersionString) && string.IsNullOrWhiteSpace(spec.VersionId))
            throw new ArgumentException(
                spec.UseReleaseVersion
                    ? "Spec.UseReleaseVersion must be resolved by the unified Apple release workflow before metadata sync."
                    : "Spec.VersionString or Spec.VersionId is required.",
                nameof(request));
        if (string.IsNullOrWhiteSpace(spec.Locale))
            throw new ArgumentException("Spec.Locale is required.", nameof(request));

        if (spec.Metadata is null || AppStoreConnectClient.GetSuppliedLocalizationFields(spec.Metadata).Length == 0)
            throw new ArgumentException("Spec.Metadata must supply at least one metadata field.", nameof(request));

        var version = await ResolveVersionAsync(spec, cancellationToken).ConfigureAwait(false);
        var localization = (await _client.GetVersionLocalizationsAsync(
            version.Id,
            spec.Locale,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault();

        var updated = localization is null
            ? await _client.CreateVersionLocalizationAsync(version.Id, spec.Locale, spec.Metadata, cancellationToken).ConfigureAwait(false)
            : await _client.UpdateVersionLocalizationAsync(localization.Id, spec.Metadata, cancellationToken).ConfigureAwait(false);

        return new AppStoreConnectVersionMetadataSyncResult
        {
            Version = version,
            Before = localization,
            CreatedLocalization = localization is null,
            After = updated,
            UpdatedFields = AppStoreConnectClient.GetSuppliedLocalizationFields(spec.Metadata)
        };
    }

    private async Task<AppStoreConnectVersionInfo> ResolveVersionAsync(
        AppStoreConnectVersionMetadataSpec spec,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(spec.VersionId))
        {
            return new AppStoreConnectVersionInfo
            {
                Id = spec.VersionId!.Trim(),
                VersionString = spec.VersionString,
                Platform = spec.Platform.ToString()
            };
        }

        return (await _client.GetVersionsAsync(
            spec.AppId,
            spec.VersionString,
            spec.Platform,
            limit: 10,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException($"App Store version '{spec.VersionString}' was not found for app '{spec.AppId}' and platform '{spec.Platform}'.");
    }
}
