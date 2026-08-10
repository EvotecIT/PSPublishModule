using System.Globalization;

namespace PowerForge.Web;

/// <summary>Validates and canonicalizes provider-neutral website traffic observations.</summary>
public static class WebTrafficObservationNormalizer
{
    /// <summary>Returns a normalized copy with deterministic run and observation identities.</summary>
    public static WebTrafficObservationBatch Normalize(WebTrafficObservationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.SchemaVersion != WebTrafficObservationBatch.CurrentSchemaVersion)
            throw new ArgumentException($"Unsupported traffic observation schema version '{batch.SchemaVersion}'.", nameof(batch));

        var provider = RequiredIdentifier(batch.Provider, "provider");
        var siteId = RequiredIdentifier(batch.SiteId, "siteId");
        var sourceKind = RequiredIdentifier(batch.SourceKind, "sourceKind");
        var status = RequiredIdentifier(batch.Status, "status");
        if (status is not ("complete" or "partial"))
            throw new ArgumentException("Traffic observation status must be 'complete' or 'partial'.", nameof(batch));
        if (batch.CollectedAtUtc == default)
            throw new ArgumentException("Traffic observation batch requires collectedAtUtc.", nameof(batch));

        if (batch.Observations is null)
            throw new ArgumentException("Traffic observations must be an array.", nameof(batch));
        var observations = batch.Observations
            .Select((value, index) => NormalizeObservation(value, provider, siteId, batch.EvidenceReference, index))
            .OrderBy(ContentFingerprint, StringComparer.Ordinal)
            .ToArray();
        var duplicate = observations.GroupBy(DimensionFingerprint, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException("A traffic batch cannot contain multiple rows for the same provider, site, date, host, and path.", nameof(batch));

        var coverage = NormalizeCoverage(batch.CollectionCoverage, status, observations);
        if (status == "complete" && observations.Length == 0 && !batch.ZeroDataConfirmed)
            throw new ArgumentException("A complete empty traffic batch must explicitly confirm zero provider data.", nameof(batch));
        if (batch.ZeroDataConfirmed && (status != "complete" || observations.Length != 0))
            throw new ArgumentException("zeroDataConfirmed is valid only for a complete empty traffic batch.", nameof(batch));

        var normalized = new WebTrafficObservationBatch
        {
            SchemaVersion = batch.SchemaVersion,
            Provider = provider,
            SiteId = siteId,
            CollectedAtUtc = batch.CollectedAtUtc.ToUniversalTime(),
            SourceKind = sourceKind,
            Status = status,
            ConfigurationHash = Optional(batch.ConfigurationHash),
            EvidenceReference = Optional(batch.EvidenceReference),
            CollectionCoverage = coverage,
            ZeroDataConfirmed = batch.ZeroDataConfirmed,
            Observations = observations
        };
        normalized.RunId = Optional(batch.RunId) ?? RunFingerprint(normalized);
        foreach (var observation in normalized.Observations)
            observation.ObservationKey = WebSearchIdentityHasher.Compute(normalized.RunId, ContentFingerprint(observation));
        return normalized;
    }

    private static WebTrafficObservationCollectionCoverage NormalizeCoverage(
        WebTrafficObservationCollectionCoverage coverage,
        string status,
        IReadOnlyCollection<WebTrafficObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        if (coverage.FromDate == default || coverage.ThroughDate == default || coverage.FromDate > coverage.ThroughDate)
            throw new ArgumentException("Traffic collection coverage has an invalid date range.", nameof(coverage));
        var completed = (coverage.CompletedDates ?? Array.Empty<DateOnly>()).OrderBy(value => value).ToArray();
        if (completed.Distinct().Count() != completed.Length || completed.Any(value => value < coverage.FromDate || value > coverage.ThroughDate))
            throw new ArgumentException("Traffic completed dates must be unique and inside the requested range.", nameof(coverage));
        var failureCategory = Dimension(coverage.FailureCategory);

        if (status == "complete")
        {
            var expected = coverage.ThroughDate.DayNumber - coverage.FromDate.DayNumber + 1;
            if (coverage.FailedDate is not null || failureCategory is not null || completed.Length != expected)
                throw new ArgumentException("Complete traffic coverage must include every requested date and no failure.", nameof(coverage));
        }
        else
        {
            if (coverage.FailedDate is not DateOnly failedDate || failureCategory is null ||
                failedDate < coverage.FromDate || failedDate > coverage.ThroughDate)
            {
                throw new ArgumentException("Partial traffic coverage requires a bounded failedDate and failureCategory.", nameof(coverage));
            }
            if (completed.Length != failedDate.DayNumber - coverage.FromDate.DayNumber)
                throw new ArgumentException("Partial traffic completed dates must be the consecutive prefix before failedDate.", nameof(coverage));
        }
        for (var index = 0; index < completed.Length; index++)
        {
            if (completed[index] != coverage.FromDate.AddDays(index))
                throw new ArgumentException("Traffic completed dates must be consecutive from fromDate.", nameof(coverage));
        }
        foreach (var observation in observations)
        {
            if (!completed.Contains(observation.Date) && observation.Date != coverage.FailedDate)
                throw new ArgumentException("Traffic observations may belong only to completed dates or the failed date.", nameof(coverage));
        }

        return new WebTrafficObservationCollectionCoverage
        {
            FromDate = coverage.FromDate,
            ThroughDate = coverage.ThroughDate,
            CompletedDates = completed,
            FailedDate = coverage.FailedDate,
            FailureCategory = failureCategory
        };
    }

    private static WebTrafficObservation NormalizeObservation(
        WebTrafficObservation observation,
        string provider,
        string siteId,
        string? batchEvidence,
        int index)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Date == default)
            throw new ArgumentException($"Traffic observation at index {index} requires a date.", nameof(observation));
        if (observation.Requests < 0 || observation.Visits < 0 || observation.EdgeResponseBytes < 0)
            throw new ArgumentException($"Traffic observation at index {index} cannot contain negative metrics.", nameof(observation));
        if (!double.IsFinite(observation.SampleInterval) || observation.SampleInterval < 1d)
            throw new ArgumentException($"Traffic observation at index {index} requires a finite sampleInterval of at least one.", nameof(observation));

        var normalizedProvider = string.IsNullOrWhiteSpace(observation.Provider) ? provider : RequiredIdentifier(observation.Provider, $"observations[{index}].provider");
        var normalizedSite = string.IsNullOrWhiteSpace(observation.SiteId) ? siteId : RequiredIdentifier(observation.SiteId, $"observations[{index}].siteId");
        if (normalizedProvider != provider || normalizedSite != siteId)
            throw new ArgumentException($"Traffic observation at index {index} does not match its batch provider and site.", nameof(observation));
        var host = NormalizeHost(observation.Host, index);
        var path = NormalizePath(observation.Path, index);

        return new WebTrafficObservation
        {
            Provider = provider,
            SiteId = siteId,
            Date = observation.Date,
            Host = host,
            Path = path,
            Requests = observation.Requests,
            Visits = observation.Visits,
            EdgeResponseBytes = observation.EdgeResponseBytes,
            SampleInterval = observation.SampleInterval,
            EvidenceReference = Optional(observation.EvidenceReference) ?? Optional(batchEvidence)
        };
    }

    private static string NormalizeHost(string? value, int index)
    {
        var host = Optional(value)?.TrimEnd('.');
        if (host is null || !Uri.CheckHostName(host).Equals(UriHostNameType.Dns))
            throw new ArgumentException($"Traffic observation at index {index} requires a DNS host.", nameof(value));
        var uri = new Uri("https://" + host + "/");
        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
    }

    private static string NormalizePath(string? value, int index)
    {
        var path = Optional(value);
        if (path is null || !path.StartsWith("/", StringComparison.Ordinal) || path.Contains('#') || path.Any(char.IsControl))
            throw new ArgumentException($"Traffic observation at index {index} requires an absolute request path without a fragment.", nameof(value));
        return path;
    }

    private static string RunFingerprint(WebTrafficObservationBatch batch) => WebSearchIdentityHasher.Compute(
        batch.SchemaVersion.ToString(CultureInfo.InvariantCulture), batch.Provider, batch.SiteId,
        batch.CollectedAtUtc.ToString("O", CultureInfo.InvariantCulture), batch.SourceKind, batch.Status,
        batch.ConfigurationHash, batch.EvidenceReference, batch.ZeroDataConfirmed ? "zero-data-confirmed" : null,
        batch.CollectionCoverage.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        batch.CollectionCoverage.ThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string.Join(",", batch.CollectionCoverage.CompletedDates.Select(value => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
        batch.CollectionCoverage.FailedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        batch.CollectionCoverage.FailureCategory,
        string.Join("|", batch.Observations.Select(ContentFingerprint)));

    private static string DimensionFingerprint(WebTrafficObservation value) => WebSearchIdentityHasher.Compute(
        value.Provider, value.SiteId, value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value.Host, value.Path);

    private static string ContentFingerprint(WebTrafficObservation value) => WebSearchIdentityHasher.Compute(
        DimensionFingerprint(value), value.Requests.ToString(CultureInfo.InvariantCulture), value.Visits.ToString(CultureInfo.InvariantCulture),
        value.EdgeResponseBytes.ToString(CultureInfo.InvariantCulture), value.SampleInterval.ToString("R", CultureInfo.InvariantCulture),
        value.EvidenceReference);

    private static string RequiredIdentifier(string? value, string field) =>
        Dimension(value) ?? throw new ArgumentException($"Traffic observation batch requires {field}.", field);

    private static string? Dimension(string? value) => Optional(value)?.ToLowerInvariant();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
