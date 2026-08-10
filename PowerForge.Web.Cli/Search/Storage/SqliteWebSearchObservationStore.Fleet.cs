using System.Data;
using System.Globalization;
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
        var runs = await client.QueryAsListAsync(_databasePath,
            """
            SELECT run_id, provider, site_id, collected_at_utc, status,
                   json_extract(normalized_manifest_json, '$.collectionCoverage.mode'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.fromDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.throughDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.searchType')
            FROM search_observation_runs;
            """,
            static record => new SearchFleetRunMetadata(
                record.GetString(0), record.GetString(1), record.GetString(2), ParseFleetTimestamp(record.GetString(3)), record.GetString(4),
                NullableString(record, 5), NullableDate(record, 6), NullableDate(record, 7), NullableString(record, 8)),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var coverageDates = await client.QueryAsListAsync(_databasePath,
            """
            SELECT r.provider, r.site_id, r.run_id, dates.value
            FROM search_observation_runs r
            JOIN json_each(r.normalized_manifest_json, '$.collectionCoverage.completedDates') dates;
            """,
            static record => new FleetDatedScope(record.GetString(0), record.GetString(1), record.GetString(2),
                ParseFleetDate(record.GetString(3)), string.Empty), cancellationToken: cancellationToken).ConfigureAwait(false);
        var observationScopes = await client.QueryAsListAsync(_databasePath,
            """
            SELECT provider, site_id, run_id, observation_date,
                   COALESCE(NULLIF(LOWER(TRIM(search_type)), ''), 'web')
            FROM search_observations
            GROUP BY provider, site_id, run_id, observation_date, COALESCE(NULLIF(LOWER(TRIM(search_type)), ''), 'web');
            """,
            static record => new FleetDatedScope(record.GetString(0), record.GetString(1), record.GetString(2),
                ParseFleetDate(record.GetString(3)), record.GetString(4)), cancellationToken: cancellationToken).ConfigureAwait(false);

        var coverageByRun = coverageDates.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var observationsByRun = observationScopes.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var result = new List<FleetRun>();
        foreach (var run in runs)
        {
            var identity = FleetRunIdentity(run.Provider, run.SiteId, run.RunId);
            if (run.FromDate.HasValue && run.ThroughDate.HasValue)
            {
                var explicitDates = coverageByRun[identity].Select(value => value.Date);
                var ranges = run.Status == "complete" && string.Equals(run.CoverageMode, "daily", StringComparison.Ordinal)
                    ? [new WebSearchFleetCompletedRange { FromDate = run.FromDate.Value, ThroughDate = run.ThroughDate.Value }]
                    : MergeDates(explicitDates);
                result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                    run.SearchType?.Trim().ToLowerInvariant() ?? "web", ranges));
                continue;
            }

            foreach (var scope in observationsByRun[identity].GroupBy(value => value.Scope, StringComparer.Ordinal))
            {
                result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                    scope.Key, MergeDates(scope.Select(value => value.Date))));
            }
        }
        return result.ToArray();
    }

    private async Task<FleetRun[]> ReadTrafficFleetRunsAsync(SQLite client, CancellationToken cancellationToken)
    {
        var runs = await client.QueryAsListAsync(_databasePath,
            """
            SELECT run_id, provider, site_id, collected_at_utc, status,
                   json_extract(normalized_manifest_json, '$.collectionCoverage.fromDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.throughDate')
            FROM traffic_observation_runs;
            """,
            static record => new TrafficFleetRunMetadata(
                record.GetString(0), record.GetString(1), record.GetString(2), ParseFleetTimestamp(record.GetString(3)), record.GetString(4),
                NullableDate(record, 5), NullableDate(record, 6)),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var coverageDates = await client.QueryAsListAsync(_databasePath,
            """
            SELECT r.provider, r.site_id, r.run_id, dates.value
            FROM traffic_observation_runs r
            JOIN json_each(r.normalized_manifest_json, '$.collectionCoverage.completedDates') dates;
            """,
            static record => new FleetDatedScope(record.GetString(0), record.GetString(1), record.GetString(2),
                ParseFleetDate(record.GetString(3)), "traffic"), cancellationToken: cancellationToken).ConfigureAwait(false);
        var coverageByRun = coverageDates.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        return runs.Select(run =>
        {
            var ranges = run.Status == "complete" && run.FromDate.HasValue && run.ThroughDate.HasValue
                ? [new WebSearchFleetCompletedRange { FromDate = run.FromDate.Value, ThroughDate = run.ThroughDate.Value }]
                : MergeDates(coverageByRun[FleetRunIdentity(run.Provider, run.SiteId, run.RunId)].Select(value => value.Date));
            return new FleetRun("traffic", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status, "traffic", ranges);
        }).ToArray();
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
            .GroupBy(FleetRunIdentity, StringComparer.Ordinal)
            .Select(value => value.First())
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

    private static string FleetRunIdentity(string provider, string siteId, string runId) =>
        string.Join("\u001f", provider, siteId, runId);

    private static string FleetScopeIdentity(FleetDatedScope value) => FleetRunIdentity(value.Provider, value.SiteId, value.RunId);

    private static string? NullableString(IDataRecord record, int index) => record.IsDBNull(index) ? null : record.GetString(index);

    private static DateOnly? NullableDate(IDataRecord record, int index) =>
        record.IsDBNull(index) ? null : ParseFleetDate(record.GetString(index));

    private static DateOnly ParseFleetDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException("Stored fleet coverage contains an invalid date.");

    private static DateTimeOffset ParseFleetTimestamp(string value) =>
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            ? timestamp.ToUniversalTime()
            : throw new InvalidOperationException("Stored fleet run contains an invalid collection timestamp.");

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

    private sealed record SearchFleetRunMetadata(
        string RunId,
        string Provider,
        string SiteId,
        DateTimeOffset CollectedAtUtc,
        string Status,
        string? CoverageMode,
        DateOnly? FromDate,
        DateOnly? ThroughDate,
        string? SearchType);

    private sealed record TrafficFleetRunMetadata(
        string RunId,
        string Provider,
        string SiteId,
        DateTimeOffset CollectedAtUtc,
        string Status,
        DateOnly? FromDate,
        DateOnly? ThroughDate);

    private sealed record FleetDatedScope(string Provider, string SiteId, string RunId, DateOnly Date, string Scope);

    private sealed record RetentionPlan(
        string RunTable,
        string ObservationTable,
        FleetRun[] Candidates,
        WebSearchFleetRetentionKindResult Result);
}
