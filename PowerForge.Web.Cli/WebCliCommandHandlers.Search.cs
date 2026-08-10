using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        if (subArgs.Length == 0)
            return FailSearch("Observe requires the 'import' or 'collect' action.", outputJson, logger, "web.observe");

        return subArgs[0].ToLowerInvariant() switch
        {
            "import" => HandleObserveImport(subArgs.Skip(1).ToArray(), outputJson, logger, outputSchemaVersion),
            "collect" => HandleObserveCollect(subArgs.Skip(1).ToArray(), outputJson, logger, outputSchemaVersion),
            _ => FailSearch("Observe requires the 'import' or 'collect' action.", outputJson, logger, "web.observe")
        };
    }

    private static int HandleObserveImport(
        string[] args,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        var missingValueOption = FindSearchOptionWithoutValue(
            args,
            "--input", "--database", "--provider", "--site", "--output");
        if (missingValueOption is not null)
            return FailSearch($"{missingValueOption} requires a value.", outputJson, logger, "web.observe.import");
        var inputPath = TryGetOptionValue(args, "--input");
        var databasePath = TryGetOptionValue(args, "--database");
        if (string.IsNullOrWhiteSpace(inputPath))
            return FailSearch("Observe import requires --input.", outputJson, logger, "web.observe.import");
        if (string.IsNullOrWhiteSpace(databasePath))
            return FailSearch("Observe import requires --database.", outputJson, logger, "web.observe.import");

        try
        {
            var fullInputPath = ResolveExistingFilePath(inputPath);
            var fullDatabasePath = Path.GetFullPath(databasePath.Trim().Trim('"'));
            var providerOverride = TryGetOptionValue(args, "--provider");
            var siteOverride = TryGetOptionValue(args, "--site");
            var batch = DeserializeObservationBatch(
                File.ReadAllText(fullInputPath),
                providerOverride,
                siteOverride);
            if (batch is null)
                return FailSearch("Observe import input is not a valid search observation batch.", outputJson, logger, "web.observe.import");

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
                    $"Imported {result.InsertedCount} search observations for {EscapeSearchConsoleText(result.SiteId, "(unknown site)")} from {EscapeSearchConsoleText(result.Provider, "(unknown provider)")}; {result.DuplicateCount} duplicates ignored.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.observe.import");
        }
    }

    internal static int HandleOpportunity(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        if (subArgs.Length == 0 || !subArgs[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            return FailSearch("Opportunity requires the 'list' action.", outputJson, logger, "web.opportunity.list");

        var args = subArgs.Skip(1).ToArray();
        var missingValueOption = FindSearchOptionWithoutValue(
            args,
            "--database", "--site", "--provider", "--from", "--to", "--min-impressions", "--min-ctr", "--output");
        if (missingValueOption is not null)
            return FailSearch($"{missingValueOption} requires a value.", outputJson, logger, "web.opportunity.list");
        var databasePath = TryGetOptionValue(args, "--database");
        var siteId = TryGetOptionValue(args, "--site");
        if (string.IsNullOrWhiteSpace(databasePath))
            return FailSearch("Opportunity list requires --database.", outputJson, logger, "web.opportunity.list");
        if (string.IsNullOrWhiteSpace(siteId))
            return FailSearch("Opportunity list requires --site.", outputJson, logger, "web.opportunity.list");

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
                return FailSearch($"Search database not found: {fullDatabasePath}", outputJson, logger, "web.opportunity.list");
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
                        $"[{opportunity.RuleId}] score {opportunity.Score:F2} {EscapeSearchConsoleText(opportunity.Page, "(no page)")} :: {EscapeSearchConsoleText(opportunity.Query, "(all queries)")}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.opportunity.list");
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

    private static WebSearchObservationBatch? DeserializeObservationBatch(
        string json,
        string? providerOverride,
        string? siteOverride)
    {
        var document = JsonNode.Parse(
                json,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = WebCliJson.Options.AllowTrailingCommas,
                    CommentHandling = WebCliJson.Options.ReadCommentHandling
                })?.AsObject()
            ?? throw new JsonException("Observe import input must be a JSON object.");
        ApplyIdentityOverride(document, "provider", providerOverride);
        ApplyIdentityOverride(document, "siteId", siteOverride);

        return document.Deserialize<WebSearchObservationBatch>(WebCliJson.Options);
    }

    private static void ApplyIdentityOverride(JsonObject document, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var existingName in document
                     .Select(property => property.Key)
                     .Where(name => string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            document.Remove(existingName);
        }
        document[propertyName] = value;
    }

    private static string? FindSearchOptionWithoutValue(string[] args, params string[] optionNames)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!optionNames.Contains(args[index], StringComparer.OrdinalIgnoreCase))
                continue;
            if (index + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[index];
            }
        }

        return null;
    }

    internal static string EscapeSearchConsoleText(string? value, string fallback)
    {
        if (string.IsNullOrEmpty(value))
            return fallback;

        StringBuilder? escaped = null;
        var segmentStart = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsControl(character) && character is not '\u2028' and not '\u2029')
                continue;

            escaped ??= new StringBuilder(value.Length + 8);
            escaped.Append(value, segmentStart, index - segmentStart);
            escaped.Append("\\u");
            escaped.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            segmentStart = index + 1;
        }

        if (escaped is null)
            return value;
        escaped.Append(value, segmentStart, value.Length - segmentStart);
        return escaped.ToString();
    }

    private static int FailSearch(
        string message,
        bool outputJson,
        WebConsoleLogger logger,
        string command) => Fail(
            outputJson ? message : EscapeSearchConsoleText(message, "Search operation failed."),
            outputJson,
            logger,
            command);
}
