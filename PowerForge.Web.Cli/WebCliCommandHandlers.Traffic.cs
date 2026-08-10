using System.Globalization;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static readonly string[] TrafficCollectValueOptions =
    [
        "--config", "--database", "--site", "--provider", "--from", "--to", "--evidence", "--output"
    ];

    internal static int HandleTraffic(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        if (subArgs.Length == 0)
            return FailSearch("Traffic requires the 'collect' or 'list' action.", outputJson, logger, "web.traffic");
        return subArgs[0].ToLowerInvariant() switch
        {
            "collect" => HandleTrafficCollect(subArgs.Skip(1).ToArray(), outputJson, logger, outputSchemaVersion),
            "list" => HandleTrafficList(subArgs.Skip(1).ToArray(), outputJson, logger, outputSchemaVersion),
            _ => FailSearch("Traffic requires the 'collect' or 'list' action.", outputJson, logger, "web.traffic")
        };
    }

    private static int HandleTrafficCollect(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var optionError = ValidateOptions(args, TrafficCollectValueOptions);
        if (optionError is not null)
            return FailSearch(optionError, outputJson, logger, "web.traffic.collect");
        var configPath = TryGetOptionValue(args, "--config");
        var databasePath = TryGetOptionValue(args, "--database");
        var siteId = TryGetOptionValue(args, "--site");
        var providerId = TryGetOptionValue(args, "--provider");
        var fromValue = TryGetOptionValue(args, "--from");
        var throughValue = TryGetOptionValue(args, "--to");
        if (new[] { configPath, databasePath, siteId, providerId, fromValue, throughValue }.Any(string.IsNullOrWhiteSpace))
            return FailSearch("Traffic collect requires --config, --database, --site, --provider, --from and --to.", outputJson, logger, "web.traffic.collect");

        try
        {
            var fromDate = ParseTrafficDate(fromValue!, "--from");
            var throughDate = ParseTrafficDate(throughValue!, "--to");
            if (fromDate > throughDate)
                throw new ArgumentException("Traffic collect requires --from to be on or before --to.");
            var loaded = WebSearchProviderConfigurationLoader.LoadWithPath(configPath!, WebCliJson.Options);
            var site = loaded.Configuration.Sites.SingleOrDefault(value => string.Equals(value.Id, siteId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Traffic site '{siteId}' is not configured.");
            var provider = site.Providers.SingleOrDefault(value => string.Equals(value.Id, providerId, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Traffic provider '{providerId}' is not configured for site '{siteId}'.");
            if (!provider.Enabled)
                throw new ArgumentException($"Traffic provider '{providerId}' is disabled.");
            var doctor = InspectProviderAction(
                loaded.Configuration,
                site,
                provider,
                WebSearchProviderCapabilities.TrafficAnalytics,
                useSelectedCredential: true);
            if (!doctor.Success || string.IsNullOrWhiteSpace(doctor.ConfigurationHash))
            {
                var first = doctor.Checks.FirstOrDefault(value => value.Severity == WebSearchProviderCheckSeverity.Error);
                return FailSearch(first?.Message ?? "Provider configuration has blocking capability errors.", outputJson, logger, "web.traffic.collect");
            }
            if (!provider.Kind.Equals(CloudflareAnalyticsCollector.ProviderKind, StringComparison.Ordinal))
                throw new ArgumentException("Traffic collect currently supports the cloudflare-analytics provider.");
            if (!provider.Capabilities.Contains(WebSearchProviderCapabilities.TrafficAnalytics, StringComparer.Ordinal))
                throw new ArgumentException("Cloudflare provider must request traffic.analytics.");
            if (provider.Credential is null)
                throw new ArgumentException("Cloudflare analytics requires a credential reference.");
            if (!provider.Settings.TryGetValue("zoneId", out var zoneId) || string.IsNullOrWhiteSpace(zoneId))
                throw new ArgumentException("Cloudflare analytics requires the zoneId setting.");

            var tokenProvider = CloudflareEnvironmentApiTokenProvider.Create(provider.Credential);
            using var httpClient = new HttpClient();
            var collector = new CloudflareAnalyticsCollector(httpClient, tokenProvider);
            var collection = collector.CollectAsync(new CloudflareAnalyticsCollectionOptions
            {
                ProviderId = provider.Id,
                SiteId = site.Id,
                ZoneId = zoneId,
                SiteBaseUrl = site.BaseUrl,
                FromDate = fromDate,
                ThroughDate = throughDate,
                ConfigurationHash = doctor.ConfigurationHash!,
                EvidenceReference = TryGetOptionValue(args, "--evidence")
            }).GetAwaiter().GetResult();
            var normalized = WebTrafficObservationNormalizer.Normalize(collection.Batch);
            var store = new SqliteWebSearchObservationStore(Path.GetFullPath(databasePath!.Trim().Trim('"')));
            var import = store.ImportTrafficAsync(normalized).GetAwaiter().GetResult();
            var result = new CloudflareTrafficCollectionCommandResult
            {
                Provider = normalized.Provider,
                SiteId = normalized.SiteId,
                CollectedAtUtc = normalized.CollectedAtUtc,
                Status = normalized.Status,
                ZeroDataConfirmed = normalized.ZeroDataConfirmed,
                CollectionCoverage = normalized.CollectionCoverage,
                Probe = collection.Probe,
                RequestCount = collection.RequestCount,
                CompletedDateCount = collection.CompletedDateCount,
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
                    Command = "web.traffic.collect",
                    Success = collection.Success,
                    ExitCode = exitCode,
                    ConfigPath = loaded.FullPath,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.CloudflareTrafficCollectionCommandResult)
                });
            }
            else if (collection.Success)
            {
                logger.Success($"Collected {import.InputCount} Cloudflare traffic observations for {EscapeSearchConsoleText(site.Id, "(unknown site)")}; {import.InsertedCount} inserted and {import.DuplicateCount} duplicates ignored.");
            }
            else
            {
                logger.Error(EscapeSearchConsoleText($"Cloudflare traffic collection was partial: {collection.ErrorMessage}", "Cloudflare traffic collection was partial."));
                logger.Info($"Preserved {import.InputCount} partial traffic observations.");
            }
            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.traffic.collect");
        }
    }

    private static int HandleTrafficList(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var allowed = new[] { "--database", "--site", "--provider", "--from", "--to", "--output" };
        var optionError = ValidateOptions(args, allowed);
        if (optionError is not null)
            return FailSearch(optionError, outputJson, logger, "web.traffic.list");
        var databasePath = TryGetOptionValue(args, "--database");
        var siteId = TryGetOptionValue(args, "--site");
        var providerId = TryGetOptionValue(args, "--provider");
        if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(providerId))
            return FailSearch("Traffic list requires --database, --site, and --provider so totals cannot combine providers.", outputJson, logger, "web.traffic.list");
        try
        {
            var store = new SqliteWebSearchObservationStore(Path.GetFullPath(databasePath.Trim().Trim('"')));
            var evidence = store.QueryTrafficEvidenceAsync(new WebTrafficObservationQuery
            {
                SiteId = siteId,
                Provider = providerId,
                FromDate = ParseOptionalTrafficDate(TryGetOptionValue(args, "--from"), "--from"),
                ThroughDate = ParseOptionalTrafficDate(TryGetOptionValue(args, "--to"), "--to")
            }).GetAwaiter().GetResult();
            var observations = evidence.Observations;
            var evidenceState = !evidence.StoreExists
                ? "missing-store"
                : !evidence.HasEvidence
                    ? "no-evidence"
                    : evidence.HasPartialEvidence
                        ? "partial"
                        : evidence.HasCoverageGaps
                            ? "incomplete"
                            : "complete";
            var result = new WebTrafficListCommandResult
            {
                SiteId = siteId.Trim().ToLowerInvariant(),
                Provider = providerId.Trim().ToLowerInvariant(),
                EvidenceState = evidenceState,
                StoreExists = evidence.StoreExists,
                HasEvidence = evidence.HasEvidence,
                HasPartialEvidence = evidence.HasPartialEvidence,
                HasCoverageGaps = evidence.HasCoverageGaps,
                MissingDates = evidence.MissingDates,
                HasExplicitZeroEvidence = evidence.HasExplicitZeroEvidence,
                ObservationCount = observations.Length,
                Requests = observations.Sum(value => value.Requests),
                Visits = observations.Sum(value => value.Visits),
                EdgeResponseBytes = observations.Sum(value => value.EdgeResponseBytes),
                ContainsSampledEstimates = observations.Any(value => value.SampleInterval > 1d),
                SelectedRuns = evidence.SelectedRuns,
                Observations = observations.ToArray()
            };
            var exitCode = evidenceState switch
            {
                "complete" => 0,
                "missing-store" => 2,
                _ => 1
            };
            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.traffic.list",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebTrafficListCommandResult)
                });
            }
            else
            {
                if (!evidence.StoreExists)
                    logger.Error("Traffic database does not exist.");
                else if (!evidence.HasEvidence)
                    logger.Warn("No traffic collection evidence matches the requested filters.");
                else if (evidence.HasPartialEvidence)
                    logger.Warn("Traffic totals include only partial collection evidence; inspect selected runs before comparison.");
                else if (evidence.HasCoverageGaps)
                    logger.Warn($"Traffic evidence is missing for {evidence.MissingDates.Length} requested reporting date(s).");
                else if (evidence.HasExplicitZeroEvidence && observations.Length == 0)
                    logger.Info("Traffic collection explicitly confirmed zero rows for the selected complete evidence.");
                logger.Info($"Traffic observations: {result.ObservationCount}; requests: {result.Requests}; visits: {result.Visits}; bytes: {result.EdgeResponseBytes}.");
            }
            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.traffic.list");
        }
    }

    private static string? ValidateOptions(string[] args, IReadOnlyCollection<string> allowed)
    {
        var missing = FindSearchOptionWithoutValue(args, allowed.ToArray());
        if (missing is not null)
            return $"{missing} requires a value.";
        foreach (var option in allowed)
        {
            if (args.Count(value => value.Equals(option, StringComparison.OrdinalIgnoreCase)) > 1)
                return $"Traffic accepts {option} only once.";
        }
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--json", StringComparison.OrdinalIgnoreCase) ||
                args[index].Equals("--output-json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!allowed.Contains(args[index], StringComparer.OrdinalIgnoreCase))
                return $"Traffic does not recognize argument '{args[index]}'.";
            index++;
        }
        var output = TryGetOptionValue(args, "--output");
        return string.IsNullOrWhiteSpace(output) || output.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Traffic supports only '--output json'.";
    }

    private static DateOnly ParseTrafficDate(string value, string option) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{option} must use yyyy-MM-dd.", option);

    private static DateOnly? ParseOptionalTrafficDate(string? value, string option) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseTrafficDate(value, option);
}

internal sealed class CloudflareTrafficCollectionCommandResult
{
    public string Provider { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public DateTimeOffset CollectedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool ZeroDataConfirmed { get; set; }
    public WebTrafficObservationCollectionCoverage CollectionCoverage { get; set; } = new();
    public CloudflareAnalyticsCapabilityProbeResult Probe { get; set; } = new();
    public int CompletedDateCount { get; set; }
    public int RequestCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public WebTrafficObservationImportResult Import { get; set; } = new();
}

internal sealed class WebTrafficListCommandResult
{
    public string SiteId { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string EvidenceState { get; set; } = string.Empty;
    public bool StoreExists { get; set; }
    public bool HasEvidence { get; set; }
    public bool HasPartialEvidence { get; set; }
    public bool HasCoverageGaps { get; set; }
    public DateOnly[] MissingDates { get; set; } = Array.Empty<DateOnly>();
    public bool HasExplicitZeroEvidence { get; set; }
    public int ObservationCount { get; set; }
    public long Requests { get; set; }
    public long Visits { get; set; }
    public long EdgeResponseBytes { get; set; }
    public bool ContainsSampledEstimates { get; set; }
    public WebTrafficObservationRunEvidence[] SelectedRuns { get; set; } = Array.Empty<WebTrafficObservationRunEvidence>();
    public WebTrafficObservation[] Observations { get; set; } = Array.Empty<WebTrafficObservation>();
}
