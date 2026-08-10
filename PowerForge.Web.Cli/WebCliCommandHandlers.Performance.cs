using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static readonly string[] PerformanceImportOptions =
        ["--config", "--input", "--database", "--site", "--provider", "--evidence", "--output"];
    private static readonly string[] PerformanceCruxOptions =
        ["--config", "--database", "--site", "--provider", "--scope", "--target", "--form-factor", "--evidence", "--output"];
    private static readonly string[] PerformanceListOptions =
        ["--database", "--site", "--provider", "--kind", "--target", "--form-factor", "--output"];

    internal static int HandlePerformance(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (args.Length == 0)
            return FailSearch("Performance requires the 'import-lighthouse', 'collect-crux', or 'list' action.", outputJson, logger, "web.performance");
        return args[0].ToLowerInvariant() switch
        {
            "import-lighthouse" => HandleLighthouseImport(args[1..], outputJson, logger, outputSchemaVersion),
            "collect-crux" => HandleCruxCollect(args[1..], outputJson, logger, outputSchemaVersion),
            "list" => HandlePerformanceList(args[1..], outputJson, logger, outputSchemaVersion),
            _ => FailSearch("Performance requires the 'import-lighthouse', 'collect-crux', or 'list' action.", outputJson, logger, "web.performance")
        };
    }

    private static int HandleLighthouseImport(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var optionError = ValidatePerformanceOptions(args, PerformanceImportOptions);
        if (optionError is not null)
            return FailSearch(optionError, outputJson, logger, "web.performance.import-lighthouse");
        var required = RequirePerformanceValues(args, "--config", "--input", "--database", "--site", "--provider");
        if (required is not null)
            return FailSearch(required, outputJson, logger, "web.performance.import-lighthouse");
        try
        {
            var context = ResolvePerformanceProvider(args, LighthouseReportImporter.ProviderKind, WebSearchProviderCapabilities.PerformanceLighthouse);
            var inputPath = Path.GetFullPath(TryGetOptionValue(args, "--input")!.Trim().Trim('"'));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Lighthouse report was not found.", inputPath);
            var batch = LighthouseReportImporter.Import(File.ReadAllText(inputPath), new LighthouseReportImportOptions
            {
                ProviderId = context.Provider.Id,
                SiteId = context.Site.Id,
                SiteBaseUrl = context.Site.BaseUrl,
                ConfigurationHash = context.ConfigurationHash,
                EvidenceReference = TryGetOptionValue(args, "--evidence")
            });
            var store = new SqliteWebSearchObservationStore(RequiredPerformanceOption(args, "--database"));
            var import = store.ImportPerformanceAsync(batch).GetAwaiter().GetResult();
            return WritePerformanceResult("web.performance.import-lighthouse", true, 0, context.ConfigPath,
                new WebPerformanceCollectionCommandResult { Batch = batch, Import = import, RequestCount = 0 }, outputJson, logger, outputSchemaVersion,
                $"Imported {import.InputCount} Lighthouse laboratory metrics for {EscapeSearchConsoleText(context.Site.Id, "(unknown site)")}.");
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.performance.import-lighthouse");
        }
    }

    private static int HandleCruxCollect(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var optionError = ValidatePerformanceOptions(args, PerformanceCruxOptions);
        if (optionError is not null)
            return FailSearch(optionError, outputJson, logger, "web.performance.collect-crux");
        var required = RequirePerformanceValues(args, "--config", "--database", "--site", "--provider");
        if (required is not null)
            return FailSearch(required, outputJson, logger, "web.performance.collect-crux");
        try
        {
            var context = ResolvePerformanceProvider(args, CruxCollector.ProviderKind, WebSearchProviderCapabilities.PerformanceCrux);
            if (context.Provider.Credential is null)
                throw new ArgumentException("CrUX requires a credential reference.");
            var scope = (TryGetOptionValue(args, "--scope") ?? "origin").Trim().ToLowerInvariant();
            var target = TryGetOptionValue(args, "--target") ?? context.Site.BaseUrl;
            if (scope == "origin")
            {
                var uri = new Uri(target, UriKind.Absolute);
                target = new UriBuilder(uri) { Path = "/", Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
            }
            using var httpClient = new HttpClient();
            var collector = new CruxCollector(httpClient, CruxEnvironmentApiKeyProvider.Create(context.Provider.Credential));
            var result = collector.CollectAsync(new CruxCollectionOptions
            {
                ProviderId = context.Provider.Id,
                SiteId = context.Site.Id,
                SiteBaseUrl = context.Site.BaseUrl,
                TargetKind = scope,
                TargetUrl = target,
                FormFactor = TryGetOptionValue(args, "--form-factor") ?? "all",
                ConfigurationHash = context.ConfigurationHash,
                EvidenceReference = TryGetOptionValue(args, "--evidence")
            }).GetAwaiter().GetResult();
            return CompleteCruxCollection(
                result,
                context.ConfigPath,
                RequiredPerformanceOption(args, "--database"),
                outputJson,
                logger,
                outputSchemaVersion);
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.performance.collect-crux");
        }
    }

    internal static int CompleteCruxCollection(
        CruxCollectionResult result,
        string configPath,
        string databasePath,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success)
        {
            return WritePerformanceResult("web.performance.collect-crux", false, 1, configPath,
                new WebPerformanceCollectionCommandResult
                {
                    Batch = result.Batch,
                    RequestCount = result.RequestCount,
                    ErrorCode = result.ErrorCode,
                    ErrorMessage = result.ErrorMessage
                },
                outputJson, logger, outputSchemaVersion,
                result.ErrorMessage ?? "CrUX collection failed.");
        }

        var store = new SqliteWebSearchObservationStore(databasePath);
        var import = store.ImportPerformanceAsync(result.Batch).GetAwaiter().GetResult();
        return WritePerformanceResult("web.performance.collect-crux", true, 0, configPath,
            new WebPerformanceCollectionCommandResult { Batch = result.Batch, Import = import, RequestCount = result.RequestCount },
            outputJson, logger, outputSchemaVersion,
            result.Batch.ZeroDataConfirmed
                ? $"CrUX explicitly found no field record for {EscapeSearchConsoleText(result.Batch.TargetUrl, "(unknown target)")}."
                : $"Collected {import.InputCount} CrUX field metrics for {EscapeSearchConsoleText(result.Batch.TargetUrl, "(unknown target)")}.");
    }

    private static int HandlePerformanceList(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var optionError = ValidatePerformanceOptions(args, PerformanceListOptions);
        if (optionError is not null)
            return FailSearch(optionError, outputJson, logger, "web.performance.list");
        var required = RequirePerformanceValues(args, "--database", "--site");
        if (required is not null)
            return FailSearch(required, outputJson, logger, "web.performance.list");
        try
        {
            var store = new SqliteWebSearchObservationStore(RequiredPerformanceOption(args, "--database"));
            var evidence = store.QueryPerformanceEvidenceAsync(new WebPerformanceObservationQuery
            {
                SiteId = RequiredPerformanceOption(args, "--site"),
                Provider = TryGetOptionValue(args, "--provider"),
                MeasurementKind = TryGetOptionValue(args, "--kind"),
                TargetUrl = TryGetOptionValue(args, "--target"),
                FormFactor = TryGetOptionValue(args, "--form-factor")
            }).GetAwaiter().GetResult();
            var state = !evidence.StoreExists ? "missing-store" : !evidence.HasEvidence ? "no-evidence" : evidence.HasPartialEvidence ? "partial" : "complete";
            var result = new WebPerformanceListCommandResult
            {
                SiteId = RequiredPerformanceOption(args, "--site").Trim().ToLowerInvariant(),
                EvidenceState = state,
                StoreExists = evidence.StoreExists,
                HasEvidence = evidence.HasEvidence,
                HasPartialEvidence = evidence.HasPartialEvidence,
                HasExplicitZeroEvidence = evidence.HasExplicitZeroEvidence,
                EvidenceSets = evidence.EvidenceSets
            };
            var exitCode = state == "complete" ? 0 : state == "missing-store" ? 2 : 1;
            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion, Command = "web.performance.list", Success = exitCode == 0, ExitCode = exitCode,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebPerformanceListCommandResult)
                });
            }
            else
            {
                if (state == "missing-store") logger.Error("Performance database does not exist.");
                else if (state == "no-evidence") logger.Warn("No performance evidence matches the requested filters.");
                else if (state == "partial") logger.Warn("Performance evidence is partial; inspect selected run provenance.");
                else if (result.HasExplicitZeroEvidence && result.EvidenceSets.All(set => set.Observations.Length == 0)) logger.Info("Performance collection explicitly confirmed no field record.");
                logger.Info($"Performance runs: {result.EvidenceSets.Length}; metrics: {result.EvidenceSets.Sum(set => set.Observations.Length)}.");
            }
            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.performance.list");
        }
    }

    private static PerformanceProviderContext ResolvePerformanceProvider(string[] args, string kind, string capability)
    {
        var loaded = WebSearchProviderConfigurationLoader.LoadWithPath(RequiredPerformanceOption(args, "--config"), WebCliJson.Options);
        var siteId = RequiredPerformanceOption(args, "--site");
        var providerId = RequiredPerformanceOption(args, "--provider");
        var site = loaded.Configuration.Sites.SingleOrDefault(value => value.Id.Equals(siteId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Performance site '{siteId}' is not configured.");
        var provider = site.Providers.SingleOrDefault(value => value.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Performance provider '{providerId}' is not configured for site '{siteId}'.");
        if (!provider.Enabled) throw new ArgumentException($"Performance provider '{providerId}' is disabled.");
        if (!provider.Kind.Equals(kind, StringComparison.Ordinal)) throw new ArgumentException($"Performance action requires provider kind '{kind}'.");
        if (!provider.Capabilities.Contains(capability, StringComparer.Ordinal)) throw new ArgumentException($"Provider must request capability '{capability}'.");
        var doctor = InspectProviderAction(
            loaded.Configuration,
            site,
            provider,
            capability,
            useSelectedCredential: provider.Credential is not null);
        return new PerformanceProviderContext(loaded.FullPath, doctor.ConfigurationHash!, site, provider);
    }

    private static int WritePerformanceResult(string command, bool success, int exitCode, string configPath,
        WebPerformanceCollectionCommandResult result, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion, string message)
    {
        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion, Command = command, Success = success, ExitCode = exitCode, ConfigPath = configPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebPerformanceCollectionCommandResult)
            });
        }
        else if (success) logger.Success(message);
        else logger.Error(message);
        return exitCode;
    }

    private static string? ValidatePerformanceOptions(string[] args, IReadOnlyCollection<string> allowed)
    {
        var missing = FindSearchOptionWithoutValue(args, allowed.ToArray());
        if (missing is not null) return $"{missing} requires a value.";
        foreach (var option in allowed)
            if (args.Count(value => value.Equals(option, StringComparison.OrdinalIgnoreCase)) > 1)
                return $"Performance accepts {option} only once.";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--json", StringComparison.OrdinalIgnoreCase) || args[index].Equals("--output-json", StringComparison.OrdinalIgnoreCase)) continue;
            if (!allowed.Contains(args[index], StringComparer.OrdinalIgnoreCase)) return $"Performance does not recognize argument '{args[index]}'.";
            index++;
        }
        var output = TryGetOptionValue(args, "--output");
        return string.IsNullOrWhiteSpace(output) || output.Equals("json", StringComparison.OrdinalIgnoreCase) ? null : "Performance supports only '--output json'.";
    }

    private static string? RequirePerformanceValues(string[] args, params string[] names) =>
        names.FirstOrDefault(name => string.IsNullOrWhiteSpace(TryGetOptionValue(args, name))) is string missing
            ? $"Performance requires {string.Join(", ", names)}; missing {missing}."
            : null;

    private static string RequiredPerformanceOption(string[] args, string name) =>
        TryGetOptionValue(args, name) ?? throw new ArgumentException($"Performance requires {name}.");

    private sealed record PerformanceProviderContext(string ConfigPath, string ConfigurationHash,
        WebSearchSiteProviderConfiguration Site, WebSearchProviderRegistration Provider);
}

internal sealed class WebPerformanceCollectionCommandResult
{
    public WebPerformanceObservationBatch Batch { get; set; } = new();
    public int RequestCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public WebPerformanceObservationImportResult? Import { get; set; }
}

internal sealed class WebPerformanceListCommandResult
{
    public string SiteId { get; set; } = string.Empty;
    public string EvidenceState { get; set; } = string.Empty;
    public bool StoreExists { get; set; }
    public bool HasEvidence { get; set; }
    public bool HasPartialEvidence { get; set; }
    public bool HasExplicitZeroEvidence { get; set; }
    public WebPerformanceObservationEvidenceSet[] EvidenceSets { get; set; } = Array.Empty<WebPerformanceObservationEvidenceSet>();
}
