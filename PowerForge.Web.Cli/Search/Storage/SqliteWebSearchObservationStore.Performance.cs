using System.Data;
using System.Globalization;
using System.Text.Json;
using DBAClientX;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed partial class SqliteWebSearchObservationStore
{
    internal async Task<WebPerformanceObservationImportResult> ImportPerformanceAsync(
        WebPerformanceObservationBatch normalizedBatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedBatch);
        EnsureDatabaseDirectory();
        await using var client = new SQLite();
        await EnsureSchemaAsync(client, cancellationToken).ConfigureAwait(false);
        await using var session = await client.OpenSessionAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var inserted = await session.RunInTransactionAsync(async (transaction, token) =>
        {
            var manifest = JsonSerializer.Serialize(normalizedBatch, WebCliJson.Options);
            var insertedRun = await transaction.ExecuteNonQueryAsync(
                """
                INSERT OR IGNORE INTO performance_observation_runs (
                    run_id, provider, site_id, collected_at_utc, source_kind, status,
                    measurement_kind, target_kind, target_url, form_factor, tool_version,
                    zero_data_confirmed, configuration_hash, evidence_reference, normalized_manifest_json
                ) VALUES (
                    @run_id, @provider, @site_id, @collected_at_utc, @source_kind, @status,
                    @measurement_kind, @target_kind, @target_url, @form_factor, @tool_version,
                    @zero_data_confirmed, @configuration_hash, @evidence_reference, @normalized_manifest_json
                );
                """,
                PerformanceRunParameters(normalizedBatch, manifest), token).ConfigureAwait(false);
            if (insertedRun == 0)
            {
                var existing = await transaction.QueryAsListAsync(
                    "SELECT normalized_manifest_json FROM performance_observation_runs WHERE provider = @provider AND site_id = @site_id AND run_id = @run_id;",
                    static record => record.GetString(0),
                    new Dictionary<string, object?>
                    {
                        ["@provider"] = normalizedBatch.Provider,
                        ["@site_id"] = normalizedBatch.SiteId,
                        ["@run_id"] = normalizedBatch.RunId
                    }, cancellationToken: token).ConfigureAwait(false);
                if (existing.Count == 0)
                    throw new InvalidOperationException($"Performance collection time '{normalizedBatch.CollectedAtUtc:O}' is already assigned to another run for this provider and site.");
                if (existing.Count != 1 || !string.Equals(existing[0], manifest, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Performance run identifier '{normalizedBatch.RunId}' is already assigned to different normalized evidence.");
            }

            var count = 0;
            foreach (var observation in normalizedBatch.Observations)
            {
                count += await transaction.ExecuteNonQueryAsync(
                    """
                    INSERT OR IGNORE INTO performance_observations (
                        observation_key, run_id, provider, site_id, metric, metric_value, unit,
                        percentile, period_start_date, period_end_date, histogram_json
                    ) VALUES (
                        @observation_key, @run_id, @provider, @site_id, @metric, @metric_value, @unit,
                        @percentile, @period_start_date, @period_end_date, @histogram_json
                    );
                    """,
                    PerformanceObservationParameters(normalizedBatch, observation), token).ConfigureAwait(false);
            }
            return count;
        }, cancellationToken).ConfigureAwait(false);

        return new WebPerformanceObservationImportResult
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

    internal async Task<WebPerformanceObservationQueryResult> QueryPerformanceEvidenceAsync(
        WebPerformanceObservationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SiteId))
            throw new ArgumentException("Performance query requires a site identifier.", nameof(query));
        var measurementKind = NormalizePerformanceFilter(query.MeasurementKind, "measurement kind", "lab", "field");
        var formFactor = NormalizePerformanceFilter(query.FormFactor, "form factor", "all", "phone", "desktop", "tablet");
        var targetUrl = string.IsNullOrWhiteSpace(query.TargetUrl)
            ? null
            : WebPerformanceObservationNormalizer.CanonicalizeTarget(query.TargetUrl, "url");
        if (!File.Exists(_databasePath))
            return new WebPerformanceObservationQueryResult { StoreExists = false };

        await using var client = new SQLite();
        await EnsureSchemaAsync(client, cancellationToken).ConfigureAwait(false);
        var clauses = new List<string> { "site_id = @site_id" };
        var parameters = new Dictionary<string, object?> { ["@site_id"] = query.SiteId.Trim().ToLowerInvariant() };
        AddPerformanceFilter(query.Provider, "provider", clauses, parameters);
        AddPerformanceFilter(measurementKind, "measurement_kind", clauses, parameters);
        AddPerformanceFilter(formFactor, "form_factor", clauses, parameters);
        if (targetUrl is not null)
        {
            clauses.Add("target_url = @target_url");
            parameters["@target_url"] = targetUrl;
        }
        var manifests = await client.QueryAsListAsync(
            _databasePath,
            $"SELECT normalized_manifest_json FROM performance_observation_runs WHERE {string.Join(" AND ", clauses)};",
            static record => record.GetString(0), parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
        var batches = manifests.Select(manifest => JsonSerializer.Deserialize<WebPerformanceObservationBatch>(manifest, WebCliJson.Options)
                                               ?? throw new InvalidOperationException("Stored performance manifest is empty."))
            .Select(WebPerformanceObservationNormalizer.Normalize)
            .ToArray();
        var selected = batches
            .GroupBy(batch => string.Join("\u001f", batch.Provider, batch.SiteId, batch.MeasurementKind,
                batch.TargetKind, batch.TargetUrl, batch.FormFactor), StringComparer.Ordinal)
            .Select(group => group.OrderBy(batch => batch.Status == "complete" ? 0 : 1)
                                  .ThenByDescending(batch => batch.CollectedAtUtc)
                                  .ThenBy(batch => batch.RunId, StringComparer.Ordinal)
                                  .First())
            .OrderBy(batch => batch.MeasurementKind, StringComparer.Ordinal)
            .ThenBy(batch => batch.TargetUrl, StringComparer.Ordinal)
            .ThenBy(batch => batch.FormFactor, StringComparer.Ordinal)
            .ToArray();

        var evidenceSets = selected.Select(batch => new WebPerformanceObservationEvidenceSet
        {
            Run = new WebPerformanceObservationRunEvidence
            {
                RunId = batch.RunId!, Provider = batch.Provider, SiteId = batch.SiteId,
                CollectedAtUtc = batch.CollectedAtUtc, Status = batch.Status,
                MeasurementKind = batch.MeasurementKind, TargetKind = batch.TargetKind,
                TargetUrl = batch.TargetUrl, FormFactor = batch.FormFactor,
                ToolVersion = batch.ToolVersion, ConfigurationHash = batch.ConfigurationHash,
                EvidenceReference = batch.EvidenceReference,
                ZeroDataConfirmed = batch.ZeroDataConfirmed
            },
            Observations = batch.Observations
        }).ToArray();
        return new WebPerformanceObservationQueryResult
        {
            StoreExists = true,
            HasEvidence = selected.Length > 0,
            HasPartialEvidence = selected.Any(batch => batch.Status == "partial"),
            HasExplicitZeroEvidence = selected.Any(batch => batch.ZeroDataConfirmed),
            EvidenceSets = evidenceSets
        };
    }

    private static void AddPerformanceFilter(string? value, string column, List<string> clauses, Dictionary<string, object?> parameters)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var parameter = "@" + column;
        clauses.Add(column + " = " + parameter);
        parameters[parameter] = value.Trim().ToLowerInvariant();
    }

    private static string? NormalizePerformanceFilter(string? value, string label, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (!allowed.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException($"Performance {label} must be one of: {string.Join(", ", allowed)}.", nameof(value));
        return normalized;
    }

    private static Dictionary<string, object?> PerformanceRunParameters(WebPerformanceObservationBatch batch, string manifest) => new()
    {
        ["@run_id"] = batch.RunId,
        ["@provider"] = batch.Provider,
        ["@site_id"] = batch.SiteId,
        ["@collected_at_utc"] = batch.CollectedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["@source_kind"] = batch.SourceKind,
        ["@status"] = batch.Status,
        ["@measurement_kind"] = batch.MeasurementKind,
        ["@target_kind"] = batch.TargetKind,
        ["@target_url"] = batch.TargetUrl,
        ["@form_factor"] = batch.FormFactor,
        ["@tool_version"] = batch.ToolVersion,
        ["@zero_data_confirmed"] = batch.ZeroDataConfirmed ? 1 : 0,
        ["@configuration_hash"] = batch.ConfigurationHash,
        ["@evidence_reference"] = batch.EvidenceReference,
        ["@normalized_manifest_json"] = manifest
    };

    private static Dictionary<string, object?> PerformanceObservationParameters(
        WebPerformanceObservationBatch batch,
        WebPerformanceObservation value) => new()
    {
        ["@observation_key"] = value.ObservationKey,
        ["@run_id"] = batch.RunId,
        ["@provider"] = batch.Provider,
        ["@site_id"] = batch.SiteId,
        ["@metric"] = value.Metric,
        ["@metric_value"] = value.Value,
        ["@unit"] = value.Unit,
        ["@percentile"] = value.Percentile,
        ["@period_start_date"] = value.PeriodStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["@period_end_date"] = value.PeriodEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["@histogram_json"] = JsonSerializer.Serialize(value.Histogram, WebCliJson.Options)
    };
}
