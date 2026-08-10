using System.Globalization;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static readonly string[] ObserveCollectValueOptions =
    [
        "--config", "--database", "--site", "--provider", "--from", "--to", "--search-type", "--evidence", "--output"
    ];

    private static int HandleObserveCollect(
        string[] args,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        var missingValueOption = FindSearchOptionWithoutValue(args, ObserveCollectValueOptions);
        if (missingValueOption is not null)
            return FailSearch($"{missingValueOption} requires a value.", outputJson, logger, "web.observe.collect");
        var duplicateOption = FindDuplicateObserveCollectOption(args);
        if (duplicateOption is not null)
            return FailSearch($"Observe collect accepts {duplicateOption} only once.", outputJson, logger, "web.observe.collect");
        var unexpectedArgument = FindUnexpectedObserveCollectArgument(args);
        if (unexpectedArgument is not null)
            return FailSearch($"Observe collect does not recognize argument '{unexpectedArgument}'.", outputJson, logger, "web.observe.collect");

        var configPath = TryGetOptionValue(args, "--config");
        var databasePath = TryGetOptionValue(args, "--database");
        var siteId = TryGetOptionValue(args, "--site");
        var providerId = TryGetOptionValue(args, "--provider");
        var fromValue = TryGetOptionValue(args, "--from");
        var throughValue = TryGetOptionValue(args, "--to");
        if (string.IsNullOrWhiteSpace(configPath))
            return FailSearch("Observe collect requires --config.", outputJson, logger, "web.observe.collect");
        if (string.IsNullOrWhiteSpace(databasePath))
            return FailSearch("Observe collect requires --database.", outputJson, logger, "web.observe.collect");
        if (string.IsNullOrWhiteSpace(siteId))
            return FailSearch("Observe collect requires --site.", outputJson, logger, "web.observe.collect");
        if (string.IsNullOrWhiteSpace(providerId))
            return FailSearch("Observe collect requires --provider.", outputJson, logger, "web.observe.collect");
        if (string.IsNullOrWhiteSpace(fromValue))
            return FailSearch("Observe collect requires --from.", outputJson, logger, "web.observe.collect");
        if (string.IsNullOrWhiteSpace(throughValue))
            return FailSearch("Observe collect requires --to.", outputJson, logger, "web.observe.collect");
        var outputFormat = TryGetOptionValue(args, "--output");
        if (!string.IsNullOrWhiteSpace(outputFormat) && !outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
            return FailSearch("Observe collect supports only '--output json'.", outputJson, logger, "web.observe.collect");

        try
        {
            var fromDate = ParseRequiredCollectionDate(fromValue, "--from");
            var throughDate = ParseRequiredCollectionDate(throughValue, "--to");
            if (fromDate > throughDate)
                return FailSearch("Observe collect requires --from to be on or before --to.", outputJson, logger, "web.observe.collect");

            var searchType = TryGetOptionValue(args, "--search-type") ?? "web";
            var loaded = WebSearchProviderConfigurationLoader.LoadWithPath(configPath, WebCliJson.Options);
            var doctor = WebSearchProviderDoctor.InspectWithCapabilities(
                loaded.Configuration,
                WebSearchCollectorCatalog.AvailableCapabilities);
            if (!doctor.Success || string.IsNullOrWhiteSpace(doctor.ConfigurationHash))
            {
                var firstError = doctor.Checks.FirstOrDefault(check => check.Severity == WebSearchProviderCheckSeverity.Error);
                return FailSearch(
                    firstError?.Message ?? "Search provider configuration has blocking capability errors.",
                    outputJson,
                    logger,
                    "web.observe.collect");
            }

            var site = loaded.Configuration.Sites.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, siteId, StringComparison.OrdinalIgnoreCase));
            if (site is null)
                return FailSearch($"Search site '{siteId}' is not configured.", outputJson, logger, "web.observe.collect");
            var provider = site.Providers.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, providerId, StringComparison.OrdinalIgnoreCase));
            if (provider is null)
                return FailSearch($"Search provider '{providerId}' is not configured for site '{siteId}'.", outputJson, logger, "web.observe.collect");
            if (!provider.Enabled)
                return FailSearch($"Search provider '{providerId}' is disabled.", outputJson, logger, "web.observe.collect");
            if (!provider.Kind.Equals(GoogleSearchConsoleCollector.ProviderKind, StringComparison.Ordinal))
                return FailSearch("Observe collect currently supports only Google Search Console providers.", outputJson, logger, "web.observe.collect");
            if (!provider.Capabilities.Contains(WebSearchProviderCapabilities.SearchAnalytics, StringComparer.Ordinal))
                return FailSearch("Google Search Console provider must request search.analytics.", outputJson, logger, "web.observe.collect");
            if (provider.Credential is null)
                return FailSearch("Google Search Console provider requires a credential reference.", outputJson, logger, "web.observe.collect");
            if (!provider.Settings.TryGetValue("property", out var property) || string.IsNullOrWhiteSpace(property))
                return FailSearch("Google Search Console provider requires the property setting.", outputJson, logger, "web.observe.collect");

            var tokenProvider = GoogleSearchConsoleServiceAccountAccessTokenProvider.Create(provider.Credential);
            using var httpClient = new HttpClient();
            var collector = new GoogleSearchConsoleCollector(httpClient, tokenProvider);
            var collection = collector.CollectAsync(new GoogleSearchConsoleCollectionOptions
            {
                ProviderId = provider.Id,
                SiteId = site.Id,
                Property = property,
                FromDate = fromDate,
                ThroughDate = throughDate,
                SearchType = searchType,
                ConfigurationHash = doctor.ConfigurationHash,
                EvidenceReference = TryGetOptionValue(args, "--evidence")
            }).GetAwaiter().GetResult();

            var normalized = WebSearchObservationNormalizer.Normalize(collection.Batch);
            var fullDatabasePath = Path.GetFullPath(databasePath.Trim().Trim('"'));
            var store = new SqliteWebSearchObservationStore(fullDatabasePath);
            var import = store.ImportAsync(normalized).GetAwaiter().GetResult();
            var commandResult = new WebSearchCollectionCommandResult
            {
                Provider = normalized.Provider,
                SiteId = normalized.SiteId,
                CollectedAtUtc = normalized.CollectedAtUtc,
                Status = normalized.Status,
                ZeroDataConfirmed = normalized.ZeroDataConfirmed,
                CollectionCoverage = normalized.CollectionCoverage,
                Probe = collection.Probe,
                CompletedDateCount = collection.CompletedDateCount,
                RequestCount = collection.RequestCount,
                ErrorCode = collection.ErrorCode,
                ErrorMessage = collection.ErrorMessage,
                Import = import
            };
            var exitCode = collection.Success ? 0 : 1;

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.observe.collect",
                    Success = collection.Success,
                    ExitCode = exitCode,
                    ConfigPath = loaded.FullPath,
                    Result = WebCliJson.SerializeToElement(commandResult, WebCliJson.Context.WebSearchCollectionCommandResult)
                });
            }
            else if (collection.Success)
            {
                logger.Success(
                    $"Collected {import.InputCount} Google Search Console observations for {EscapeSearchConsoleText(site.Id, "(unknown site)")}; {import.InsertedCount} inserted and {import.DuplicateCount} duplicates ignored.");
            }
            else
            {
                logger.Error(EscapeSearchConsoleText(
                    $"Google Search Console collection was partial: {collection.ErrorMessage}",
                    "Google Search Console collection was partial."));
                logger.Info($"Preserved {import.InputCount} partial observations in search history.");
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.observe.collect");
        }
    }

    private static DateOnly ParseRequiredCollectionDate(string value, string optionName)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException($"{optionName} must use yyyy-MM-dd.", optionName);
        return date;
    }

    private static string? FindDuplicateObserveCollectOption(string[] args)
    {
        foreach (var optionName in ObserveCollectValueOptions)
        {
            if (args.Count(argument => argument.Equals(optionName, StringComparison.OrdinalIgnoreCase)) > 1)
                return optionName;
        }

        return null;
    }

    private static string? FindUnexpectedObserveCollectArgument(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--json", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--output-json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ObserveCollectValueOptions.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            return argument;
        }

        return null;
    }
}

internal sealed class WebSearchCollectionCommandResult
{
    public string Provider { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public DateTimeOffset CollectedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool ZeroDataConfirmed { get; set; }
    public WebSearchObservationCollectionCoverage? CollectionCoverage { get; set; }
    public GoogleSearchConsolePropertyProbeResult Probe { get; set; } = new();
    public int CompletedDateCount { get; set; }
    public int RequestCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public WebSearchObservationImportResult Import { get; set; } = new();
}
