using System.Globalization;

namespace PowerForge.Web;

/// <summary>Validates and canonicalizes provider-neutral search performance imports.</summary>
public static class WebSearchObservationNormalizer
{
    private static readonly HashSet<string> SupportedStatuses = new(StringComparer.Ordinal)
    {
        "complete",
        "partial"
    };

    /// <summary>Returns a validated normalized copy with deterministic run and observation identities.</summary>
    /// <param name="batch">Import batch to normalize.</param>
    /// <returns>A normalized copy safe for persistence and analysis.</returns>
    public static WebSearchObservationBatch Normalize(WebSearchObservationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.SchemaVersion is < WebSearchObservationBatch.MinimumSupportedSchemaVersion or > WebSearchObservationBatch.CurrentSchemaVersion)
            throw new ArgumentException($"Unsupported search observation schema version '{batch.SchemaVersion}'.", nameof(batch));
        if (batch.SchemaVersion == 1 && (batch.CollectionCoverageSpecified || batch.ZeroDataConfirmedSpecified))
            throw new ArgumentException("Search observation schema version 1 cannot contain collection coverage or zero-data confirmation.", nameof(batch));
        if (batch.SchemaVersion == 2 && batch.CollectionCoverage is null)
            throw new ArgumentException("Search observation schema version 2 requires collection coverage.", nameof(batch));
        if (batch.ObservationsWasNull)
            throw new ArgumentException("Search observation observations must be an array.", nameof(batch));

        var provider = NormalizeRequiredIdentifier(batch.Provider, "provider");
        var siteId = NormalizeRequiredIdentifier(batch.SiteId, "siteId");
        var sourceKind = NormalizeRequiredIdentifier(batch.SourceKind, "sourceKind");
        var status = NormalizeRequiredIdentifier(batch.Status, "status");
        if (!SupportedStatuses.Contains(status))
            throw new ArgumentException("Search observation status must be 'complete' or 'partial'.", nameof(batch));
        if (batch.CollectedAtUtc == default)
            throw new ArgumentException("Search observation batch requires collectedAtUtc.", nameof(batch));

        var normalizedObservations = batch.Observations
            .Select((observation, index) => NormalizeObservation(observation, provider, siteId, batch.EvidenceReference, index))
            .ToArray();
        var duplicateDimension = normalizedObservations
            .GroupBy(ComputeObservationDimensionFingerprint, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateDimension is not null)
        {
            throw new ArgumentException(
                "A search observation batch cannot contain multiple rows for the same provider, site, date, and dimensions.",
                nameof(batch));
        }

        var observations = normalizedObservations
            .Select(observation => (Observation: observation, Fingerprint: ComputeObservationContentFingerprint(observation)))
            .OrderBy(item => item.Fingerprint, StringComparer.Ordinal)
            .Select(item => item.Observation)
            .ToArray();

        var collectionCoverage = NormalizeCollectionCoverage(batch.CollectionCoverage, status, observations);

        if (status == "complete" && observations.Length == 0 && !batch.ZeroDataConfirmed)
            throw new ArgumentException("A complete empty search observation batch must explicitly confirm zero provider data.", nameof(batch));
        if (batch.ZeroDataConfirmed && (status != "complete" || observations.Length != 0))
            throw new ArgumentException("zeroDataConfirmed is valid only for a complete batch with no observations.", nameof(batch));
        if (batch.ZeroDataConfirmed && collectionCoverage is null)
            throw new ArgumentException("zeroDataConfirmed requires durable collection coverage.", nameof(batch));

        var normalized = new WebSearchObservationBatch
        {
            SchemaVersion = batch.SchemaVersion,
            Provider = provider,
            SiteId = siteId,
            CollectedAtUtc = batch.CollectedAtUtc.ToUniversalTime(),
            SourceKind = sourceKind,
            Status = status,
            ConfigurationHash = NormalizeOptional(batch.ConfigurationHash),
            EvidenceReference = NormalizeOptional(batch.EvidenceReference),
            Observations = observations
        };
        if (batch.SchemaVersion >= 2)
        {
            normalized.CollectionCoverage = collectionCoverage;
            normalized.ZeroDataConfirmed = batch.ZeroDataConfirmed;
        }
        normalized.RunId = NormalizeOptional(batch.RunId) ?? ComputeRunId(normalized);
        foreach (var observation in normalized.Observations)
            observation.ObservationKey = ComputeObservationKey(normalized.RunId, observation);
        return normalized;
    }

    private static WebSearchObservationCollectionCoverage? NormalizeCollectionCoverage(
        WebSearchObservationCollectionCoverage? coverage,
        string status,
        IReadOnlyCollection<WebSearchObservation> observations)
    {
        if (coverage is null)
            return null;
        if (coverage.CompletedDates is null)
            throw new ArgumentException("Search collection coverage completedDates must be an array.", nameof(coverage));
        if (coverage.FromDate == default || coverage.ThroughDate == default || coverage.FromDate > coverage.ThroughDate)
            throw new ArgumentException("Search collection coverage has an invalid requested date range.", nameof(coverage));

        var completedDates = coverage.CompletedDates
            .OrderBy(date => date)
            .ToArray();
        if (completedDates.Distinct().Count() != completedDates.Length ||
            completedDates.Any(date => date < coverage.FromDate || date > coverage.ThroughDate))
        {
            throw new ArgumentException("Search collection coverage completed dates must be unique and inside the requested range.", nameof(coverage));
        }

        var searchType = NormalizeDimension(coverage.SearchType);
        var failureCategory = NormalizeDimension(coverage.FailureCategory);
        if (coverage.FailedDate is DateOnly boundedFailedDate && (boundedFailedDate < coverage.FromDate || boundedFailedDate > coverage.ThroughDate))
            throw new ArgumentException("Search collection coverage failed date must be inside the requested range.", nameof(coverage));
        if (coverage.FailedDate is DateOnly duplicateFailedDate && completedDates.Contains(duplicateFailedDate))
            throw new ArgumentException("Search collection coverage cannot mark the same date completed and failed.", nameof(coverage));

        if (status == "complete")
        {
            var expectedDateCount = coverage.ThroughDate.DayNumber - coverage.FromDate.DayNumber + 1;
            if (coverage.FailedDate is not null || failureCategory is not null || completedDates.Length != expectedDateCount)
                throw new ArgumentException("Complete search collection coverage must include every requested date and no failure.", nameof(coverage));
            for (var index = 0; index < completedDates.Length; index++)
            {
                if (completedDates[index] != coverage.FromDate.AddDays(index))
                    throw new ArgumentException("Complete search collection coverage must include every requested date exactly once.", nameof(coverage));
            }
        }
        else
        {
            if (coverage.FailedDate is not DateOnly failedDate || failureCategory is null)
                throw new ArgumentException("Partial search collection coverage must provide both failedDate and failureCategory.", nameof(coverage));
            var expectedCompletedCount = failedDate.DayNumber - coverage.FromDate.DayNumber;
            if (completedDates.Length != expectedCompletedCount)
                throw new ArgumentException("Partial search collection coverage must include every date before failedDate as completed.", nameof(coverage));
            for (var index = 0; index < completedDates.Length; index++)
            {
                if (completedDates[index] != coverage.FromDate.AddDays(index))
                    throw new ArgumentException("Partial search collection coverage completed dates must be the consecutive prefix before failedDate.", nameof(coverage));
            }
        }

        foreach (var observation in observations)
        {
            if (observation.Date < coverage.FromDate || observation.Date > coverage.ThroughDate)
                throw new ArgumentException("Search observations must fall inside collection coverage.", nameof(coverage));
            if (!completedDates.Contains(observation.Date) && observation.Date != coverage.FailedDate)
                throw new ArgumentException("Partial search observations may belong only to completed dates or the failed date.", nameof(coverage));
            if (searchType is not null && !string.Equals(observation.SearchType, searchType, StringComparison.Ordinal))
                throw new ArgumentException("Search observation type must match collection coverage.", nameof(coverage));
        }

        return new WebSearchObservationCollectionCoverage
        {
            FromDate = coverage.FromDate,
            ThroughDate = coverage.ThroughDate,
            SearchType = searchType,
            CompletedDates = completedDates,
            FailedDate = coverage.FailedDate,
            FailureCategory = failureCategory
        };
    }

    private static WebSearchObservation NormalizeObservation(
        WebSearchObservation observation,
        string provider,
        string siteId,
        string? batchEvidenceReference,
        int index)
    {
        if (observation is null)
            throw new ArgumentException($"Search observation at index {index} is null.", nameof(observation));
        if (observation.Date == default)
            throw new ArgumentException($"Search observation at index {index} requires a date.", nameof(observation));
        if (observation.Clicks < 0 || observation.Impressions < 0)
            throw new ArgumentException($"Search observation at index {index} cannot contain negative clicks or impressions.", nameof(observation));
        if (observation.Clicks > observation.Impressions)
            throw new ArgumentException($"Search observation at index {index} has more clicks than impressions.", nameof(observation));
        if (observation.ClickThroughRate is double clickThroughRate &&
            (!double.IsFinite(clickThroughRate) || clickThroughRate is < 0d or > 1d))
        {
            throw new ArgumentException($"Search observation at index {index} has CTR that is not finite or outside the zero-to-one range.", nameof(observation));
        }
        if (observation.AveragePosition is double averagePosition &&
            (!double.IsFinite(averagePosition) || averagePosition < 0d))
        {
            throw new ArgumentException($"Search observation at index {index} has an average position that is not finite or is negative.", nameof(observation));
        }

        var page = NormalizePage(observation.Page, index);
        var query = NormalizeOptional(observation.Query);
        if (page is null && query is null)
            throw new ArgumentException($"Search observation at index {index} requires a page or query dimension.", nameof(observation));
        var normalizedClickThroughRate = observation.ClickThroughRate is double providedClickThroughRate
            ? CanonicalizeZero(providedClickThroughRate)
            : observation.Impressions == 0
                ? 0d
                : (double)observation.Clicks / observation.Impressions;
        double? normalizedAveragePosition = observation.AveragePosition is double providedAveragePosition
            ? CanonicalizeZero(providedAveragePosition)
            : null;

        var normalized = new WebSearchObservation
        {
            Provider = string.IsNullOrWhiteSpace(observation.Provider)
                ? provider
                : NormalizeRequiredIdentifier(observation.Provider, $"observations[{index}].provider"),
            SiteId = string.IsNullOrWhiteSpace(observation.SiteId)
                ? siteId
                : NormalizeRequiredIdentifier(observation.SiteId, $"observations[{index}].siteId"),
            Date = observation.Date,
            Page = page,
            Query = query,
            Country = NormalizeDimension(observation.Country),
            Device = NormalizeDimension(observation.Device),
            SearchType = NormalizeDimension(observation.SearchType),
            Clicks = observation.Clicks,
            Impressions = observation.Impressions,
            ClickThroughRate = normalizedClickThroughRate,
            AveragePosition = normalizedAveragePosition,
            EvidenceReference = NormalizeOptional(observation.EvidenceReference) ?? NormalizeOptional(batchEvidenceReference)
        };

        if (!string.Equals(normalized.Provider, provider, StringComparison.Ordinal) ||
            !string.Equals(normalized.SiteId, siteId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Search observation at index {index} does not match its batch provider and site.", nameof(observation));
        }

        return normalized;
    }

    private static string? NormalizePage(string? value, int index)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
            return null;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Search observation at index {index} has a page that is not an absolute HTTP(S) URL.", nameof(value));
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.AbsoluteUri;
    }

    private static string NormalizeRequiredIdentifier(string? value, string field)
    {
        var normalized = NormalizeDimension(value);
        if (normalized is null)
            throw new ArgumentException($"Search observation batch requires {field}.", field);
        return normalized;
    }

    private static string? NormalizeDimension(string? value) => NormalizeOptional(value)?.ToLowerInvariant();

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double CanonicalizeZero(double value) => value == 0d ? 0d : value;

    private static string ComputeObservationKey(string runId, WebSearchObservation observation) => WebSearchIdentityHasher.Compute(
        runId,
        ComputeObservationContentFingerprint(observation));

    private static string ComputeObservationContentFingerprint(WebSearchObservation observation) => WebSearchIdentityHasher.Compute(
        observation.Provider,
        observation.SiteId,
        observation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        observation.Page,
        observation.Query,
        observation.Country,
        observation.Device,
        observation.SearchType,
        observation.Clicks.ToString(CultureInfo.InvariantCulture),
        observation.Impressions.ToString(CultureInfo.InvariantCulture),
        observation.ClickThroughRate?.ToString("R", CultureInfo.InvariantCulture),
        observation.AveragePosition?.ToString("R", CultureInfo.InvariantCulture),
        observation.EvidenceReference);

    private static string ComputeObservationDimensionFingerprint(WebSearchObservation observation) => WebSearchIdentityHasher.Compute(
        observation.Provider,
        observation.SiteId,
        observation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        observation.Page,
        observation.Query,
        observation.Country,
        observation.Device,
        observation.SearchType);

    private static string ComputeRunId(WebSearchObservationBatch batch)
    {
        var observations = string.Join("|", batch.Observations.Select(ComputeObservationContentFingerprint));
        if (batch.SchemaVersion == 1)
        {
            return WebSearchIdentityHasher.Compute(
                batch.Provider,
                batch.SiteId,
                batch.CollectedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                batch.SourceKind,
                batch.Status,
                batch.ConfigurationHash,
                batch.EvidenceReference,
                observations);
        }

        var coverage = batch.CollectionCoverage;
        return WebSearchIdentityHasher.Compute(
            batch.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            batch.Provider,
            batch.SiteId,
            batch.CollectedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            batch.SourceKind,
            batch.Status,
            batch.ConfigurationHash,
            batch.EvidenceReference,
            batch.ZeroDataConfirmed ? "zero-data-confirmed" : null,
            coverage?.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            coverage?.ThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            coverage?.SearchType,
            coverage is null ? null : string.Join(",", coverage.CompletedDates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            coverage?.FailedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            coverage?.FailureCategory,
            observations);
    }
}
