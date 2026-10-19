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
            if (spec is null)
                throw new ArgumentException("Every metadata spec must be non-null.", nameof(request));
            var fields = spec.Metadata is null ? Array.Empty<string>() : AppStoreConnectClient.GetSuppliedLocalizationFields(spec.Metadata);
            if (fields.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(spec.Locale))
                    throw new ArgumentException("Localized metadata must declare a locale.", nameof(request));
                if (!locales.Add(spec.Locale.Trim()))
                    throw new ArgumentException($"Multiple metadata specs declare locale '{spec.Locale.Trim()}'.", nameof(request));
            }
            else if (spec.Copyright is null)
                throw new ArgumentException("Every metadata spec must supply copyright or a localized metadata field.", nameof(request));
            // Validate every target before any version creation or build selection can mutate Apple.
            CreateMetadataSpecForVersion(spec, request.AppId.Trim(), request.VersionString?.Trim() ?? string.Empty, request.Platform, string.Empty);
        }
        if (specs.Where(static spec => spec.Copyright is not null)
            .Select(static spec => spec.Copyright!.Trim()).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new ArgumentException("Metadata locales must not declare conflicting version-wide copyright notices.", nameof(request));
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
        if (source.Copyright is not null && string.IsNullOrWhiteSpace(source.Copyright))
            throw new ArgumentException("Metadata copyright must not be empty when supplied.", nameof(source));

        return new AppStoreConnectVersionMetadataSpec
        {
            AppId = appId,
            VersionString = versionString,
            VersionId = versionId,
            UseReleaseVersion = false,
            Platform = platform,
            Locale = source.Locale?.Trim() ?? string.Empty,
            Copyright = source.Copyright,
            Metadata = source.Metadata
        };
    }

}
