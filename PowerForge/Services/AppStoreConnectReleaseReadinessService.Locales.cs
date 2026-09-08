namespace PowerForge;

public sealed partial class AppStoreConnectReleaseReadinessService
{
    internal static string[] NormalizeMetadataLocales(IEnumerable<string>? locales)
    {
        var values = (locales ?? Array.Empty<string>()).ToArray();
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Metadata locales must not contain empty values.", nameof(locales));
        return values.Select(static locale => locale.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<AppStoreConnectReleaseReadinessResult> CheckLocalesAsync(
        AppStoreConnectReleaseReadinessRequest request, CancellationToken cancellationToken)
    {
        var metadataLocales = NormalizeMetadataLocales(request.MetadataLocales);
        var specs = (request.ScreenshotSpec is null
            ? Array.Empty<AppStoreConnectScreenshotSyncSpec>()
            : new[] { request.ScreenshotSpec }).Concat(request.ScreenshotSpecs ?? Array.Empty<AppStoreConnectScreenshotSyncSpec>()).ToArray();
        AppStoreConnectReleasePreparationService.ValidateScreenshotLocales(specs);
        var locales = specs.Select(static spec => spec.Locale.Trim()).Concat(metadataLocales).ToList();
        // A direct caller can still require screenshot types for its legacy Locale without a mapping.
        if (specs.Length == 0 && (request.RequireScreenshots || locales.Count == 0))
            locales.Insert(0, request.Locale.Trim());
        var selected = locales.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = new List<AppStoreConnectReleaseReadinessResult>();
        foreach (var locale in selected)
        {
            var spec = specs.FirstOrDefault(value => string.Equals(value.Locale.Trim(), locale, StringComparison.OrdinalIgnoreCase));
            var requireScreenshots = request.RequireScreenshots && (spec is not null ||
                (specs.Length == 0 && string.Equals(locale, request.Locale.Trim(), StringComparison.OrdinalIgnoreCase)));
            results.Add(await CheckAsync(request.ForLocale(locale, spec, requireScreenshots), cancellationToken).ConfigureAwait(false));
        }

        var aggregate = results[0];
        aggregate.Localizations = results.Where(static result => result.Localization is not null)
            .Select(static result => result.Localization!).ToArray();
        aggregate.IsReady = results.All(static result => result.IsReady);
        aggregate.ScreenshotSets = results.SelectMany(static result => result.ScreenshotSets).ToArray();
        aggregate.Checks = results.SelectMany((result, index) => result.Checks.Select(check => new AppStoreConnectReleaseReadinessCheck
        {
            Name = selected.Length == 1 && (request.ScreenshotSpecs?.Length ?? 0) == 0
                ? check.Name : $"{selected[index]}.{check.Name}",
            Passed = check.Passed,
            Message = check.Message
        })).ToArray();
        return aggregate;
    }
}
