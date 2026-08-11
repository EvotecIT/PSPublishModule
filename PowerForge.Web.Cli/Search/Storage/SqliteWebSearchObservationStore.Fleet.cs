using System.Data;
using System.Globalization;
using System.Text.Json;
using DBAClientX;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed partial class SqliteWebSearchObservationStore
{
    private static readonly IReadOnlySet<string> PermanentFleetFailureCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        "retention-boundary", "duration-boundary", "row-limit-reached"
    };

    internal async Task<WebSearchFleetEvidenceSnapshot> ReadFleetSnapshotAsync(
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_databasePath))
            return new WebSearchFleetEvidenceSnapshot { StoreExists = false };

        await using var client = new SQLite();
        await using var session = await client.OpenSessionAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var readSet = await session.RunInTransactionAsync(async (transaction, token) =>
        {
            var schemaVersion = await ReadCurrentFleetSchemaVersionAsync(transaction, token).ConfigureAwait(false);
            var searchRuns = await ReadSearchFleetRunsAsync(transaction, token).ConfigureAwait(false);
            var trafficRuns = await ReadTrafficFleetRunsAsync(transaction, token).ConfigureAwait(false);
            var performanceRuns = await ReadPerformanceFleetRunsAsync(transaction, token).ConfigureAwait(false);
            var retained = await ReadRetainedCoverageAsync(transaction, token).ConfigureAwait(false);
            return new FleetReadSet(schemaVersion, searchRuns, trafficRuns, performanceRuns, retained);
        }, cancellationToken).ConfigureAwait(false);
        var version = readSet.SchemaVersion;
        var search = readSet.Search;
        var traffic = readSet.Traffic;
        var performance = readSet.Performance;
        var retainedCoverage = readSet.RetainedCoverage;
        if (asOfUtc is DateTimeOffset requestedAsOf)
        {
            var cutoff = requestedAsOf.ToUniversalTime();
            search = search.Where(value => value.CollectedAtUtc <= cutoff).ToArray();
            traffic = traffic.Where(value => value.CollectedAtUtc <= cutoff).ToArray();
            performance = performance.Where(value => value.CollectedAtUtc <= cutoff).ToArray();
            retainedCoverage = retainedCoverage.Where(value => value.SourceCollectedAtUtc <= cutoff).ToArray();
        }
        search = search.Concat(retainedCoverage.Where(value => value.Kind == "search").Select(ToCoverageFleetRun)).ToArray();
        traffic = traffic.Concat(retainedCoverage.Where(value => value.Kind == "traffic").Select(ToCoverageFleetRun)).ToArray();
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
        var asOf = asOfUtc.ToUniversalTime();
        await using var readSession = await client.OpenSessionAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var plans = await readSession.RunInTransactionAsync(async (transaction, token) =>
        {
            await ReadCurrentFleetSchemaVersionAsync(transaction, token).ConfigureAwait(false);
            var search = await ReadSearchFleetRunsAsync(transaction, token).ConfigureAwait(false);
            var traffic = await ReadTrafficFleetRunsAsync(transaction, token).ConfigureAwait(false);
            var performance = await ReadPerformanceFleetRunsAsync(transaction, token).ConfigureAwait(false);
            var snapshotPlans = new[]
            {
                CreateRetentionPlan("search", "search_observation_runs", "search_observations",
                    search, asOf.AddDays(-policy.SearchRunRetentionDays), asOf),
                CreateRetentionPlan("traffic", "traffic_observation_runs", "traffic_observations",
                    traffic, asOf.AddDays(-policy.TrafficRunRetentionDays), asOf),
                CreateRetentionPlan("performance", "performance_observation_runs", "performance_observations",
                    performance, asOf.AddDays(-policy.PerformanceRunRetentionDays), asOf)
            };
            foreach (var plan in snapshotPlans)
                plan.Result.CandidateObservationCount = await CountFleetObservationsAsync(transaction, plan, token).ConfigureAwait(false);
            return snapshotPlans;
        }, cancellationToken).ConfigureAwait(false);

        if (apply && plans.Any(value => value.Candidates.Length > 0))
        {
            await using var session = await client.OpenSessionAsync(_databasePath, cancellationToken).ConfigureAwait(false);
            await session.RunInTransactionAsync(async (transaction, token) =>
            {
                foreach (var plan in plans)
                {
                    foreach (var coverage in plan.CoverageSummaries)
                    {
                        await transaction.ExecuteNonQueryAsync(
                            """
                            INSERT INTO fleet_retained_coverage (
                                kind, provider, site_id, stream_key, configuration_hash,
                                from_date, through_date, source_collected_at_utc, retained_at_utc
                            ) VALUES (
                                @kind, @provider, @site_id, @stream_key, @configuration_hash,
                                @from_date, @through_date, @source_collected_at_utc, @retained_at_utc
                            )
                            ON CONFLICT(kind, provider, site_id, stream_key, configuration_hash, from_date, through_date)
                            DO UPDATE SET
                                source_collected_at_utc = MIN(source_collected_at_utc, excluded.source_collected_at_utc);
                            """,
                            RetainedCoverageParameters(coverage), token).ConfigureAwait(false);
                    }
                }
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

    private static async Task<int> ReadCurrentFleetSchemaVersionAsync(SQLiteAsyncSession session, CancellationToken cancellationToken)
    {
        var value = await session.ExecuteScalarAsync("PRAGMA user_version;", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var version = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        if (version != CurrentSchemaVersion)
            throw new InvalidOperationException($"Fleet operations require search database schema v{CurrentSchemaVersion}; found v{version}. Import current evidence to migrate the store before scheduling, reporting, or retention.");
        return version;
    }

    private static async Task<FleetRun[]> ReadSearchFleetRunsAsync(SQLiteAsyncSession session, CancellationToken cancellationToken)
    {
        var runs = await session.QueryAsListAsync(
            """
            SELECT run_id, provider, site_id, collected_at_utc, status,
                   configuration_hash,
                   json_extract(normalized_manifest_json, '$.collectionCoverage.mode'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.fromDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.throughDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.searchType'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.failureCategory'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.failedDate'),
                   source_kind
            FROM search_observation_runs;
            """,
            static record => new SearchFleetRunMetadata(
                record.GetString(0), record.GetString(1), record.GetString(2), ParseFleetTimestamp(record.GetString(3)), record.GetString(4),
                NullableString(record, 5), NullableString(record, 6), NullableDate(record, 7), NullableDate(record, 8),
                NullableString(record, 9), NullableString(record, 10), NullableDate(record, 11), record.GetString(12)),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var coverageDates = await session.QueryAsListAsync(
            """
            SELECT r.provider, r.site_id, r.run_id, dates.value
            FROM search_observation_runs r
            JOIN json_each(r.normalized_manifest_json, '$.collectionCoverage.completedDates') dates;
            """,
            static record => new FleetDatedScope(record.GetString(0), record.GetString(1), record.GetString(2),
                ParseFleetDate(record.GetString(3)), string.Empty), cancellationToken: cancellationToken).ConfigureAwait(false);
        var observationScopes = await session.QueryAsListAsync(
            """
            SELECT provider, site_id, run_id, observation_date,
                   COALESCE(NULLIF(LOWER(TRIM(search_type)), ''), 'web')
            FROM search_observations
            GROUP BY provider, site_id, run_id, observation_date, COALESCE(NULLIF(LOWER(TRIM(search_type)), ''), 'web');
            """,
            static record => new FleetDatedScope(record.GetString(0), record.GetString(1), record.GetString(2),
                ParseFleetDate(record.GetString(3)), record.GetString(4)), cancellationToken: cancellationToken).ConfigureAwait(false);
        var dimensionScopes = await session.QueryAsListAsync(
            """
            SELECT r.provider, r.site_id, r.run_id, LOWER(TRIM(scopes.value))
            FROM search_observation_runs r
            JOIN json_each(r.normalized_manifest_json, '$.collectionCoverage.dimensionScopes') scopes;
            """,
            static record => new FleetRunScope(record.GetString(0), record.GetString(1), record.GetString(2), record.GetString(3)),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var observationDimensionScopes = await session.QueryAsListAsync(
            """
            SELECT provider, site_id, run_id, observation_date,
                   CASE
                       WHEN page IS NOT NULL AND query IS NOT NULL THEN 'page-query'
                       WHEN page IS NOT NULL THEN 'page'
                       ELSE 'query'
                   END
            FROM search_observations
            GROUP BY provider, site_id, run_id, observation_date,
                     CASE
                         WHEN page IS NOT NULL AND query IS NOT NULL THEN 'page-query'
                         WHEN page IS NOT NULL THEN 'page'
                         ELSE 'query'
                     END;
            """,
            static record => new FleetDatedScope(record.GetString(0), record.GetString(1), record.GetString(2),
                ParseFleetDate(record.GetString(3)), record.GetString(4)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var coverageByRun = coverageDates.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var observationsByRun = observationScopes.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var dimensionScopesByRun = dimensionScopes.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var observationDimensionScopesByRun = observationDimensionScopes.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var result = new List<FleetRun>();
        foreach (var run in runs)
        {
            var identity = FleetRunIdentity(run.Provider, run.SiteId, run.RunId);
            if (run.FromDate.HasValue && run.ThroughDate.HasValue)
            {
                var runDimensionScopes = dimensionScopesByRun[identity]
                    .SelectMany(value => ExpandDimensionScope(value.Scope))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var explicitCoverageDates = coverageByRun[identity].Select(value => value.Date).ToArray();
                var explicitCoverageDateSet = explicitCoverageDates.ToHashSet();
                var explicitCoverageRanges = MergeDates(explicitCoverageDates);
                var inferredDimensionRows = observationDimensionScopesByRun[identity].ToArray();
                var snapshot = string.Equals(run.CoverageMode, "snapshot", StringComparison.Ordinal);
                var requiresObservedDimensions = snapshot && (run.Status != "complete" ||
                    string.Equals(run.SourceKind, "csv-import", StringComparison.Ordinal));
                var coversFleetCapability = SearchSnapshotCoversFleetCapability(run, runDimensionScopes) &&
                                            !requiresObservedDimensions &&
                                            !(string.Equals(run.CoverageMode, "snapshot", StringComparison.Ordinal) &&
                                              runDimensionScopes.Length == 0 && inferredDimensionRows.Length > 0);
                var observedCompleteDates = requiresObservedDimensions
                    ? inferredDimensionRows.Where(value => explicitCoverageDateSet.Contains(value.Date))
                        .GroupBy(value => value.Date)
                        .Where(group => SearchSnapshotCoversFleetCapability(run,
                            group.SelectMany(value => ExpandDimensionScope(value.Scope)).Distinct(StringComparer.Ordinal).ToArray()))
                        .Select(group => group.Key)
                    : Enumerable.Empty<DateOnly>();
                var explicitDates = coversFleetCapability
                    ? explicitCoverageDates
                    : observedCompleteDates;
                var dimensionCoverage = coversFleetCapability
                    ? Array.Empty<FleetDimensionCoverage>()
                    : !requiresObservedDimensions && runDimensionScopes.Length > 0
                        ? runDimensionScopes.Select(scope => new FleetDimensionCoverage(scope, explicitCoverageRanges)).ToArray()
                        : inferredDimensionRows
                            .Where(value => explicitCoverageDateSet.Contains(value.Date))
                            .SelectMany(value => ExpandDimensionScope(value.Scope).Select(scope => (scope, value.Date)))
                            .GroupBy(value => value.scope, StringComparer.Ordinal)
                            .OrderBy(value => value.Key, StringComparer.Ordinal)
                            .Select(group => new FleetDimensionCoverage(group.Key, MergeDates(group.Select(value => value.Date))))
                            .ToArray();
                if (!string.IsNullOrWhiteSpace(run.SearchType))
                {
                    var ranges = run.Status == "complete" && string.Equals(run.CoverageMode, "daily", StringComparison.Ordinal)
                        ? [new WebSearchFleetCompletedRange { FromDate = run.FromDate.Value, ThroughDate = run.ThroughDate.Value }]
                        : MergeDates(explicitDates);
                    result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                        run.SearchType.Trim().ToLowerInvariant(), ranges, run.ConfigurationHash, run.FailureCategory, run.FailureDate,
                        DimensionCoverage: dimensionCoverage));
                }
                else
                {
                    var scopes = observationsByRun[identity]
                        .GroupBy(value => value.Scope, StringComparer.Ordinal)
                        .ToArray();
                    if (scopes.Length == 0)
                    {
                        var ranges = run.Status == "complete" && string.Equals(run.CoverageMode, "daily", StringComparison.Ordinal)
                            ? [new WebSearchFleetCompletedRange { FromDate = run.FromDate.Value, ThroughDate = run.ThroughDate.Value }]
                            : MergeDates(explicitDates);
                        result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                            "web", ranges, run.ConfigurationHash, run.FailureCategory, run.FailureDate,
                            DimensionCoverage: dimensionCoverage));
                    }
                    foreach (var scope in scopes)
                    {
                        var dates = run.Status == "partial"
                            ? explicitDates
                            : scope.Select(value => value.Date);
                        if (run.Status != "partial" && scope.Key == "web")
                            dates = dates.Concat(explicitDates);
                        result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                            scope.Key, MergeDates(dates), run.ConfigurationHash, run.FailureCategory, run.FailureDate,
                            DimensionCoverage: dimensionCoverage));
                    }
                }
                continue;
            }

            var legacyScopes = observationsByRun[identity].GroupBy(value => value.Scope, StringComparer.Ordinal).ToArray();
            if (legacyScopes.Length == 0)
            {
                result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                    "web", Array.Empty<WebSearchFleetCompletedRange>(), run.ConfigurationHash, run.FailureCategory, run.FailureDate));
            }
            foreach (var scope in legacyScopes)
            {
                var ranges = run.Status == "complete"
                    ? MergeDates(scope.Select(value => value.Date))
                    : Array.Empty<WebSearchFleetCompletedRange>();
                result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                    scope.Key, ranges, run.ConfigurationHash, run.FailureCategory, run.FailureDate));
            }
        }
        return result.ToArray();
    }

    private static async Task<FleetRun[]> ReadTrafficFleetRunsAsync(SQLiteAsyncSession session, CancellationToken cancellationToken)
    {
        var runs = await session.QueryAsListAsync(
            """
            SELECT run_id, provider, site_id, collected_at_utc, status,
                   configuration_hash,
                   json_extract(normalized_manifest_json, '$.collectionCoverage.fromDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.throughDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.failureCategory'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.failedDate')
            FROM traffic_observation_runs;
            """,
            static record => new TrafficFleetRunMetadata(
                record.GetString(0), record.GetString(1), record.GetString(2), ParseFleetTimestamp(record.GetString(3)), record.GetString(4),
                NullableString(record, 5), NullableDate(record, 6), NullableDate(record, 7), NullableString(record, 8), NullableDate(record, 9)),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var coverageDates = await session.QueryAsListAsync(
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
            return new FleetRun("traffic", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status, "traffic", ranges,
                run.ConfigurationHash, run.FailureCategory, run.FailureDate);
        }).ToArray();
    }

    private static async Task<FleetRun[]> ReadPerformanceFleetRunsAsync(SQLiteAsyncSession session, CancellationToken cancellationToken)
    {
        var runs = await session.QueryAsListAsync(
            """
            SELECT run_id, provider, site_id, collected_at_utc, status,
                   measurement_kind, target_kind, target_url, form_factor, configuration_hash
            FROM performance_observation_runs;
            """,
            static record => new PerformanceFleetRunMetadata(
                record.GetString(0), record.GetString(1), record.GetString(2), ParseFleetTimestamp(record.GetString(3)),
                record.GetString(4), record.GetString(5), record.GetString(6), record.GetString(7), record.GetString(8),
                NullableString(record, 9)),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return runs.Select(run => new FleetRun(
                "performance", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                string.Join("\u001f", run.MeasurementKind, run.TargetKind, run.TargetUrl, run.FormFactor),
                Array.Empty<WebSearchFleetCompletedRange>(),
                run.ConfigurationHash,
                null,
                MeasurementKind: run.MeasurementKind))
            .ToArray();
    }

    private static async Task<FleetRetainedCoverage[]> ReadRetainedCoverageAsync(SQLiteAsyncSession session, CancellationToken cancellationToken)
    {
        var values = await session.QueryAsListAsync(
            """
            SELECT kind, provider, site_id, stream_key, NULLIF(configuration_hash, ''),
                   from_date, through_date, source_collected_at_utc, retained_at_utc
            FROM fleet_retained_coverage;
            """,
            MapFleetRetainedCoverage,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return values.ToArray();
    }

    private static FleetRun ToCoverageFleetRun(FleetRetainedCoverage value)
    {
        var ranges = new[] { new WebSearchFleetCompletedRange { FromDate = value.FromDate, ThroughDate = value.ThroughDate } };
        return new FleetRun(
            "coverage-summary",
            value.Provider,
            value.SiteId,
            $"coverage:{value.Kind}:{value.StreamKey}:{value.DimensionScope}:{value.FromDate:yyyy-MM-dd}:{value.ThroughDate:yyyy-MM-dd}",
            value.SourceCollectedAtUtc,
            "coverage-summary",
            value.StreamKey,
            value.DimensionScope is null ? ranges : Array.Empty<WebSearchFleetCompletedRange>(),
            value.ConfigurationHash,
            null,
            IsCoverageSummary: true,
            DimensionCoverage: value.DimensionScope is null
                ? null
                : [new FleetDimensionCoverage(value.DimensionScope, ranges)]);
    }

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildSearchStreams(IEnumerable<FleetRun> runs) =>
        runs.GroupBy(value => (value.Provider, value.SiteId, value.StreamKey, value.ConfigurationHash))
            .Select(group =>
            {
                var ranges = MergeRanges(group.SelectMany(value => value.CompletedRanges)
                    .Concat(CombineSearchDimensionCoverage(group)));
                return BuildStream(group, WebSearchProviderCapabilities.SearchAnalytics,
                    ranges.LastOrDefault()?.ThroughDate, ranges);
            });

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildTrafficStreams(IEnumerable<FleetRun> runs) =>
        BuildDailyStreams(runs, WebSearchProviderCapabilities.TrafficAnalytics);

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildDailyStreams(IEnumerable<FleetRun> runs, string capability) =>
        runs.GroupBy(value => (value.Provider, value.SiteId, value.StreamKey, value.ConfigurationHash))
            .Select(group =>
            {
                var ranges = MergeRanges(group.SelectMany(value => value.CompletedRanges));
                return BuildStream(group, capability, ranges.LastOrDefault()?.ThroughDate, ranges);
            });

    private static IEnumerable<WebSearchFleetEvidenceStream> BuildPerformanceStreams(IEnumerable<FleetRun> runs) =>
        runs.GroupBy(value => (value.Provider, value.SiteId, value.MeasurementKind, value.StreamKey, value.ConfigurationHash))
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
        var actualRuns = runs.Where(value => !value.IsCoverageSummary).ToArray();
        var latestActual = actualRuns.FirstOrDefault();
        var completed = actualRuns.Where(value => value.Status == "complete" ||
                                                  value.CompletedRanges.Length > 0 ||
                                                  value.DimensionCoverage.Any(coverage => coverage.CompletedRanges.Length > 0)).ToArray();
        var permanentFailures = actualRuns
            .Where(value => value.Status == "partial" &&
                            value.FailureDate.HasValue &&
                            PermanentFleetFailureCategories.Contains(value.FailureCategory ?? string.Empty))
            .GroupBy(value => value.FailureDate!.Value)
            .Select(group => group
                .OrderByDescending(value => value.CollectedAtUtc)
                .ThenByDescending(value => value.RunId, StringComparer.Ordinal)
                .First())
            .OrderBy(value => value.FailureDate)
            .Select(value => new WebSearchFleetFailurePartition
            {
                Date = value.FailureDate!.Value,
                Category = value.FailureCategory!
            })
            .ToArray();
        return new WebSearchFleetEvidenceStream
        {
            SiteId = latest.SiteId,
            ProviderId = latest.Provider,
            Capability = capability,
            ScopeKey = latest.StreamKey,
            ConfigurationHash = latest.ConfigurationHash,
            LatestCompleteDate = latestDate,
            CompletedRanges = ranges,
            LastCompleteAtUtc = completed.Length == 0 ? null : completed.Max(value => value.CollectedAtUtc),
            LastAttemptAtUtc = latestActual?.CollectedAtUtc,
            HasPartialEvidence = latestActual?.Status == "partial",
            LatestFailureCategory = latestActual?.Status == "partial" ? latestActual.FailureCategory : null,
            LatestFailureDate = latestActual?.Status == "partial" ? latestActual.FailureDate : null,
            PermanentFailures = permanentFailures,
            HasRetainedCoverage = runs.Any(value => value.IsCoverageSummary),
            RunCount = actualRuns.Length
        };
    }

    private static RetentionPlan CreateRetentionPlan(
        string kind,
        string runTable,
        string observationTable,
        FleetRun[] runs,
        DateTimeOffset cutoffUtc,
        DateTimeOffset retainedAtUtc)
    {
        var visibleRuns = runs.Where(value => value.CollectedAtUtc <= retainedAtUtc).ToArray();
        var preserved = visibleRuns
            .GroupBy(value => (value.Provider, value.SiteId, value.StreamKey, value.ConfigurationHash, value.RetentionScopeKey))
            .SelectMany(SelectRetentionPreservationRuns)
            .Select(FleetRunIdentity)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = runs
            .Where(value => value.CollectedAtUtc < cutoffUtc && !preserved.Contains(FleetRunIdentity(value)))
            .GroupBy(FleetRunIdentity, StringComparer.Ordinal)
            .Select(value => value.First())
            .OrderBy(value => value.CollectedAtUtc)
            .ToArray();
        var candidateIdentities = candidates.Select(FleetRunIdentity).ToHashSet(StringComparer.Ordinal);
        var coverageSummaries = runs
            .Where(value => candidateIdentities.Contains(FleetRunIdentity(value)))
            .SelectMany(value => value.CompletedRanges.Select(range => new FleetRetainedCoverage(
                    kind, value.Provider, value.SiteId, value.StreamKey, value.ConfigurationHash,
                    range.FromDate, range.ThroughDate, value.CollectedAtUtc, retainedAtUtc, null))
                .Concat(value.DimensionCoverage.SelectMany(coverage => coverage.CompletedRanges.Select(range =>
                    new FleetRetainedCoverage(
                        kind, value.Provider, value.SiteId, value.StreamKey, value.ConfigurationHash,
                        range.FromDate, range.ThroughDate, value.CollectedAtUtc, retainedAtUtc, coverage.Scope)))))
            .Distinct()
            .ToArray();
        return new RetentionPlan(runTable, observationTable, candidates, coverageSummaries, new WebSearchFleetRetentionKindResult
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

    private static IEnumerable<FleetRun> SelectRetentionPreservationRuns(IEnumerable<FleetRun> values)
    {
        var runs = values.ToArray();
        var primary = SelectRetentionPreservationRun(runs);
        yield return primary;
        var permanentFailures = runs
            .Where(value => value.Status == "partial" && PermanentFleetFailureCategories.Contains(value.FailureCategory ?? string.Empty))
            .GroupBy(value => value.FailureDate)
            .Select(group => group
                .OrderByDescending(value => value.CollectedAtUtc)
                .ThenByDescending(value => value.RunId, StringComparer.Ordinal)
                .First())
            .Where(failure => failure.FailureDate is DateOnly failureDate && !runs.Any(candidate =>
                candidate.CollectedAtUtc > failure.CollectedAtUtc &&
                candidate.CompletedRanges.Any(range => failureDate >= range.FromDate && failureDate <= range.ThroughDate)));
        foreach (var permanentFailure in permanentFailures)
        {
            if (FleetRunIdentity(permanentFailure) != FleetRunIdentity(primary))
                yield return permanentFailure;
        }
    }

    private static async Task<int> CountFleetObservationsAsync(SQLiteAsyncSession session, RetentionPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Candidates.Length == 0)
            return 0;
        var rows = await session.QueryAsListAsync(
            $"SELECT provider, site_id, run_id, COUNT(*) FROM {plan.ObservationTable} GROUP BY provider, site_id, run_id;",
            static record => (Identity: FleetRunIdentity(record.GetString(0), record.GetString(1), record.GetString(2)), Count: Convert.ToInt32(record.GetInt64(3))),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var counts = rows.ToDictionary(value => value.Identity, value => value.Count, StringComparer.Ordinal);
        return plan.Candidates.Sum(run => counts.GetValueOrDefault(FleetRunIdentity(run)));
    }

    private static string FleetRunIdentity(FleetRun run) => string.Join("\u001f", run.Provider, run.SiteId, run.RunId);

    private static string FleetRunIdentity(string provider, string siteId, string runId) =>
        string.Join("\u001f", provider, siteId, runId);

    private static string FleetScopeIdentity(FleetDatedScope value) => FleetRunIdentity(value.Provider, value.SiteId, value.RunId);

    private static string FleetScopeIdentity(FleetRunScope value) => FleetRunIdentity(value.Provider, value.SiteId, value.RunId);

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

    private static Dictionary<string, object?> RetainedCoverageParameters(FleetRetainedCoverage value) => new()
    {
        ["@kind"] = value.Kind,
        ["@provider"] = value.Provider,
        ["@site_id"] = value.SiteId,
        ["@stream_key"] = StoredFleetCoverageStreamKey(value),
        ["@configuration_hash"] = value.ConfigurationHash ?? string.Empty,
        ["@from_date"] = value.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["@through_date"] = value.ThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["@source_collected_at_utc"] = value.SourceCollectedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["@retained_at_utc"] = value.RetainedAtUtc.ToString("O", CultureInfo.InvariantCulture)
    };

    private static string StoredFleetCoverageStreamKey(FleetRetainedCoverage value) => value.DimensionScope is null
        ? value.StreamKey
        : $"{value.StreamKey}\u001fdimension:{value.DimensionScope}";

    private static FleetRetainedCoverage MapFleetRetainedCoverage(IDataRecord record)
    {
        const string dimensionMarker = "\u001fdimension:";
        var storedStreamKey = record.GetString(3);
        var markerIndex = storedStreamKey.LastIndexOf(dimensionMarker, StringComparison.Ordinal);
        var streamKey = markerIndex < 0 ? storedStreamKey : storedStreamKey[..markerIndex];
        var dimensionScope = markerIndex < 0 ? null : storedStreamKey[(markerIndex + dimensionMarker.Length)..];
        return new FleetRetainedCoverage(
            record.GetString(0), record.GetString(1), record.GetString(2), streamKey, NullableString(record, 4),
            ParseFleetDate(record.GetString(5)), ParseFleetDate(record.GetString(6)),
            ParseFleetTimestamp(record.GetString(7)), ParseFleetTimestamp(record.GetString(8)), dimensionScope);
    }

    private sealed record FleetRun(
        string Kind,
        string Provider,
        string SiteId,
        string RunId,
        DateTimeOffset CollectedAtUtc,
        string Status,
        string StreamKey,
        WebSearchFleetCompletedRange[] CompletedRanges,
        string? ConfigurationHash,
        string? FailureCategory,
        DateOnly? FailureDate = null,
        bool IsCoverageSummary = false,
        string? MeasurementKind = null,
        FleetDimensionCoverage[]? DimensionCoverage = null)
    {
        public FleetDimensionCoverage[] DimensionCoverage { get; } =
            DimensionCoverage ?? Array.Empty<FleetDimensionCoverage>();

        public string RetentionScopeKey => DimensionCoverage.Length == 0
            ? string.Empty
            : string.Join(",", DimensionCoverage.Select(value => value.Scope).OrderBy(value => value, StringComparer.Ordinal));
    }

    private sealed record FleetDimensionCoverage(string Scope, WebSearchFleetCompletedRange[] CompletedRanges);

    private sealed record FleetReadSet(
        int SchemaVersion,
        FleetRun[] Search,
        FleetRun[] Traffic,
        FleetRun[] Performance,
        FleetRetainedCoverage[] RetainedCoverage);

    private sealed record SearchFleetRunMetadata(
        string RunId,
        string Provider,
        string SiteId,
        DateTimeOffset CollectedAtUtc,
        string Status,
        string? ConfigurationHash,
        string? CoverageMode,
        DateOnly? FromDate,
        DateOnly? ThroughDate,
        string? SearchType,
        string? FailureCategory,
        DateOnly? FailureDate,
        string SourceKind);

    private sealed record TrafficFleetRunMetadata(
        string RunId,
        string Provider,
        string SiteId,
        DateTimeOffset CollectedAtUtc,
        string Status,
        string? ConfigurationHash,
        DateOnly? FromDate,
        DateOnly? ThroughDate,
        string? FailureCategory,
        DateOnly? FailureDate);

    private sealed record PerformanceFleetRunMetadata(
        string RunId,
        string Provider,
        string SiteId,
        DateTimeOffset CollectedAtUtc,
        string Status,
        string MeasurementKind,
        string TargetKind,
        string TargetUrl,
        string FormFactor,
        string? ConfigurationHash);

    private sealed record FleetDatedScope(string Provider, string SiteId, string RunId, DateOnly Date, string Scope);

    private sealed record FleetRunScope(string Provider, string SiteId, string RunId, string Scope);

    private sealed record FleetRetainedCoverage(
        string Kind,
        string Provider,
        string SiteId,
        string StreamKey,
        string? ConfigurationHash,
        DateOnly FromDate,
        DateOnly ThroughDate,
        DateTimeOffset SourceCollectedAtUtc,
        DateTimeOffset RetainedAtUtc,
        string? DimensionScope);

    private sealed record RetentionPlan(
        string RunTable,
        string ObservationTable,
        FleetRun[] Candidates,
        FleetRetainedCoverage[] CoverageSummaries,
        WebSearchFleetRetentionKindResult Result);
}
