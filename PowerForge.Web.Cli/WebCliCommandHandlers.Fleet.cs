using System.Globalization;
using System.Text.RegularExpressions;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static readonly string[] FleetReadOptions = ["--config", "--database", "--as-of", "--output"];
    private static readonly string[] FleetPruneOptions = ["--config", "--database", "--as-of", "--output"];
    private static readonly string[] FleetPruneFlags = ["--apply"];
    private static readonly Regex ExplicitOffsetPattern = new(
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    internal static int HandleFleet(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (args.Length == 0)
            return FailSearch("Fleet requires the 'schedule', 'report', or 'prune' action.", outputJson, logger, "web.fleet");
        return args[0].ToLowerInvariant() switch
        {
            "schedule" => HandleFleetSchedule(args[1..], outputJson, logger, outputSchemaVersion),
            "report" => HandleFleetReport(args[1..], outputJson, logger, outputSchemaVersion),
            "prune" => HandleFleetPrune(args[1..], outputJson, logger, outputSchemaVersion),
            _ => FailSearch("Fleet requires the 'schedule', 'report', or 'prune' action.", outputJson, logger, "web.fleet")
        };
    }

    private static int HandleFleetSchedule(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var prepared = PrepareFleetCommand(args, FleetReadOptions, Array.Empty<string>(), outputJson, logger, "web.fleet.schedule");
        if (prepared.ExitCode.HasValue)
            return prepared.ExitCode.Value;
        try
        {
            var doctor = WebSearchProviderDoctor.InspectWithCapabilities(
                prepared.Configuration!, WebSearchCollectorCatalog.AvailableCapabilities);
            var snapshot = new SqliteWebSearchObservationStore(prepared.DatabasePath!).ReadFleetSnapshotAsync(prepared.AsOfUtc).GetAwaiter().GetResult();
            var plan = WebSearchFleetPlanner.CreateSchedule(prepared.Configuration!, doctor, snapshot, prepared.AsOfUtc);
            var hasActionableWork = plan.WorkItems.Any(value => value.Readiness is "ready" or "input-required");
            var exitCode = plan.ConfigurationValid || hasActionableWork ? 0 : 2;
            if (outputJson)
                WriteFleetEnvelope("web.fleet.schedule", plan, WebCliJson.Context.WebSearchFleetSchedulePlan, exitCode, prepared.ConfigPath!, outputSchemaVersion);
            else
            {
                logger.Info($"Fleet work items: {plan.WorkItems.Length}; operations: {plan.OperationsHash}.");
                foreach (var item in plan.WorkItems)
                {
                    var range = item.FromDate.HasValue
                        ? $" {item.FromDate:yyyy-MM-dd}..{item.ThroughDate:yyyy-MM-dd}"
                        : $" due {item.DueAtUtc:O}";
                    logger.Info($"[{item.Readiness}] {EscapeSearchConsoleText(item.SiteId, "(site)")}/{EscapeSearchConsoleText(item.ProviderId, "(provider)")} {item.Action}{range}{(item.HasMoreBackfill ? " (more backfill remains)" : string.Empty)}");
                }
            }
            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.fleet.schedule");
        }
    }

    private static int HandleFleetReport(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var prepared = PrepareFleetCommand(args, FleetReadOptions, Array.Empty<string>(), outputJson, logger, "web.fleet.report");
        if (prepared.ExitCode.HasValue)
            return prepared.ExitCode.Value;
        try
        {
            var doctor = WebSearchProviderDoctor.InspectWithCapabilities(
                prepared.Configuration!, WebSearchCollectorCatalog.AvailableCapabilities);
            var snapshot = new SqliteWebSearchObservationStore(prepared.DatabasePath!).ReadFleetSnapshotAsync(prepared.AsOfUtc).GetAwaiter().GetResult();
            var report = WebSearchFleetPlanner.CreateReport(prepared.Configuration!, doctor, snapshot, prepared.AsOfUtc);
            var exitCode = report.ConfigurationValid ? report.NeedsAttention ? 1 : 0 : 2;
            if (outputJson)
                WriteFleetEnvelope("web.fleet.report", report, WebCliJson.Context.WebSearchFleetReport, exitCode, prepared.ConfigPath!, outputSchemaVersion);
            else
            {
                logger.Info($"Fleet sites: {report.SiteCount}; providers: {report.ProviderCount}; evidence streams: {report.Rows.Length}.");
                foreach (var row in report.Rows)
                    logger.Info($"[{row.EvidenceState}] {EscapeSearchConsoleText(row.SiteId, "(site)")}/{EscapeSearchConsoleText(row.ProviderId, "(provider)")} {EscapeSearchConsoleText(row.Capability, "(no capability)")}");
            }
            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.fleet.report");
        }
    }

    private static int HandleFleetPrune(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var prepared = PrepareFleetCommand(args, FleetPruneOptions, FleetPruneFlags, outputJson, logger, "web.fleet.prune");
        if (prepared.ExitCode.HasValue)
            return prepared.ExitCode.Value;
        try
        {
            var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
            var policy = prepared.Configuration!.Operations ?? new WebSearchFleetOperationsConfiguration();
            var result = new SqliteWebSearchObservationStore(prepared.DatabasePath!)
                .ApplyFleetRetentionAsync(policy, prepared.AsOfUtc, apply).GetAwaiter().GetResult();
            var exitCode = result.StoreExists ? 0 : 2;
            if (outputJson)
                WriteFleetEnvelope("web.fleet.prune", result, WebCliJson.Context.WebSearchFleetRetentionResult, exitCode, prepared.ConfigPath!, outputSchemaVersion);
            else if (!result.StoreExists)
                logger.Error("Fleet database does not exist.");
            else
            {
                logger.Info(apply ? "Fleet retention applied." : "Fleet retention dry run; pass --apply to delete candidates.");
                foreach (var kind in result.Kinds)
                    logger.Info($"{kind.Kind}: {kind.CandidateRunCount} candidate runs / {kind.CandidateObservationCount} observations; deleted {kind.DeletedRunCount} / {kind.DeletedObservationCount}.");
            }
            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.fleet.prune");
        }
    }

    private static FleetCommandPreparation PrepareFleetCommand(
        string[] args,
        string[] valueOptions,
        string[] flagOptions,
        bool outputJson,
        WebConsoleLogger logger,
        string command)
    {
        var optionError = ValidateFleetOptions(args, valueOptions, flagOptions);
        if (optionError is not null)
            return new FleetCommandPreparation { ExitCode = FailSearch(optionError, outputJson, logger, command) };
        var required = RequirePerformanceValues(args, "--config", "--database");
        if (required is not null)
            return new FleetCommandPreparation { ExitCode = FailSearch(required, outputJson, logger, command) };
        try
        {
            var loaded = WebSearchProviderConfigurationLoader.LoadWithPath(RequiredPerformanceOption(args, "--config"), WebCliJson.Options);
            WebSearchFleetPlanner.ValidatePolicy(loaded.Configuration.Operations ?? new WebSearchFleetOperationsConfiguration());
            return new FleetCommandPreparation
            {
                Configuration = loaded.Configuration,
                ConfigPath = loaded.FullPath,
                DatabasePath = Path.GetFullPath(RequiredPerformanceOption(args, "--database").Trim().Trim('"')),
                AsOfUtc = ParseFleetAsOf(TryGetOptionValue(args, "--as-of"))
            };
        }
        catch (Exception ex)
        {
            return new FleetCommandPreparation { ExitCode = FailSearch(ex.Message, outputJson, logger, command) };
        }
    }

    private static string? ValidateFleetOptions(
        string[] args,
        IReadOnlyCollection<string> valueOptions,
        IReadOnlyCollection<string> flagOptions)
    {
        var missing = FindSearchOptionWithoutValue(args, valueOptions.ToArray());
        if (missing is not null)
            return $"{missing} requires a value.";
        foreach (var option in valueOptions.Concat(flagOptions))
        {
            if (args.Count(value => value.Equals(option, StringComparison.OrdinalIgnoreCase)) > 1)
                return $"Fleet accepts {option} only once.";
        }
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--json", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--output-json", StringComparison.OrdinalIgnoreCase) ||
                flagOptions.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (valueOptions.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }
            return $"Fleet does not recognize argument '{argument}'.";
        }
        var output = TryGetOptionValue(args, "--output");
        return string.IsNullOrWhiteSpace(output) || output.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Fleet supports only '--output json'.";
    }

    private static DateTimeOffset ParseFleetAsOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTimeOffset.UtcNow;
        var trimmed = value.Trim();
        if (!ExplicitOffsetPattern.IsMatch(trimmed) ||
            !DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new ArgumentException("--as-of must be an ISO-8601 timestamp with an explicit UTC offset.", nameof(value));
        return parsed.ToUniversalTime();
    }

    private static void WriteFleetEnvelope<T>(
        string command,
        T result,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        int exitCode,
        string configPath,
        int outputSchemaVersion) => WebCliJsonWriter.Write(new WebCliJsonEnvelope
        {
            SchemaVersion = outputSchemaVersion,
            Command = command,
            Success = exitCode == 0,
            ExitCode = exitCode,
            ConfigPath = configPath,
            Result = WebCliJson.SerializeToElement(result, typeInfo)
        });

    private sealed class FleetCommandPreparation
    {
        internal int? ExitCode { get; set; }
        internal WebSearchProviderConfiguration? Configuration { get; set; }
        internal string? ConfigPath { get; set; }
        internal string? DatabasePath { get; set; }
        internal DateTimeOffset AsOfUtc { get; set; }
    }
}
