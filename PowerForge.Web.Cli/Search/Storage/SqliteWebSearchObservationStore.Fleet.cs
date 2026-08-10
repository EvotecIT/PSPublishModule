using System.Data;
using System.Text.Json;
using DBAClientX;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed partial class SqliteWebSearchObservationStore
{
    internal async Task<WebSearchFleetEvidenceSnapshot> ReadFleetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_databasePath))
            return new WebSearchFleetEvidenceSnapshot { StoreExists = false };

        await using var client = new SQLite();
        var version = await ReadCurrentFleetSchemaVersionAsync(client, cancellationToken).ConfigureAwait(false);
        var search = await ReadSearchFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var traffic = await ReadTrafficFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var performance = await ReadPerformanceFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var streams = BuildSearchStreams(search)
            .Concat(BuildTrafficStreams(traffic))
            .Concat(BuildPerformanceStreams(performance))
            .OrderBy(value => value.SiteId, StringComparer.Ordinal)
            .ThenBy(value => value.ProviderId, StringComparer.Ordinal)
            .ThenBy(value => value.Capability, StringComparer.Ordinal)
            .ToArray();
        return new WebSearchFleetEvidenceSnapshot
        {
            StoreExists = true,
            DatabaseSchemaVersion = version,
            Streams = streams
        };
    }

    internal async Task<WebSearchFleetRetentionResult> ApplyFleetRetentionAsync(
        WebSearchFleetOperationsConfiguration policy,
        DateTimeOffset asOfUtc,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        WebSearchFleetPlanner.ValidatePolicy(policy);
        if (!File.Exists(_databasePath))
            return new WebSearchFleetRetentionResult { StoreExists = false, Applied = apply };

        await using var client = new SQLite();
        await ReadCurrentFleetSchemaVersionAsync(client, cancellationToken).ConfigureAwait(false);
        var search = await ReadSearchFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var traffic = await ReadTrafficFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var performance = await ReadPerformanceFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var asOf = asOfUtc.ToUniversalTime();
        var plans = new[]
        {
            CreateRetentionPlan("search", "search_observation_runs", "search_observations",
                search, asOf.AddDays(-policy.SearchRunRetentionDays)),
            CreateRetentionPlan("traffic", "traffic_observation_runs", "traffic_observations",
                traffic, asOf.AddDays(-policy.TrafficRunRetentionDays)),
            CreateRetentionPlan("performance", "performance_observation_runs", "performance_observations",
                performance, asOf.AddDays(-policy.PerformanceRunRetentionDays))
        };

        foreach (var plan in plans)
            plan.Result.CandidateObservationCount = await CountFleetObservationsAsync(client, plan, cancellationToken).ConfigureAwait(false);

        if (apply && plans.Any(value => value.Candidates.Length > 0))
        {
            await using var session = await client.OpenSessionAsync(_databasePath, cancellationToken).ConfigureAwait(false);
            await session.RunInTransactionAsync(async (transaction, token) =>
            {
                foreach (var plan in plans)
                foreach (var run in plan.Candidates)
                {
                    var parameters = FleetRunParameters(run);
                    plan.Result.DeletedObservationCount += await transaction.ExecuteNonQueryAsync(
                        $"DELETE FROM {plan.ObservationTable} WHERE provider = @provider AND site_id = @site_id AND run_id = @run_id;",
                        parameters, token).ConfigureAwait(false);
                    plan.Result.DeletedRunCount += await transaction.ExecuteNonQueryAsync(
                        $"DELETE FROM {plan.RunTable} WHERE provider = @provider AND site_id = @site_id AND run_id = @run_id;",
                        parameters, token).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        return new WebSearchFleetRetentionResult
        {
            StoreExists = true,
            Applied = apply,
            Kinds = plans.Select(value => value.Result).ToArray()
        };
    }

    private async Task<int> ReadCurrentFleetSchemaVersionAsync(SQLite client, CancellationToken cancellationToken)
    {
        var value = await client.ExecuteScalarAsync(_databasePath, "PRAGMA user_version;", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var version = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        if (version != CurrentSchemaVersion)
            throw new InvalidOperationException($"Fleet operations require search database schema v{CurrentSchemaVersion}; found v{version}. Import current evidence to migrate the store before scheduling, reporting, or retention.");
        return version;
    }

    private async Task<FleetRun[]> ReadSearchFleetRunsAsync(SQLite client, CancellationToken cancellationToken)
    {
        var manifests = await client.QueryAsListAsync(_databasePath,
            "SELECT normalized_manifest_json FROM search_observation_runs;", static record => record.GetString(0),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return manifests.Select(value => JsonSerializer.Deserialize<WebSearchObservationBatch>(value, WebCliJson.Options)
                                         ?? throw new InvalidOperationException("Stored search manifest is empty."))
            .Select(WebSearchObservationNormalizer.Normalize)
            .Select(batch => new FleetRun(
                "search", batch.Provider, batch.SiteId, batch.RunId!, batch.CollectedAtUtc, batch.Status,
                SearchStreamKey(batch), SearchCompletedRanges(batch)))
            .ToArray();
    }

    private async Task<FleetRun[]> ReadTrafficFleetRunsAsync(SQLite client, CancellationToken cancellationToken)
    {
        var manifests = await client.QueryAsListAsync(_databasePath,
            "SELECT normalized_manifest_json FROM traffic_observation_runs;", static record => record.GetString(0),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return manifests.Select(value => JsonSerializer.Deserialize<WebTrafficObservationBatch>(value, WebCliJson.Options)
                                         ?? throw new InvalidOperationException("Stored traffic manifest is empty."))
            .Select(WebTrafficObservationNormalizer.Normalize)
            .Select(batch => new FleetRun(
                "traffic", batch.Provider, batch.SiteId, batch.RunId!, batch.CollectedAtUtc, batch.Status,
                "traffic", TrafficCompletedRanges(batch)))
            .ToArray();
    }

    private async Task<FleetRun[]> ReadPerformanceFleetRunsAsync(SQLite client, CancellationToken cancellationToken)
    {
        var manifests = await client.QueryAsListAsync(_databasePath,
            "SELECT normalized_manifest_json FROM performance_observation_runs;", static record => record.GetString(0),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return manifests.Select(value => JsonSerializer.Deserialize<WebPerformanceObservationBatch>(value, WebCliJson.Options)
                                         ?? throw new InvalidOperationException("Stored performance manifest is empty."))
            .Select(WebPerformanceObservationNormalizer.Normalize)
            .Select(batch => new FleetRun(
                "performance", batch.Provider, batch.SiteId, batch.RunId!, batch.CollectedAtUtc, batch.Status,
                string.Join("\u001f", batch.MeasurementKind, batch.TargetKind, batch.TargetUrl, batch.FormFactor),
                Array.Empty<WebSearchFleetCompletedRange>(),
                batch.MeasurementKind))
            .ToArray();
    }

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildSearchStreams(IEnumerable<FleetRun> runs) =>
        BuildDailyStreams(runs, WebSearchProviderCapabilities.SearchAnalytics);

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildTrafficStreams(IEnumerable<FleetRun> runs) =>
        BuildDailyStreams(runs, WebSearchProviderCapabilities.TrafficAnalytics);

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildDailyStreams(IEnumerable<FleetRun> runs, string capability) =>
        runs.GroupBy(value => (value.Provider, value.SiteId, value.StreamKey))
            .Select(group =>
            {
                var ranges = MergeRanges(group.SelectMany(value => value.CompletedRanges));
                return BuildStream(group, capability, ranges.LastOrDefault()?.ThroughDate, ranges);
            });

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildPerformanceStreams(IEnumerable<FleetRun> runs) =>
        runs.GroupBy(value => (value.Provider, value.SiteId, value.MeasurementKind, value.StreamKey))
            .Select(group => BuildStream(group,
                group.Key.MeasurementKind == "lab"
                    ? WebSearchProviderCapabilities.PerformanceLighthouse
                    : WebSearchProviderCapabilities.PerformanceCrux,
                null,
                Array.Empty<WebSearchFleetCompletedRange>()));

    private static WebSearchFleetEvidenceStream BuildStream(
        IEnumerable<FleetRun> values,
        string capability,
        DateOnly? latestDate,
        WebSearchFleetCompletedRange[] ranges)
    {
        var runs = values.OrderByDescending(value => value.CollectedAtUtc).ThenByDescending(value => value.RunId, StringComparer.Ordinal).ToArray();
        var latest = runs[0];
        var completed = runs.Where(value => value.Status == "complete" || value.CompletedRanges.Length > 0).ToArray();
        return new WebSearchFleetEvidenceStream
        {
            SiteId = latest.SiteId,
            ProviderId = latest.Provider,
            Capability = capability,
            ScopeKey = latest.StreamKey,
            LatestCompleteDate = latestDate,
            CompletedRanges = ranges,
            LastCompleteAtUtc = completed.Length == 0 ? null : completed.Max(value => value.CollectedAtUtc),
            LastAttemptAtUtc = latest.CollectedAtUtc,
            HasPartialEvidence = latest.Status == "partial",
            RunCount = runs.Length
        };
    }

    private static RetentionPlan CreateRetentionPlan(
        string kind,
        string runTable,
        string observationTable,
        FleetRun[] runs,
        DateTimeOffset cutoffUtc)
    {
        var preserved = runs
            .GroupBy(value => (value.Provider, value.SiteId, value.StreamKey))
            .Select(SelectRetentionPreservationRun)
            .Select(FleetRunIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = runs
            .Where(value => value.CollectedAtUtc < cutoffUtc && !preserved.Contains(FleetRunIdentity(value)))
            .OrderBy(value => value.CollectedAtUtc)
            .ToArray();
        return new RetentionPlan(runTable, observationTable, candidates, new WebSearchFleetRetentionKindResult
        {
            Kind = kind,
            CutoffUtc = cutoffUtc,
            CandidateRunCount = candidates.Length
        });
    }

    private static FleetRun SelectRetentionPreservationRun(IEnumerable<FleetRun> values)
    {
        var runs = values.ToArray();
        if (runs.Any(value => value.CompletedRanges.Length > 0))
        {
            return runs
                .OrderByDescending(value => value.CompletedRanges.Select(range => (DateOnly?)range.ThroughDate).Max())
                .ThenByDescending(value => value.CompletedRanges.Sum(range => range.ThroughDate.DayNumber - range.FromDate.DayNumber + 1))
                .ThenByDescending(value => value.CollectedAtUtc)
                .ThenByDescending(value => value.RunId, StringComparer.Ordinal)
                .First();
        }

        return runs.OrderBy(value => value.Status == "complete" ? 0 : 1)
            .ThenByDescending(value => value.CollectedAtUtc)
            .ThenByDescending(value => value.RunId, StringComparer.Ordinal)
            .First();
    }

    private async Task<int> CountFleetObservationsAsync(SQLite client, RetentionPlan plan, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var run in plan.Candidates)
        {
            var rows = await client.QueryAsListAsync(_databasePath,
                $"SELECT COUNT(*) FROM {plan.ObservationTable} WHERE provider = @provider AND site_id = @site_id AND run_id = @run_id;",
                static record => Convert.ToInt32(record.GetInt64(0)), FleetRunParameters(run), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            count += rows.Single();
        }
        return count;
    }

    private static string SearchStreamKey(WebSearchObservationBatch batch) =>
        batch.CollectionCoverage?.SearchType?.Trim().ToLowerInvariant() ?? "web";

    private static WebSearchFleetCompletedRange[] SearchCompletedRanges(WebSearchObservationBatch batch)
    {
        if (batch.CollectionCoverage is { } coverage)
        {
            if (batch.Status == "complete")
                return [new WebSearchFleetCompletedRange { FromDate = coverage.FromDate, ThroughDate = coverage.ThroughDate }];
            return MergeDates(coverage.CompletedDates);
        }
        return MergeDates(batch.Observations.Select(value => value.Date));
    }

    private static WebSearchFleetCompletedRange[] TrafficCompletedRanges(WebTrafficObservationBatch batch)
    {
        if (batch.Status == "complete")
            return [new WebSearchFleetCompletedRange
            {
                FromDate = batch.CollectionCoverage.FromDate,
                ThroughDate = batch.CollectionCoverage.ThroughDate
            }];
        return MergeDates(batch.CollectionCoverage.CompletedDates);
    }

    private static WebSearchFleetCompletedRange[] MergeDates(IEnumerable<DateOnly> dates) => MergeRanges(
        dates.Distinct().Select(value => new WebSearchFleetCompletedRange { FromDate = value, ThroughDate = value }));

    private static WebSearchFleetCompletedRange[] MergeRanges(IEnumerable<WebSearchFleetCompletedRange> values)
    {
        var ordered = values.OrderBy(value => value.FromDate).ThenBy(value => value.ThroughDate).ToArray();
        if (ordered.Length == 0)
            return Array.Empty<WebSearchFleetCompletedRange>();
        var merged = new List<WebSearchFleetCompletedRange>();
        var current = new WebSearchFleetCompletedRange { FromDate = ordered[0].FromDate, ThroughDate = ordered[0].ThroughDate };
        foreach (var range in ordered.Skip(1))
        {
            if (range.FromDate.DayNumber <= current.ThroughDate.DayNumber + 1)
            {
                if (range.ThroughDate > current.ThroughDate)
                    current.ThroughDate = range.ThroughDate;
                continue;
            }
            merged.Add(current);
            current = new WebSearchFleetCompletedRange { FromDate = range.FromDate, ThroughDate = range.ThroughDate };
        }
        merged.Add(current);
        return merged.ToArray();
    }

    private static string FleetRunIdentity(FleetRun run) => string.Join("\u001f", run.Provider, run.SiteId, run.RunId);

    private static Dictionary<string, object?> FleetRunParameters(FleetRun run) => new()
    {
        ["@provider"] = run.Provider,
        ["@site_id"] = run.SiteId,
        ["@run_id"] = run.RunId
    };

    private sealed record FleetRun(
        string Kind,
        string Provider,
        string SiteId,
        string RunId,
        DateTimeOffset CollectedAtUtc,
        string Status,
        string StreamKey,
        WebSearchFleetCompletedRange[] CompletedRanges,
        string? MeasurementKind = null);

    private sealed record RetentionPlan(
        string RunTable,
        string ObservationTable,
        FleetRun[] Candidates,
        WebSearchFleetRetentionKindResult Result);
}
