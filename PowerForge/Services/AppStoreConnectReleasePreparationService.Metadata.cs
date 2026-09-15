namespace PowerForge;

public sealed partial class AppStoreConnectReleasePreparationService
{
    private static AppStoreConnectVersionMetadataSpec[] ResolveMetadataSpecs(AppStoreConnectReleasePreparationRequest request)
    {
        var specs = new List<AppStoreConnectVersionMetadataSpec>();
        if (request.MetadataSpec is not null)
            specs.Add(request.MetadataSpec);
        specs.AddRange(request.MetadataSpecs ?? Array.Empty<AppStoreConnectVersionMetadataSpec>());
        var locales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            if (spec is null || string.IsNullOrWhiteSpace(spec.Locale) || spec.Metadata is null)
                throw new ArgumentException("Every metadata spec must declare a locale and metadata object.", nameof(request));
            if (!locales.Add(spec.Locale.Trim()))
                throw new ArgumentException($"Multiple metadata specs declare locale '{spec.Locale.Trim()}'.", nameof(request));
            if (AppStoreConnectClient.GetSuppliedLocalizationFields(spec.Metadata).Length == 0)
                throw new ArgumentException("Every metadata spec must supply at least one metadata field.", nameof(request));
            // Validate every target before any version creation or build selection can mutate Apple.
            CreateMetadataSpecForVersion(spec, request.AppId.Trim(), request.VersionString?.Trim() ?? string.Empty, request.Platform, string.Empty);
        }
        return specs.ToArray();
    }

    private static AppStoreConnectVersionMetadataSpec CreateMetadataSpecForVersion(
        AppStoreConnectVersionMetadataSpec source,
        string appId,
        string versionString,
        ApplePlatform platform,
        string versionId)
    {
        if (!string.IsNullOrWhiteSpace(source.AppId) &&
            !string.Equals(source.AppId.Trim(), appId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Metadata config AppId '{source.AppId}' does not match release app id '{appId}'.");
        var sourceVersionString = string.IsNullOrWhiteSpace(source.VersionString) ? null : source.VersionString!.Trim();
        if (sourceVersionString is not null &&
            !string.Equals(sourceVersionString, versionString, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Metadata config VersionString '{source.VersionString}' does not match release version '{versionString}'.");
        if (source.Platform != platform)
            throw new InvalidOperationException($"Metadata config Platform '{source.Platform}' does not match release platform '{platform}'.");

        return new AppStoreConnectVersionMetadataSpec
        {
            AppId = appId,
            VersionString = versionString,
            VersionId = versionId,
            UseReleaseVersion = false,
            Platform = platform,
            Locale = source.Locale.Trim(),
            Metadata = source.Metadata
        };
    }

}
