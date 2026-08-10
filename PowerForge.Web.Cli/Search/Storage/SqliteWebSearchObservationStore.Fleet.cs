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
        var version = await ReadCurrentFleetSchemaVersionAsync(client, cancellationToken).ConfigureAwait(false);
        var search = await ReadSearchFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var traffic = await ReadTrafficFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var performance = await ReadPerformanceFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var retainedCoverage = await ReadRetainedCoverageAsync(client, cancellationToken).ConfigureAwait(false);
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
        await ReadCurrentFleetSchemaVersionAsync(client, cancellationToken).ConfigureAwait(false);
        var search = await ReadSearchFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var traffic = await ReadTrafficFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var performance = await ReadPerformanceFleetRunsAsync(client, cancellationToken).ConfigureAwait(false);
        var asOf = asOfUtc.ToUniversalTime();
        var plans = new[]
        {
            CreateRetentionPlan("search", "search_observation_runs", "search_observations",
                search, asOf.AddDays(-policy.SearchRunRetentionDays), asOf),
            CreateRetentionPlan("traffic", "traffic_observation_runs", "traffic_observations",
                traffic, asOf.AddDays(-policy.TrafficRunRetentionDays), asOf),
            CreateRetentionPlan("performance", "performance_observation_runs", "performance_observations",
                performance, asOf.AddDays(-policy.PerformanceRunRetentionDays), asOf)
        };

        foreach (var plan in plans)
            plan.Result.CandidateObservationCount = await CountFleetObservationsAsync(client, plan, cancellationToken).ConfigureAwait(false);

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
                   configuration_hash,
                   json_extract(normalized_manifest_json, '$.collectionCoverage.mode'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.fromDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.throughDate'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.searchType'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.failureCategory'),
                   json_extract(normalized_manifest_json, '$.collectionCoverage.failedDate')
            FROM search_observation_runs;
            """,
            static record => new SearchFleetRunMetadata(
                record.GetString(0), record.GetString(1), record.GetString(2), ParseFleetTimestamp(record.GetString(3)), record.GetString(4),
                NullableString(record, 5), NullableString(record, 6), NullableDate(record, 7), NullableDate(record, 8),
                NullableString(record, 9), NullableString(record, 10), NullableDate(record, 11)),
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
        var dimensionScopes = await client.QueryAsListAsync(_databasePath,
            """
            SELECT r.provider, r.site_id, r.run_id, LOWER(TRIM(scopes.value))
            FROM search_observation_runs r
            JOIN json_each(r.normalized_manifest_json, '$.collectionCoverage.dimensionScopes') scopes;
            """,
            static record => new FleetRunScope(record.GetString(0), record.GetString(1), record.GetString(2), record.GetString(3)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var coverageByRun = coverageDates.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var observationsByRun = observationScopes.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var dimensionScopesByRun = dimensionScopes.ToLookup(FleetScopeIdentity, StringComparer.Ordinal);
        var result = new List<FleetRun>();
        foreach (var run in runs)
        {
            var identity = FleetRunIdentity(run.Provider, run.SiteId, run.RunId);
            if (run.FromDate.HasValue && run.ThroughDate.HasValue)
            {
                var runDimensionScopes = dimensionScopesByRun[identity].Select(value => value.Scope).ToArray();
                var explicitCoverageDates = coverageByRun[identity].Select(value => value.Date).ToArray();
                var explicitCoverageRanges = MergeDates(explicitCoverageDates);
                var coversFleetCapability = SearchSnapshotCoversFleetCapability(run, runDimensionScopes);
                var explicitDates = coversFleetCapability
                    ? explicitCoverageDates
                    : Enumerable.Empty<DateOnly>();
                var dimensionRanges = coversFleetCapability
                    ? Array.Empty<WebSearchFleetCompletedRange>()
                    : explicitCoverageRanges;
                if (!string.IsNullOrWhiteSpace(run.SearchType))
                {
                    var ranges = run.Status == "complete" && string.Equals(run.CoverageMode, "daily", StringComparison.Ordinal)
                        ? [new WebSearchFleetCompletedRange { FromDate = run.FromDate.Value, ThroughDate = run.ThroughDate.Value }]
                        : MergeDates(explicitDates);
                    result.Add(new FleetRun("search", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status,
                        run.SearchType.Trim().ToLowerInvariant(), ranges, run.ConfigurationHash, run.FailureCategory, run.FailureDate,
                        DimensionScopes: runDimensionScopes, DimensionCompletedRanges: dimensionRanges));
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
                            DimensionScopes: runDimensionScopes, DimensionCompletedRanges: dimensionRanges));
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
                            DimensionScopes: runDimensionScopes, DimensionCompletedRanges: dimensionRanges));
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

    private async Task<FleetRun[]> ReadTrafficFleetRunsAsync(SQLite client, CancellationToken cancellationToken)
    {
        var runs = await client.QueryAsListAsync(_databasePath,
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
            return new FleetRun("traffic", run.Provider, run.SiteId, run.RunId, run.CollectedAtUtc, run.Status, "traffic", ranges,
                run.ConfigurationHash, run.FailureCategory, run.FailureDate);
        }).ToArray();
    }

    private async Task<FleetRun[]> ReadPerformanceFleetRunsAsync(SQLite client, CancellationToken cancellationToken)
    {
        var runs = await client.QueryAsListAsync(_databasePath,
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

    private async Task<FleetRetainedCoverage[]> ReadRetainedCoverageAsync(SQLite client, CancellationToken cancellationToken)
    {
        var values = await client.QueryAsListAsync(
            _databasePath,
            """
            SELECT kind, provider, site_id, stream_key, NULLIF(configuration_hash, ''),
                   from_date, through_date, source_collected_at_utc, retained_at_utc
            FROM fleet_retained_coverage;
            """,
            static record => new FleetRetainedCoverage(
                record.GetString(0), record.GetString(1), record.GetString(2), record.GetString(3), NullableString(record, 4),
                ParseFleetDate(record.GetString(5)), ParseFleetDate(record.GetString(6)),
                ParseFleetTimestamp(record.GetString(7)), ParseFleetTimestamp(record.GetString(8))),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return values.ToArray();
    }

    private static FleetRun ToCoverageFleetRun(FleetRetainedCoverage value) => new(
        "coverage-summary",
        value.Provider,
        value.SiteId,
        $"coverage:{value.Kind}:{value.StreamKey}:{value.FromDate:yyyy-MM-dd}:{value.ThroughDate:yyyy-MM-dd}",
        value.SourceCollectedAtUtc,
        "coverage-summary",
        value.StreamKey,
        [new WebSearchFleetCompletedRange { FromDate = value.FromDate, ThroughDate = value.ThroughDate }],
        value.ConfigurationHash,
        null,
        IsCoverageSummary: true);

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
                                                  value.DimensionCompletedRanges.Length > 0).ToArray();
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
                range.FromDate, range.ThroughDate, value.CollectedAtUtc, retainedAtUtc)))
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
                .First());
        foreach (var permanentFailure in permanentFailures)
        {
            if (FleetRunIdentity(permanentFailure) != FleetRunIdentity(primary))
                yield return permanentFailure;
        }
    }

    private async Task<int> CountFleetObservationsAsync(SQLite client, RetentionPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Candidates.Length == 0)
            return 0;
        var rows = await client.QueryAsListAsync(
            _databasePath,
            $"SELECT provider, site_id, run_id, COUNT(*) FROM {plan.ObservationTable} GROUP BY provider, site_id, run_id;",
            static record => (Identity: FleetRunIdentity(record.GetString(0), record.GetString(1), record.GetString(2)), Count: Convert.ToInt32(record.GetInt64(3))),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var counts = rows.ToDictionary(value => value.Identity, value => value.Count, StringComparer.Ordinal);
        return plan.Candidates.Sum(run => counts.GetValueOrDefault(FleetRunIdentity(run)));
    }

    private static WebSearchFleetCompletedRange[] MergeDates(IEnumerable<DateOnly> dates) => MergeRanges(
        dates.Distinct().Select(value => new WebSearchFleetCompletedRange { FromDate = value, ThroughDate = value }));

    private static WebSearchFleetCompletedRange[] CombineSearchDimensionCoverage(IEnumerable<FleetRun> values)
    {
        var runs = values.ToArray();
        var pageRanges = MergeRanges(runs
            .Where(value => value.DimensionScopes.Contains("page", StringComparer.Ordinal))
            .SelectMany(value => value.DimensionCompletedRanges));
        var queryRanges = MergeRanges(runs
            .Where(value => value.DimensionScopes.Contains("query", StringComparer.Ordinal))
            .SelectMany(value => value.DimensionCompletedRanges));
        var combined = new List<WebSearchFleetCompletedRange>();
        var pageIndex = 0;
        var queryIndex = 0;
        while (pageIndex < pageRanges.Length && queryIndex < queryRanges.Length)
        {
            var page = pageRanges[pageIndex];
            var query = queryRanges[queryIndex];
            var fromDate = page.FromDate > query.FromDate ? page.FromDate : query.FromDate;
            var throughDate = page.ThroughDate < query.ThroughDate ? page.ThroughDate : query.ThroughDate;
            if (fromDate <= throughDate)
                combined.Add(new WebSearchFleetCompletedRange { FromDate = fromDate, ThroughDate = throughDate });
            if (page.ThroughDate < query.ThroughDate)
                pageIndex++;
            else
                queryIndex++;
        }
        return MergeRanges(combined);
    }

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

    private static string FleetScopeIdentity(FleetRunScope value) => FleetRunIdentity(value.Provider, value.SiteId, value.RunId);

    private static bool SearchSnapshotCoversFleetCapability(SearchFleetRunMetadata run, IReadOnlyCollection<string> scopes)
    {
        if (!string.Equals(run.CoverageMode, "snapshot", StringComparison.Ordinal))
            return true;
        var dimensionScopes = scopes.ToHashSet(StringComparer.Ordinal);
        return dimensionScopes.Count == 0 || dimensionScopes.Contains("page") && dimensionScopes.Contains("query");
    }

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
        ["@stream_key"] = value.StreamKey,
        ["@configuration_hash"] = value.ConfigurationHash ?? string.Empty,
        ["@from_date"] = value.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["@through_date"] = value.ThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["@source_collected_at_utc"] = value.SourceCollectedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["@retained_at_utc"] = value.RetainedAtUtc.ToString("O", CultureInfo.InvariantCulture)
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
        string? ConfigurationHash,
        string? FailureCategory,
        DateOnly? FailureDate = null,
        bool IsCoverageSummary = false,
        string? MeasurementKind = null,
        string[]? DimensionScopes = null,
        WebSearchFleetCompletedRange[]? DimensionCompletedRanges = null)
    {
        public string[] DimensionScopes { get; } = DimensionScopes ?? Array.Empty<string>();

        public WebSearchFleetCompletedRange[] DimensionCompletedRanges { get; } =
            DimensionCompletedRanges ?? Array.Empty<WebSearchFleetCompletedRange>();

        public string RetentionScopeKey => DimensionCompletedRanges.Length == 0
            ? string.Empty
            : string.Join(",", DimensionScopes);
    }

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
        DateOnly? FailureDate);

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
        DateTimeOffset RetainedAtUtc);

    private sealed record RetentionPlan(
        string RunTable,
        string ObservationTable,
        FleetRun[] Candidates,
        FleetRetainedCoverage[] CoverageSummaries,
        WebSearchFleetRetentionKindResult Result);
}
