using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static readonly string[] ObserveImportBingValueOptions =
    [
        "--config", "--input", "--database", "--site", "--provider", "--from", "--to", "--collected-at", "--search-type", "--evidence", "--output"
    ];

    private static int HandleBingObserveCollect(
        WebSearchSiteProviderConfiguration site,
        WebSearchProviderRegistration provider,
        string configurationHash,
        string configPath,
        string databasePath,
        DateOnly fromDate,
        DateOnly throughDate,
        string searchType,
        string? evidenceReference,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        if (!provider.Capabilities.Contains(WebSearchProviderCapabilities.SearchAnalytics, StringComparer.Ordinal))
            return FailSearch("Bing Webmaster provider must request search.analytics.", outputJson, logger, "web.observe.collect");
        if (provider.Credential is null)
            return FailSearch("Bing Webmaster provider requires a credential reference.", outputJson, logger, "web.observe.collect");
        if (!provider.Settings.TryGetValue("siteUrl", out var siteUrl) || string.IsNullOrWhiteSpace(siteUrl))
            return FailSearch("Bing Webmaster provider requires the siteUrl setting.", outputJson, logger, "web.observe.collect");

        try
        {
            var apiKeyProvider = BingWebmasterEnvironmentApiKeyProvider.Create(provider.Credential);
            using var httpClient = new HttpClient();
            var collector = new BingWebmasterCollector(httpClient, apiKeyProvider);
            var collection = collector.CollectAsync(new BingWebmasterCollectionOptions
            {
                ProviderId = provider.Id,
                SiteId = site.Id,
                SiteUrl = siteUrl,
                FromDate = fromDate,
                ThroughDate = throughDate,
                SearchType = searchType,
                ConfigurationHash = configurationHash,
                EvidenceReference = evidenceReference
            }).GetAwaiter().GetResult();

            var normalized = WebSearchObservationNormalizer.Normalize(collection.Batch);
            var fullDatabasePath = Path.GetFullPath(databasePath.Trim().Trim('"'));
            var store = new SqliteWebSearchObservationStore(fullDatabasePath);
            var import = store.ImportAsync(normalized).GetAwaiter().GetResult();
            var commandResult = new BingWebmasterCollectionCommandResult
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
                    ConfigPath = configPath,
                    Result = WebCliJson.SerializeToElement(commandResult, WebCliJson.Context.BingWebmasterCollectionCommandResult)
                });
            }
            else if (collection.Success)
            {
                logger.Success(
                    $"Collected {import.InputCount} Bing Webmaster observations for {EscapeSearchConsoleText(site.Id, "(unknown site)")}; {import.InsertedCount} inserted and {import.DuplicateCount} duplicates ignored.");
            }
            else
            {
                logger.Error(EscapeSearchConsoleText(
                    $"Bing Webmaster collection was partial: {collection.ErrorMessage}",
                    "Bing Webmaster collection was partial."));
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.observe.collect");
        }
    }

    private static int HandleObserveImportBing(
        string[] args,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        var missingValueOption = FindSearchOptionWithoutValue(args, ObserveImportBingValueOptions);
        if (missingValueOption is not null)
            return FailSearch($"{missingValueOption} requires a value.", outputJson, logger, "web.observe.import-bing");
        var duplicateOption = ObserveImportBingValueOptions.FirstOrDefault(option =>
            args.Count(argument => argument.Equals(option, StringComparison.OrdinalIgnoreCase)) > 1);
        if (duplicateOption is not null)
            return FailSearch($"Observe import-bing accepts {duplicateOption} only once.", outputJson, logger, "web.observe.import-bing");
        var unexpectedArgument = FindUnexpectedArgument(args, ObserveImportBingValueOptions);
        if (unexpectedArgument is not null)
            return FailSearch($"Observe import-bing does not recognize argument '{unexpectedArgument}'.", outputJson, logger, "web.observe.import-bing");

        var configPath = TryGetOptionValue(args, "--config");
        var inputPath = TryGetOptionValue(args, "--input");
        var databasePath = TryGetOptionValue(args, "--database");
        var siteId = TryGetOptionValue(args, "--site");
        var providerId = TryGetOptionValue(args, "--provider");
        var fromValue = TryGetOptionValue(args, "--from");
        var throughValue = TryGetOptionValue(args, "--to");
        var collectedAtValue = TryGetOptionValue(args, "--collected-at");
        if (string.IsNullOrWhiteSpace(configPath) || string.IsNullOrWhiteSpace(inputPath) ||
            string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(siteId) ||
            string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(fromValue) ||
            string.IsNullOrWhiteSpace(throughValue) || string.IsNullOrWhiteSpace(collectedAtValue))
        {
            return FailSearch(
                "Observe import-bing requires --config, --input, --database, --site, --provider, --from, --to and --collected-at.",
                outputJson,
                logger,
                "web.observe.import-bing");
        }
        var outputFormat = TryGetOptionValue(args, "--output");
        if (!string.IsNullOrWhiteSpace(outputFormat) && !outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
            return FailSearch("Observe import-bing supports only '--output json'.", outputJson, logger, "web.observe.import-bing");

        try
        {
            var fromDate = ParseRequiredCollectionDate(fromValue, "--from");
            var throughDate = ParseRequiredCollectionDate(throughValue, "--to");
            if (fromDate > throughDate)
                return FailSearch("Observe import-bing requires --from to be on or before --to.", outputJson, logger, "web.observe.import-bing");
            var collectedAtUtc = ParseRequiredOffsetTimestamp(collectedAtValue, "--collected-at");

            var loaded = WebSearchProviderConfigurationLoader.LoadWithPath(configPath, WebCliJson.Options);
            var configuredSite = loaded.Configuration.Sites.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, siteId, StringComparison.OrdinalIgnoreCase));
            var configuredProvider = configuredSite?.Providers.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, providerId, StringComparison.OrdinalIgnoreCase));
            if (configuredSite is null || configuredProvider is null)
                return FailSearch("Observe import-bing site or provider is not configured.", outputJson, logger, "web.observe.import-bing");
            var unusedCredentialVariable = configuredProvider.Credential?.EnvironmentVariable;
            var doctor = WebSearchProviderDoctor.InspectWithCapabilities(
                loaded.Configuration,
                WebSearchCollectorCatalog.AvailableCapabilities,
                name => string.Equals(name, unusedCredentialVariable, StringComparison.Ordinal)
                    ? "credential-not-used-by-csv-import"
                    : Environment.GetEnvironmentVariable(name));
            if (!doctor.Success || string.IsNullOrWhiteSpace(doctor.ConfigurationHash))
            {
                var firstError = doctor.Checks.FirstOrDefault(check => check.Severity == WebSearchProviderCheckSeverity.Error);
                return FailSearch(firstError?.Message ?? "Search provider configuration has blocking capability errors.", outputJson, logger, "web.observe.import-bing");
            }

            var site = configuredSite;
            var provider = configuredProvider;
            if (provider.Kind is not (BingWebmasterCollector.ProviderKind or BingWebmasterCsvExportParser.ProviderKind))
                return FailSearch("Observe import-bing requires a Bing Webmaster provider.", outputJson, logger, "web.observe.import-bing");
            if (!provider.Capabilities.Contains(WebSearchProviderCapabilities.SearchAnalytics, StringComparer.Ordinal))
                return FailSearch("Bing Webmaster provider must request search.analytics.", outputJson, logger, "web.observe.import-bing");

            var fullInputPath = ResolveExistingFilePath(inputPath);
            var batch = BingWebmasterCsvExportParser.Parse(
                File.ReadAllText(fullInputPath),
                new BingWebmasterCsvExportOptions
                {
                    ProviderId = provider.Id,
                    SiteId = site.Id,
                    FromDate = fromDate,
                    ThroughDate = throughDate,
                    SearchType = TryGetOptionValue(args, "--search-type") ?? "web",
                    CollectedAtUtc = collectedAtUtc,
                    ConfigurationHash = doctor.ConfigurationHash,
                    EvidenceReference = TryGetOptionValue(args, "--evidence")
                });
            var fullDatabasePath = Path.GetFullPath(databasePath.Trim().Trim('"'));
            var store = new SqliteWebSearchObservationStore(fullDatabasePath);
            var result = store.ImportAsync(batch).GetAwaiter().GetResult();

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.observe.import-bing",
                    Success = true,
                    ExitCode = 0,
                    ConfigPath = loaded.FullPath,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebSearchObservationImportResult)
                });
            }
            else
            {
                logger.Success(
                    $"Imported {result.InsertedCount} Bing Webmaster observations for {EscapeSearchConsoleText(result.SiteId, "(unknown site)")}; {result.DuplicateCount} duplicates ignored.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.observe.import-bing");
        }
    }

    private static string? FindUnexpectedArgument(string[] args, IReadOnlyCollection<string> valueOptions)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--json", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--output-json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (valueOptions.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }
            return argument;
        }
        return null;
    }

    private static DateTimeOffset ParseRequiredOffsetTimestamp(string value, string optionName)
    {
        var trimmed = value.Trim();
        var hasOffset = trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                        (trimmed.Length >= 6 &&
                         (trimmed[^6] == '+' || trimmed[^6] == '-') &&
                         trimmed[^3] == ':');
        if (!hasOffset ||
            !DateTimeOffset.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new ArgumentException($"{optionName} must be an ISO 8601 timestamp with Z or an explicit numeric offset.", optionName);
        }
        return parsed.ToUniversalTime();
    }
}

internal sealed class BingWebmasterCollectionCommandResult
{
    public string Provider { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public DateTimeOffset CollectedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool ZeroDataConfirmed { get; set; }
    public WebSearchObservationCollectionCoverage? CollectionCoverage { get; set; }
    public BingWebmasterSiteProbeResult Probe { get; set; } = new();
    public int CompletedDateCount { get; set; }
    public int RequestCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public WebSearchObservationImportResult Import { get; set; } = new();
}
