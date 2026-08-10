using System.Data;
using System.Globalization;
using System.Text.Json;
using DBAClientX;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed partial class SqliteWebSearchObservationStore
{
    internal async Task<WebTrafficObservationImportResult> ImportTrafficAsync(
        WebTrafficObservationBatch normalizedBatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedBatch);
        EnsureDatabaseDirectory();

        await using var client = new SQLite();
        await EnsureSchemaAsync(client, cancellationToken).ConfigureAwait(false);
        await using var session = await client.OpenSessionAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var inserted = await session.RunInTransactionAsync(
            async (transaction, token) =>
            {
                var manifest = JsonSerializer.Serialize(normalizedBatch, WebCliJson.Options);
                var insertedRun = await transaction.ExecuteNonQueryAsync(
                    """
                    INSERT OR IGNORE INTO traffic_observation_runs (
                        run_id, provider, site_id, collected_at_utc, source_kind, status,
                        configuration_hash, evidence_reference, normalized_manifest_json
                    ) VALUES (
                        @run_id, @provider, @site_id, @collected_at_utc, @source_kind, @status,
                        @configuration_hash, @evidence_reference, @normalized_manifest_json
                    );
                    """,
                    TrafficRunParameters(normalizedBatch, manifest),
                    token).ConfigureAwait(false);
                if (insertedRun == 0)
                {
                    var existing = await transaction.QueryAsListAsync(
                        "SELECT normalized_manifest_json FROM traffic_observation_runs WHERE provider = @provider AND site_id = @site_id AND run_id = @run_id;",
                        static record => record.GetString(0),
                        new Dictionary<string, object?>
                        {
                            ["@provider"] = normalizedBatch.Provider,
                            ["@site_id"] = normalizedBatch.SiteId,
                            ["@run_id"] = normalizedBatch.RunId
                        },
                        cancellationToken: token).ConfigureAwait(false);
                    if (existing.Count == 0)
                        throw new InvalidOperationException($"Traffic collection time '{normalizedBatch.CollectedAtUtc:O}' is already assigned to another run for this provider and site.");
                    if (existing.Count != 1 || !string.Equals(existing[0], manifest, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Traffic run identifier '{normalizedBatch.RunId}' is already assigned to different normalized evidence.");
                }

                var count = 0;
                foreach (var observation in normalizedBatch.Observations)
                {
                    count += await transaction.ExecuteNonQueryAsync(
                        """
                        INSERT OR IGNORE INTO traffic_observations (
                            observation_key, run_id, provider, site_id, observation_date, host, path,
                            requests, visits, edge_response_bytes, sample_interval, evidence_reference
                        ) VALUES (
                            @observation_key, @run_id, @provider, @site_id, @observation_date, @host, @path,
                            @requests, @visits, @edge_response_bytes, @sample_interval, @evidence_reference
                        );
                        """,
                        TrafficObservationParameters(normalizedBatch.RunId!, observation),
                        token).ConfigureAwait(false);
                }
                return count;
            },
            cancellationToken).ConfigureAwait(false);

        return new WebTrafficObservationImportResult
        {
            RunId = normalizedBatch.RunId!,
            Provider = normalizedBatch.Provider,
            SiteId = normalizedBatch.SiteId,
            InputCount = normalizedBatch.Observations.Length,
            InsertedCount = inserted,
            DuplicateCount = normalizedBatch.Observations.Length - inserted,
            DatabaseSchemaVersion = CurrentSchemaVersion
        };
    }

    internal async Task<IReadOnlyList<WebTrafficObservation>> QueryTrafficAsync(
        WebTrafficObservationQuery query,
        CancellationToken cancellationToken = default) =>
        (await QueryTrafficEvidenceAsync(query, cancellationToken).ConfigureAwait(false)).Observations;

    internal async Task<WebTrafficObservationQueryResult> QueryTrafficEvidenceAsync(
        WebTrafficObservationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SiteId))
            throw new ArgumentException("Traffic query requires a site identifier.", nameof(query));
        if (query.FromDate.HasValue && query.ThroughDate.HasValue && query.FromDate > query.ThroughDate)
            throw new ArgumentException("Traffic from date cannot be after through date.", nameof(query));
        if (query.FromDate.HasValue && query.ThroughDate.HasValue && string.IsNullOrWhiteSpace(query.Provider))
            throw new ArgumentException("A bounded traffic completeness query requires a provider identifier.", nameof(query));
        if (!File.Exists(_databasePath))
            return new WebTrafficObservationQueryResult { StoreExists = false };

        await using var client = new SQLite();
        await EnsureSchemaAsync(client, cancellationToken).ConfigureAwait(false);
        var runClauses = new List<string> { "site_id = @site_id" };
        var parameters = new Dictionary<string, object?> { ["@site_id"] = query.SiteId.Trim().ToLowerInvariant() };
        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            runClauses.Add("provider = @provider");
            parameters["@provider"] = query.Provider.Trim().ToLowerInvariant();
        }
        var coverageDateClauses = new List<string>();
        var observationDateClauses = new List<string>();
        var failedDateClauses = new List<string>();
        if (query.FromDate.HasValue)
        {
            parameters["@from_date"] = FormatDate(query.FromDate.Value);
            coverageDateClauses.Add("dates.value >= @from_date");
            observationDateClauses.Add("observations.observation_date >= @from_date");
            failedDateClauses.Add("json_extract(traffic_observation_runs.normalized_manifest_json, '$.collectionCoverage.failedDate') >= @from_date");
        }
        if (query.ThroughDate.HasValue)
        {
            parameters["@through_date"] = FormatDate(query.ThroughDate.Value);
            coverageDateClauses.Add("dates.value <= @through_date");
            observationDateClauses.Add("observations.observation_date <= @through_date");
            failedDateClauses.Add("json_extract(traffic_observation_runs.normalized_manifest_json, '$.collectionCoverage.failedDate') <= @through_date");
        }
        if (coverageDateClauses.Count > 0)
        {
            runClauses.Add($"""
                (
                    EXISTS (
                        SELECT 1
                        FROM json_each(traffic_observation_runs.normalized_manifest_json, '$.collectionCoverage.completedDates') AS dates
                        WHERE {string.Join(" AND ", coverageDateClauses)}
                    ) OR (
                        json_extract(traffic_observation_runs.normalized_manifest_json, '$.collectionCoverage.failedDate') IS NOT NULL
                        AND {string.Join(" AND ", failedDateClauses)}
                    ) OR EXISTS (
                        SELECT 1
                        FROM traffic_observations AS observations
                        WHERE observations.provider = traffic_observation_runs.provider
                          AND observations.site_id = traffic_observation_runs.site_id
                          AND observations.run_id = traffic_observation_runs.run_id
                          AND {string.Join(" AND ", observationDateClauses)}
                    )
                )
                """);
        }

        var manifests = await client.QueryAsListAsync(
            _databasePath,
            $"SELECT normalized_manifest_json FROM traffic_observation_runs WHERE {string.Join(" AND ", runClauses)};",
            static record => record.GetString(0),
            parameters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var batches = manifests
            .Select(manifest => JsonSerializer.Deserialize<WebTrafficObservationBatch>(manifest, WebCliJson.Options)
                ?? throw new InvalidOperationException("Stored traffic run manifest is empty."))
            .Select(WebTrafficObservationNormalizer.Normalize)
            .ToArray();
        var selectedByDate = batches
            .SelectMany(EvidencePartitions)
            .Where(value => (!query.FromDate.HasValue || value.Date >= query.FromDate.Value) &&
                            (!query.ThroughDate.HasValue || value.Date <= query.ThroughDate.Value))
            .GroupBy(value => (value.Batch.Provider, value.Batch.SiteId, value.Date))
            .Select(group => group
                .OrderBy(value => value.IsComplete ? 0 : 1)
                .ThenBy(value => value.HasUsableObservations ? 0 : 1)
                .ThenByDescending(value => value.Batch.CollectedAtUtc)
                .ThenByDescending(value => value.Batch.RunId, StringComparer.Ordinal)
                .First())
            .ToDictionary(value => (value.Batch.Provider, value.Batch.SiteId, value.Date));
        var selectedRuns = selectedByDate.Values
            .GroupBy(value => (value.Batch.Provider, value.Batch.SiteId, value.Batch.RunId))
            .Select(group =>
            {
                var batch = group.First().Batch;
                var selectedDates = group.Select(value => value.Date).Distinct().OrderBy(date => date).ToArray();
                var selectedDateSet = selectedDates.ToHashSet();
                return new WebTrafficObservationRunEvidence
                {
                    RunId = batch.RunId!,
                    Provider = batch.Provider,
                    SiteId = batch.SiteId,
                    CollectedAtUtc = batch.CollectedAtUtc,
                    Status = group.All(value => value.IsComplete) ? "complete" : "partial",
                    ZeroDataConfirmed = batch.ZeroDataConfirmed ||
                                        (group.All(value => value.IsComplete) &&
                                         !batch.Observations.Any(observation => selectedDateSet.Contains(observation.Date))),
                    CollectionCoverage = batch.CollectionCoverage,
                    SelectedDates = selectedDates
                };
            })
            .OrderBy(value => value.Provider, StringComparer.Ordinal)
            .ThenBy(value => value.SiteId, StringComparer.Ordinal)
            .ThenBy(value => value.SelectedDates.FirstOrDefault())
            .ThenBy(value => value.RunId, StringComparer.Ordinal)
            .ToArray();
        var requestedDates = query.FromDate.HasValue && query.ThroughDate.HasValue
            ? Enumerable.Range(0, query.ThroughDate.Value.DayNumber - query.FromDate.Value.DayNumber + 1)
                .Select(query.FromDate.Value.AddDays)
                .ToArray()
            : Array.Empty<DateOnly>();
        var coveredDates = selectedByDate.Keys.Select(key => key.Date).ToHashSet();
        var missingDates = requestedDates.Where(date => !coveredDates.Contains(date)).ToArray();
        if (selectedByDate.Count == 0)
        {
            return new WebTrafficObservationQueryResult
            {
                StoreExists = true,
                HasEvidence = false,
                HasCoverageGaps = missingDates.Length > 0,
                MissingDates = missingDates
            };
        }

        var observationClauses = new List<string> { "site_id = @site_id" };
        if (parameters.ContainsKey("@provider"))
            observationClauses.Add("provider = @provider");
        if (query.FromDate.HasValue)
        {
            observationClauses.Add("observation_date >= @from_date");
        }
        if (query.ThroughDate.HasValue)
        {
            observationClauses.Add("observation_date <= @through_date");
        }

        var sql = $"""
            SELECT observation_key, run_id, provider, site_id, observation_date, host, path,
                   requests, visits, edge_response_bytes, sample_interval, evidence_reference
            FROM traffic_observations
            WHERE {string.Join(" AND ", observationClauses)}
            ORDER BY observation_date, host, path;
            """;
        var stored = await client.QueryAsListAsync(
            _databasePath,
            sql,
            MapStoredTrafficObservation,
            parameters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var observations = stored
            .Where(value => selectedByDate.TryGetValue(
                                (value.Observation.Provider, value.Observation.SiteId, value.Observation.Date),
                                out var selected) &&
                            string.Equals(selected.Batch.RunId, value.RunId, StringComparison.Ordinal))
            .Select(value => value.Observation)
            .OrderBy(value => value.Date)
            .ThenBy(value => value.Host, StringComparer.Ordinal)
            .ThenBy(value => value.Path, StringComparer.Ordinal)
            .ToArray();
        return new WebTrafficObservationQueryResult
        {
            StoreExists = true,
            HasEvidence = true,
            HasPartialEvidence = selectedRuns.Any(value => value.Status == "partial"),
            HasCoverageGaps = missingDates.Length > 0,
            MissingDates = missingDates,
            HasExplicitZeroEvidence = selectedRuns.Any(value => value.ZeroDataConfirmed),
            SelectedRuns = selectedRuns,
            Observations = observations
        };
    }

    private static IEnumerable<SelectedTrafficDate> EvidencePartitions(WebTrafficObservationBatch batch)
    {
        foreach (var date in batch.CollectionCoverage.CompletedDates.Distinct())
            yield return new SelectedTrafficDate(batch, date, true,
                batch.Observations.Any(observation => observation.Date == date));
        if (batch.CollectionCoverage.FailedDate is DateOnly failedDate)
            yield return new SelectedTrafficDate(batch, failedDate, false,
                batch.Observations.Any(observation => observation.Date == failedDate));
    }

    private static Dictionary<string, object?> TrafficRunParameters(WebTrafficObservationBatch batch, string manifest) => new()
    {
        ["@run_id"] = batch.RunId,
        ["@provider"] = batch.Provider,
        ["@site_id"] = batch.SiteId,
        ["@collected_at_utc"] = batch.CollectedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["@source_kind"] = batch.SourceKind,
        ["@status"] = batch.Status,
        ["@configuration_hash"] = batch.ConfigurationHash,
        ["@evidence_reference"] = batch.EvidenceReference,
        ["@normalized_manifest_json"] = manifest
    };

    private static Dictionary<string, object?> TrafficObservationParameters(string runId, WebTrafficObservation value) => new()
    {
        ["@observation_key"] = value.ObservationKey,
        ["@run_id"] = runId,
        ["@provider"] = value.Provider,
        ["@site_id"] = value.SiteId,
        ["@observation_date"] = FormatDate(value.Date),
        ["@host"] = value.Host,
        ["@path"] = value.Path,
        ["@requests"] = value.Requests,
        ["@visits"] = value.Visits,
        ["@edge_response_bytes"] = value.EdgeResponseBytes,
        ["@sample_interval"] = value.SampleInterval,
        ["@evidence_reference"] = value.EvidenceReference
    };

    private static StoredTrafficObservation MapStoredTrafficObservation(IDataRecord record) => new(
        record.GetString(1),
        new WebTrafficObservation
        {
            ObservationKey = record.GetString(0),
            Provider = record.GetString(2),
            SiteId = record.GetString(3),
            Date = DateOnly.ParseExact(record.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Host = record.GetString(5),
            Path = record.GetString(6),
            Requests = record.GetInt64(7),
            Visits = record.GetInt64(8),
            EdgeResponseBytes = record.GetInt64(9),
            SampleInterval = record.GetDouble(10),
            EvidenceReference = GetNullableString(record, 11)
        });

    private sealed record SelectedTrafficDate(
        WebTrafficObservationBatch Batch,
        DateOnly Date,
        bool IsComplete,
        bool HasUsableObservations);
    private sealed record StoredTrafficObservation(string RunId, WebTrafficObservation Observation);
}
