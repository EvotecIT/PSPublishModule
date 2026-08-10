using System.Data;
using System.Globalization;
using System.Text.Json;
using DBAClientX;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed partial class SqliteWebSearchObservationStore
{
    internal const int CurrentSchemaVersion = 7;

    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS search_observation_runs (
            run_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            site_id TEXT NOT NULL,
            collected_at_utc TEXT NOT NULL,
            source_kind TEXT NOT NULL,
            status TEXT NOT NULL,
            configuration_hash TEXT NULL,
            evidence_reference TEXT NULL,
            normalized_manifest_json TEXT NOT NULL,
            PRIMARY KEY (provider, site_id, run_id)
        );
        CREATE TABLE IF NOT EXISTS search_observations (
            observation_key TEXT NOT NULL PRIMARY KEY,
            run_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            site_id TEXT NOT NULL,
            observation_date TEXT NOT NULL,
            page TEXT NULL,
            query TEXT NULL,
            country TEXT NULL,
            device TEXT NULL,
            search_type TEXT NULL,
            clicks INTEGER NOT NULL,
            impressions INTEGER NOT NULL,
            click_through_rate REAL NULL,
            average_position REAL NULL,
            evidence_reference TEXT NULL,
            FOREIGN KEY (provider, site_id, run_id)
                REFERENCES search_observation_runs(provider, site_id, run_id)
        );
        """;

    private const string CreateIndexesSql = """
        CREATE INDEX IF NOT EXISTS ix_search_observations_site_date
            ON search_observations(site_id, observation_date);
        CREATE INDEX IF NOT EXISTS ix_search_observations_provider_site_date
            ON search_observations(provider, site_id, observation_date);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_search_observation_runs_provider_site_collected
            ON search_observation_runs(provider, site_id, collected_at_utc);
        """;

    private const string CreateTrafficTablesSql = """
        CREATE TABLE IF NOT EXISTS traffic_observation_runs (
            run_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            site_id TEXT NOT NULL,
            collected_at_utc TEXT NOT NULL,
            source_kind TEXT NOT NULL,
            status TEXT NOT NULL,
            configuration_hash TEXT NULL,
            evidence_reference TEXT NULL,
            normalized_manifest_json TEXT NOT NULL,
            PRIMARY KEY (provider, site_id, run_id)
        );
        CREATE TABLE IF NOT EXISTS traffic_observations (
            observation_key TEXT NOT NULL PRIMARY KEY,
            run_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            site_id TEXT NOT NULL,
            observation_date TEXT NOT NULL,
            host TEXT NOT NULL,
            path TEXT NOT NULL,
            requests INTEGER NOT NULL,
            visits INTEGER NOT NULL,
            edge_response_bytes INTEGER NOT NULL,
            sample_interval REAL NOT NULL,
            evidence_reference TEXT NULL,
            FOREIGN KEY (provider, site_id, run_id)
                REFERENCES traffic_observation_runs(provider, site_id, run_id)
        );
        """;

    private const string CreateTrafficIndexesSql = """
        CREATE INDEX IF NOT EXISTS ix_traffic_observations_site_date
            ON traffic_observations(site_id, observation_date);
        CREATE INDEX IF NOT EXISTS ix_traffic_observations_provider_site_date
            ON traffic_observations(provider, site_id, observation_date);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_traffic_observation_runs_provider_site_collected
            ON traffic_observation_runs(provider, site_id, collected_at_utc);
        """;

    private const string CreatePerformanceTablesSql = """
        CREATE TABLE IF NOT EXISTS performance_observation_runs (
            run_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            site_id TEXT NOT NULL,
            collected_at_utc TEXT NOT NULL,
            source_kind TEXT NOT NULL,
            status TEXT NOT NULL,
            measurement_kind TEXT NOT NULL,
            target_kind TEXT NOT NULL,
            target_url TEXT NOT NULL,
            form_factor TEXT NOT NULL,
            tool_version TEXT NULL,
            zero_data_confirmed INTEGER NOT NULL,
            configuration_hash TEXT NULL,
            evidence_reference TEXT NULL,
            normalized_manifest_json TEXT NOT NULL,
            PRIMARY KEY (provider, site_id, run_id)
        );
        CREATE TABLE IF NOT EXISTS performance_observations (
            observation_key TEXT NOT NULL PRIMARY KEY,
            run_id TEXT NOT NULL,
            provider TEXT NOT NULL,
            site_id TEXT NOT NULL,
            metric TEXT NOT NULL,
            metric_value REAL NOT NULL,
            unit TEXT NOT NULL,
            percentile INTEGER NULL,
            period_start_date TEXT NULL,
            period_end_date TEXT NULL,
            histogram_json TEXT NOT NULL,
            FOREIGN KEY (provider, site_id, run_id)
                REFERENCES performance_observation_runs(provider, site_id, run_id)
        );
        """;

    private const string CreatePerformanceIndexesSql = """
        CREATE INDEX IF NOT EXISTS ix_performance_observation_runs_site_kind
            ON performance_observation_runs(site_id, measurement_kind);
        CREATE INDEX IF NOT EXISTS ix_performance_observation_runs_target
            ON performance_observation_runs(provider, site_id, measurement_kind, target_url, form_factor);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_performance_observation_runs_provider_site_collected
            ON performance_observation_runs(provider, site_id, measurement_kind, target_kind, target_url, form_factor, collected_at_utc);
        """;

    private const string CreateFleetTablesSql = """
        CREATE TABLE IF NOT EXISTS fleet_retained_coverage (
            kind TEXT NOT NULL,
            provider TEXT NOT NULL,
            site_id TEXT NOT NULL,
            stream_key TEXT NOT NULL,
            configuration_hash TEXT NOT NULL,
            from_date TEXT NOT NULL,
            through_date TEXT NOT NULL,
            source_collected_at_utc TEXT NOT NULL,
            retained_at_utc TEXT NOT NULL,
            PRIMARY KEY (kind, provider, site_id, stream_key, configuration_hash, from_date, through_date)
        );
        """;

    private const string CreateSchemaSql = CreateTablesSql + CreateIndexesSql +
                                           CreateTrafficTablesSql + CreateTrafficIndexesSql +
                                           CreatePerformanceTablesSql + CreatePerformanceIndexesSql +
                                           CreateFleetTablesSql + "PRAGMA user_version = 7;";

    private const string FindVersionOneCollisionsSql = """
        SELECT provider, site_id, collected_at_utc
        FROM search_observation_runs
        GROUP BY provider, site_id, collected_at_utc
        HAVING COUNT(*) > 1
        ORDER BY provider, site_id, collected_at_utc
        LIMIT 1;
        """;

    private const string MigrateLegacySchemaSql = """
        ALTER TABLE search_observations RENAME TO search_observations_legacy;
        ALTER TABLE search_observation_runs RENAME TO search_observation_runs_legacy;
        DROP INDEX IF EXISTS ix_search_observations_site_date;
        DROP INDEX IF EXISTS ix_search_observations_provider_site_date;
        DROP INDEX IF EXISTS ux_search_observation_runs_provider_site_collected;
        """ + CreateTablesSql + """
        INSERT INTO search_observation_runs (
            run_id, provider, site_id, collected_at_utc, source_kind, status,
            configuration_hash, evidence_reference, normalized_manifest_json
        )
        SELECT run_id, provider, site_id, collected_at_utc, source_kind, status,
               configuration_hash, evidence_reference, normalized_manifest_json
        FROM search_observation_runs_legacy;
        INSERT INTO search_observations (
            observation_key, run_id, provider, site_id, observation_date, page, query,
            country, device, search_type, clicks, impressions, click_through_rate,
            average_position, evidence_reference
        )
        SELECT observation_key, run_id, provider, site_id, observation_date, page, query,
               country, device, search_type, clicks, impressions, click_through_rate,
               average_position, evidence_reference
        FROM search_observations_legacy;
        DROP TABLE search_observations_legacy;
        DROP TABLE search_observation_runs_legacy;
        """ + CreateIndexesSql + CreateTrafficTablesSql + CreateTrafficIndexesSql +
        CreatePerformanceTablesSql + CreatePerformanceIndexesSql + CreateFleetTablesSql + "PRAGMA user_version = 7;";

    private const string MigrateVersionThreeSql = CreateTrafficTablesSql + CreateTrafficIndexesSql +
                                                   CreatePerformanceTablesSql + CreatePerformanceIndexesSql +
                                                   CreateFleetTablesSql + "PRAGMA user_version = 7;";

    private const string MigrateVersionFourSql = CreatePerformanceTablesSql + CreatePerformanceIndexesSql +
                                                  CreateFleetTablesSql + "PRAGMA user_version = 7;";

    private const string MigrateVersionFiveSql = CreateFleetTablesSql + "PRAGMA user_version = 7;";

    private const string MigrateVersionSixSql = """
        ALTER TABLE fleet_retained_coverage ADD COLUMN source_collected_at_utc TEXT NULL;
        UPDATE fleet_retained_coverage
        SET source_collected_at_utc = retained_at_utc
        WHERE source_collected_at_utc IS NULL;
        PRAGMA user_version = 7;
        """;

    private readonly string _databasePath;

    internal SqliteWebSearchObservationStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Search database path is required.", nameof(databasePath));

        _databasePath = Path.GetFullPath(databasePath.Trim().Trim('"'));
    }

    internal async Task<WebSearchObservationImportResult> ImportAsync(
        WebSearchObservationBatch normalizedBatch,
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
                var normalizedManifestJson = JsonSerializer.Serialize(normalizedBatch, WebCliJson.Options);
                var insertedRun = await transaction.ExecuteNonQueryAsync(
                    """
                    INSERT OR IGNORE INTO search_observation_runs (
                        run_id, provider, site_id, collected_at_utc, source_kind, status,
                        configuration_hash, evidence_reference, normalized_manifest_json
                    ) VALUES (
                        @run_id, @provider, @site_id, @collected_at_utc, @source_kind, @status,
                        @configuration_hash, @evidence_reference, @normalized_manifest_json
                    );
                    """,
                    RunParameters(normalizedBatch, normalizedManifestJson),
                    token).ConfigureAwait(false);

                if (insertedRun == 0)
                {
                    var existingManifest = await transaction.QueryAsListAsync(
                        "SELECT normalized_manifest_json FROM search_observation_runs WHERE provider = @provider AND site_id = @site_id AND run_id = @run_id;",
                        static record => record.GetString(0),
                        new Dictionary<string, object?>
                        {
                            ["@provider"] = normalizedBatch.Provider,
                            ["@site_id"] = normalizedBatch.SiteId,
                            ["@run_id"] = normalizedBatch.RunId
                        },
                        cancellationToken: token).ConfigureAwait(false);
                    if (existingManifest.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Search collection time '{normalizedBatch.CollectedAtUtc:O}' is already assigned to another run for provider '{normalizedBatch.Provider}' and site '{normalizedBatch.SiteId}'.");
                    }
                    if (existingManifest.Count != 1 ||
                        !string.Equals(existingManifest[0], normalizedManifestJson, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Search run identifier '{normalizedBatch.RunId}' is already assigned to different normalized evidence for provider '{normalizedBatch.Provider}' and site '{normalizedBatch.SiteId}'.");
                    }
                }

                var insertedCount = 0;
                foreach (var observation in normalizedBatch.Observations)
                {
                    insertedCount += await transaction.ExecuteNonQueryAsync(
                        """
                        INSERT OR IGNORE INTO search_observations (
                            observation_key, run_id, provider, site_id, observation_date, page, query,
                            country, device, search_type, clicks, impressions, click_through_rate,
                            average_position, evidence_reference
                        ) VALUES (
                            @observation_key, @run_id, @provider, @site_id, @observation_date, @page, @query,
                            @country, @device, @search_type, @clicks, @impressions, @click_through_rate,
                            @average_position, @evidence_reference
                        );
                        """,
                        ObservationParameters(normalizedBatch.RunId!, observation),
                        token).ConfigureAwait(false);
                }

                return insertedCount;
            },
            cancellationToken).ConfigureAwait(false);

        return new WebSearchObservationImportResult
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

    internal async Task<IReadOnlyList<WebSearchObservation>> QueryAsync(
        WebSearchObservationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SiteId))
            throw new ArgumentException("Search observation query requires a site identifier.", nameof(query));
        if (query.FromDate.HasValue && query.ThroughDate.HasValue && query.FromDate > query.ThroughDate)
            throw new ArgumentException("Search observation from date cannot be after through date.", nameof(query));
        if (!File.Exists(_databasePath))
            return Array.Empty<WebSearchObservation>();

        await using var client = new SQLite();
        await EnsureSchemaAsync(client, cancellationToken).ConfigureAwait(false);

        var clauses = new List<string> { "site_id = @site_id" };
        var parameters = new Dictionary<string, object?>
        {
            ["@site_id"] = query.SiteId.Trim().ToLowerInvariant()
        };
        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            clauses.Add("provider = @provider");
            parameters["@provider"] = query.Provider.Trim().ToLowerInvariant();
        }
        if (query.FromDate.HasValue)
        {
            clauses.Add("observation_date >= @from_date");
            parameters["@from_date"] = FormatDate(query.FromDate.Value);
        }
        if (query.ThroughDate.HasValue)
        {
            clauses.Add("observation_date <= @through_date");
            parameters["@through_date"] = FormatDate(query.ThroughDate.Value);
        }

        var sql = $"""
            WITH completed_runs AS MATERIALIZED (
                SELECT runs.provider,
                       runs.site_id,
                       runs.run_id,
                       runs.collected_at_utc,
                       runs.normalized_manifest_json,
                       completed_dates.value AS observation_date,
                       COALESCE(NULLIF(LOWER(TRIM(json_extract(
                           runs.normalized_manifest_json,
                           '$.collectionCoverage.mode'))), ''), 'daily') AS coverage_mode,
                       COALESCE(
                           NULLIF(LOWER(TRIM(json_extract(
                               runs.normalized_manifest_json,
                               '$.collectionCoverage.searchType'))), ''),
                           'web') AS search_type
                FROM search_observation_runs AS runs
                INNER JOIN json_each(
                    runs.normalized_manifest_json,
                    '$.collectionCoverage.completedDates') AS completed_dates
                WHERE runs.status = 'complete'
            ),
            complete_coverage AS MATERIALIZED (
                SELECT completed.provider,
                       completed.site_id,
                       completed.run_id,
                       completed.collected_at_utc,
                       completed.observation_date,
                       completed.search_type,
                       COALESCE(NULLIF(LOWER(TRIM(dimension_scopes.value)), ''), '*') AS dimension_scope
                FROM completed_runs AS completed
                LEFT JOIN json_each(
                    completed.normalized_manifest_json,
                    '$.collectionCoverage.dimensionScopes') AS dimension_scopes ON TRUE
                WHERE completed.coverage_mode != 'snapshot'
                   OR NOT EXISTS (
                       SELECT 1
                       FROM search_observations AS covered_observations
                       WHERE covered_observations.provider = completed.provider
                         AND covered_observations.site_id = completed.site_id
                         AND covered_observations.run_id = completed.run_id
                         AND covered_observations.observation_date = completed.observation_date
                   )

                UNION

                SELECT completed.provider,
                       completed.site_id,
                       completed.run_id,
                       completed.collected_at_utc,
                       completed.observation_date,
                       completed.search_type,
                       CASE
                           WHEN covered_observations.page IS NOT NULL AND covered_observations.query IS NOT NULL THEN 'page-query'
                           WHEN covered_observations.page IS NOT NULL THEN 'page'
                           ELSE 'query'
                       END AS dimension_scope
                FROM completed_runs AS completed
                INNER JOIN search_observations AS covered_observations
                    ON covered_observations.provider = completed.provider
                   AND covered_observations.site_id = completed.site_id
                   AND covered_observations.run_id = completed.run_id
                   AND covered_observations.observation_date = completed.observation_date
                WHERE completed.coverage_mode = 'snapshot'
            ),
            ranked_observations AS (
                SELECT observations.observation_key,
                       observations.provider,
                       observations.site_id,
                       observations.observation_date,
                       observations.page,
                       observations.query,
                       observations.country,
                       observations.device,
                       observations.search_type,
                       observations.clicks,
                       observations.impressions,
                       observations.click_through_rate,
                       observations.average_position,
                       observations.evidence_reference,
                       ROW_NUMBER() OVER (
                           PARTITION BY observations.provider,
                                        observations.site_id,
                                        observations.observation_date,
                                        COALESCE(observations.page, ''),
                                        COALESCE(observations.query, ''),
                                        COALESCE(observations.country, ''),
                                        COALESCE(observations.device, ''),
                                        COALESCE(observations.search_type, '')
                           ORDER BY CASE WHEN runs.status = 'complete' THEN 0 ELSE 1 END,
                                    runs.collected_at_utc DESC,
                                    observations.run_id DESC
                       ) AS revision_rank
                FROM search_observations AS observations
                INNER JOIN search_observation_runs AS runs
                    ON runs.provider = observations.provider
                   AND runs.site_id = observations.site_id
                   AND runs.run_id = observations.run_id
                WHERE {string.Join(" AND ", clauses.Select(clause => "observations." + clause))}
                  AND NOT EXISTS (
                      SELECT 1
                      FROM complete_coverage AS coverage
                      WHERE coverage.provider = observations.provider
                        AND coverage.site_id = observations.site_id
                        AND coverage.observation_date = observations.observation_date
                        AND coverage.search_type = COALESCE(NULLIF(LOWER(TRIM(observations.search_type)), ''), 'web')
                        AND (
                            coverage.dimension_scope = '*' OR
                            coverage.dimension_scope = CASE
                                WHEN observations.page IS NOT NULL AND observations.query IS NOT NULL THEN 'page-query'
                                WHEN observations.page IS NOT NULL THEN 'page'
                                ELSE 'query'
                            END
                        )
                        AND (
                            coverage.collected_at_utc > runs.collected_at_utc OR
                            (coverage.collected_at_utc = runs.collected_at_utc AND coverage.run_id > runs.run_id)
                        )
                  )
            )
            SELECT observation_key, provider, site_id, observation_date, page, query,
                   country, device, search_type, clicks, impressions, click_through_rate,
                   average_position, evidence_reference
            FROM ranked_observations
            WHERE revision_rank = 1
            ORDER BY observation_date, observation_key;
            """;

        return await client.QueryAsListAsync(
            _databasePath,
            sql,
            MapObservation,
            parameters,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(SQLite client, CancellationToken cancellationToken)
    {
        await using var session = await client.OpenSessionAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await session.RunInTransactionAsync(
            async (transaction, token) =>
            {
                var versionValue = await transaction.ExecuteScalarAsync(
                    "PRAGMA user_version;",
                    cancellationToken: token).ConfigureAwait(false);
                var version = Convert.ToInt32(versionValue, CultureInfo.InvariantCulture);
                if (version > CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"Search database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
                }
                if (version == 0)
                {
                    var existingObject = await transaction.QueryAsListAsync(
                        "SELECT name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 1;",
                        static record => record.GetString(0),
                        cancellationToken: token).ConfigureAwait(false);
                    if (existingObject.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Refusing to initialize search storage in nonempty schema-version-zero database '{_databasePath}'. Existing object: {existingObject[0]}.");
                    }

                    await transaction.ExecuteNonQueryAsync(
                        CreateSchemaSql,
                        cancellationToken: token).ConfigureAwait(false);
                    return;
                }
                if (version == 1)
                {
                    var collisions = await transaction.QueryAsListAsync(
                        FindVersionOneCollisionsSql,
                        static record => $"{record.GetString(0)}/{record.GetString(1)} at {record.GetString(2)}",
                        cancellationToken: token).ConfigureAwait(false);
                    if (collisions.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Search database schema v1 contains competing runs for {collisions[0]}. Resolve the duplicate collection timestamp before upgrading to schema v{CurrentSchemaVersion}.");
                    }
                }
                if (version is 1 or 2)
                {
                    await transaction.ExecuteNonQueryAsync(
                        MigrateLegacySchemaSql,
                        cancellationToken: token).ConfigureAwait(false);
                }
                else if (version == 3)
                {
                    await transaction.ExecuteNonQueryAsync(
                        MigrateVersionThreeSql,
                        cancellationToken: token).ConfigureAwait(false);
                }
                else if (version == 4)
                {
                    await transaction.ExecuteNonQueryAsync(
                        MigrateVersionFourSql,
                        cancellationToken: token).ConfigureAwait(false);
                }
                else if (version == 5)
                {
                    await transaction.ExecuteNonQueryAsync(
                        MigrateVersionFiveSql,
                        cancellationToken: token).ConfigureAwait(false);
                }
                else if (version == 6)
                {
                    await transaction.ExecuteNonQueryAsync(
                        MigrateVersionSixSql,
                        cancellationToken: token).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private void EnsureDatabaseDirectory()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static Dictionary<string, object?> RunParameters(
        WebSearchObservationBatch batch,
        string normalizedManifestJson) => new()
    {
        ["@run_id"] = batch.RunId,
        ["@provider"] = batch.Provider,
        ["@site_id"] = batch.SiteId,
        ["@collected_at_utc"] = batch.CollectedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["@source_kind"] = batch.SourceKind,
        ["@status"] = batch.Status,
        ["@configuration_hash"] = batch.ConfigurationHash,
        ["@evidence_reference"] = batch.EvidenceReference,
        ["@normalized_manifest_json"] = normalizedManifestJson
    };

    private static Dictionary<string, object?> ObservationParameters(string runId, WebSearchObservation observation) => new()
    {
        ["@observation_key"] = observation.ObservationKey,
        ["@run_id"] = runId,
        ["@provider"] = observation.Provider,
        ["@site_id"] = observation.SiteId,
        ["@observation_date"] = FormatDate(observation.Date),
        ["@page"] = observation.Page,
        ["@query"] = observation.Query,
        ["@country"] = observation.Country,
        ["@device"] = observation.Device,
        ["@search_type"] = observation.SearchType,
        ["@clicks"] = observation.Clicks,
        ["@impressions"] = observation.Impressions,
        ["@click_through_rate"] = observation.ClickThroughRate,
        ["@average_position"] = observation.AveragePosition,
        ["@evidence_reference"] = observation.EvidenceReference
    };

    private static WebSearchObservation MapObservation(IDataRecord record) => new()
    {
        ObservationKey = record.GetString(0),
        Provider = record.GetString(1),
        SiteId = record.GetString(2),
        Date = DateOnly.ParseExact(record.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Page = GetNullableString(record, 4),
        Query = GetNullableString(record, 5),
        Country = GetNullableString(record, 6),
        Device = GetNullableString(record, 7),
        SearchType = GetNullableString(record, 8),
        Clicks = record.GetInt64(9),
        Impressions = record.GetInt64(10),
        ClickThroughRate = record.IsDBNull(11) ? null : record.GetDouble(11),
        AveragePosition = record.IsDBNull(12) ? null : record.GetDouble(12),
        EvidenceReference = GetNullableString(record, 13)
    };

    private static string? GetNullableString(IDataRecord record, int ordinal) =>
        record.IsDBNull(ordinal) ? null : record.GetString(ordinal);

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
