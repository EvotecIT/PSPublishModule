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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SiteId))
            throw new ArgumentException("Traffic query requires a site identifier.", nameof(query));
        if (query.FromDate.HasValue && query.ThroughDate.HasValue && query.FromDate > query.ThroughDate)
            throw new ArgumentException("Traffic from date cannot be after through date.", nameof(query));
        if (!File.Exists(_databasePath))
            return Array.Empty<WebTrafficObservation>();

        await using var client = new SQLite();
        await EnsureSchemaAsync(client, cancellationToken).ConfigureAwait(false);
        var clauses = new List<string> { "observations.site_id = @site_id" };
        var parameters = new Dictionary<string, object?> { ["@site_id"] = query.SiteId.Trim().ToLowerInvariant() };
        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            clauses.Add("observations.provider = @provider");
            parameters["@provider"] = query.Provider.Trim().ToLowerInvariant();
        }
        if (query.FromDate.HasValue)
        {
            clauses.Add("observations.observation_date >= @from_date");
            parameters["@from_date"] = FormatDate(query.FromDate.Value);
        }
        if (query.ThroughDate.HasValue)
        {
            clauses.Add("observations.observation_date <= @through_date");
            parameters["@through_date"] = FormatDate(query.ThroughDate.Value);
        }

        var sql = $"""
            WITH ranked AS (
                SELECT observations.observation_key, observations.provider, observations.site_id,
                       observations.observation_date, observations.host, observations.path,
                       observations.requests, observations.visits, observations.edge_response_bytes,
                       observations.sample_interval, observations.evidence_reference,
                       ROW_NUMBER() OVER (
                           PARTITION BY observations.provider, observations.site_id,
                                        observations.observation_date, observations.host, observations.path
                           ORDER BY CASE WHEN runs.status = 'complete' THEN 0 ELSE 1 END,
                                    runs.collected_at_utc DESC, observations.run_id DESC
                       ) AS revision_rank
                FROM traffic_observations AS observations
                INNER JOIN traffic_observation_runs AS runs
                    ON runs.provider = observations.provider
                   AND runs.site_id = observations.site_id
                   AND runs.run_id = observations.run_id
                WHERE {string.Join(" AND ", clauses)}
            )
            SELECT observation_key, provider, site_id, observation_date, host, path,
                   requests, visits, edge_response_bytes, sample_interval, evidence_reference
            FROM ranked
            WHERE revision_rank = 1
            ORDER BY observation_date, host, path;
            """;
        return await client.QueryAsListAsync(
            _databasePath,
            sql,
            MapTrafficObservation,
            parameters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

    private static WebTrafficObservation MapTrafficObservation(IDataRecord record) => new()
    {
        ObservationKey = record.GetString(0),
        Provider = record.GetString(1),
        SiteId = record.GetString(2),
        Date = DateOnly.ParseExact(record.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Host = record.GetString(4),
        Path = record.GetString(5),
        Requests = record.GetInt64(6),
        Visits = record.GetInt64(7),
        EdgeResponseBytes = record.GetInt64(8),
        SampleInterval = record.GetDouble(9),
        EvidenceReference = GetNullableString(record, 10)
    };
}
