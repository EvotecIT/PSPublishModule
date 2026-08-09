using System.Globalization;
using System.Text.Json;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static int HandleObserve(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        if (subArgs.Length == 0 || !subArgs[0].Equals("import", StringComparison.OrdinalIgnoreCase))
            return Fail("Observe requires the 'import' action.", outputJson, logger, "web.observe.import");

        var args = subArgs.Skip(1).ToArray();
        var inputPath = TryGetOptionValue(args, "--input");
        var databasePath = TryGetOptionValue(args, "--database");
        if (string.IsNullOrWhiteSpace(inputPath))
            return Fail("Observe import requires --input.", outputJson, logger, "web.observe.import");
        if (string.IsNullOrWhiteSpace(databasePath))
            return Fail("Observe import requires --database.", outputJson, logger, "web.observe.import");

        try
        {
            var fullInputPath = ResolveExistingFilePath(inputPath);
            var fullDatabasePath = Path.GetFullPath(databasePath.Trim().Trim('"'));
            var batch = JsonSerializer.Deserialize<WebSearchObservationBatch>(
                File.ReadAllText(fullInputPath),
                WebCliJson.Options);
            if (batch is null)
                return Fail("Observe import input is not a valid search observation batch.", outputJson, logger, "web.observe.import");

            var providerOverride = TryGetOptionValue(args, "--provider");
            var siteOverride = TryGetOptionValue(args, "--site");
            if (!string.IsNullOrWhiteSpace(providerOverride))
                batch.Provider = providerOverride;
            if (!string.IsNullOrWhiteSpace(siteOverride))
                batch.SiteId = siteOverride;

            var normalized = WebSearchObservationNormalizer.Normalize(batch);
            var store = new SqliteWebSearchObservationStore(fullDatabasePath);
            var result = store.ImportAsync(normalized).GetAwaiter().GetResult();

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.observe.import",
                    Success = true,
                    ExitCode = 0,
                    ConfigPath = fullInputPath,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebSearchObservationImportResult)
                });
            }
            else
            {
                logger.Success(
                    $"Imported {result.InsertedCount} search observations for {result.SiteId} from {result.Provider}; {result.DuplicateCount} duplicates ignored.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, outputJson, logger, "web.observe.import");
        }
    }

    internal static int HandleOpportunity(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        if (subArgs.Length == 0 || !subArgs[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            return Fail("Opportunity requires the 'list' action.", outputJson, logger, "web.opportunity.list");

        var args = subArgs.Skip(1).ToArray();
        var databasePath = TryGetOptionValue(args, "--database");
        var siteId = TryGetOptionValue(args, "--site");
        if (string.IsNullOrWhiteSpace(databasePath))
            return Fail("Opportunity list requires --database.", outputJson, logger, "web.opportunity.list");
        if (string.IsNullOrWhiteSpace(siteId))
            return Fail("Opportunity list requires --site.", outputJson, logger, "web.opportunity.list");

        try
        {
            var provider = TryGetOptionValue(args, "--provider");
            var fromDate = ParseOptionalDate(TryGetOptionValue(args, "--from"), "--from");
            var throughDate = ParseOptionalDate(TryGetOptionValue(args, "--to"), "--to");
            var minimumImpressions = ParsePositiveLong(
                TryGetOptionValue(args, "--min-impressions"),
                100,
                "--min-impressions");
            var minimumCtr = ParseRate(TryGetOptionValue(args, "--min-ctr"), 0.02d, "--min-ctr");
            var fullDatabasePath = Path.GetFullPath(databasePath.Trim().Trim('"'));
            if (!File.Exists(fullDatabasePath))
                return Fail($"Search database not found: {fullDatabasePath}", outputJson, logger, "web.opportunity.list");
            var store = new SqliteWebSearchObservationStore(fullDatabasePath);
            var observations = store.QueryAsync(new WebSearchObservationQuery
            {
                SiteId = siteId,
                Provider = provider,
                FromDate = fromDate,
                ThroughDate = throughDate
            }).GetAwaiter().GetResult();

            var report = WebSearchOpportunityAnalyzer.Analyze(
                observations,
                new WebSearchOpportunityOptions
                {
                    SiteId = siteId,
                    Provider = provider,
                    FromDate = fromDate,
                    ThroughDate = throughDate,
                    MinimumImpressions = minimumImpressions,
                    MinimumClickThroughRate = minimumCtr
                },
                DateTimeOffset.UtcNow);

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.opportunity.list",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(report, WebCliJson.Context.WebSearchOpportunityReport)
                });
            }
            else
            {
                logger.Info($"Search observations: {report.ObservationCount}; opportunities: {report.Opportunities.Length}.");
                foreach (var opportunity in report.Opportunities)
                {
                    logger.Info(
                        $"[{opportunity.RuleId}] score {opportunity.Score:F2} {opportunity.Page} :: {opportunity.Query ?? "(all queries)"}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, outputJson, logger, "web.opportunity.list");
        }
    }

    private static DateOnly? ParseOptionalDate(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException($"{optionName} must use yyyy-MM-dd.", optionName);
        return date;
    }

    private static long ParsePositiveLong(string? value, long fallback, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
            throw new ArgumentException($"{optionName} must be a positive integer.", optionName);
        return parsed;
    }

    private static double ParseRate(string? value, double fallback, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed <= 0d ||
            parsed > 1d)
        {
            throw new ArgumentException($"{optionName} must be greater than zero and at most one.", optionName);
        }
        return parsed;
    }
}
