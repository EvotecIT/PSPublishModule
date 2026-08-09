using System.Globalization;

namespace PowerForge.Web;

/// <summary>Creates deterministic, evidence-linked search opportunities from normalized observations.</summary>
public static class WebSearchOpportunityAnalyzer
{
    private const string WeakPageRule = "search.weak-page";
    private const string CtrRule = "search.ctr-underperformance";

    /// <summary>Analyzes observations using deterministic rules and caller-supplied report time.</summary>
    /// <param name="observations">Normalized observations.</param>
    /// <param name="options">Analysis filters and thresholds.</param>
    /// <param name="generatedAtUtc">Report generation time supplied by the orchestration boundary.</param>
    /// <returns>An evidence-linked opportunity report.</returns>
    public static WebSearchOpportunityReport Analyze(
        IEnumerable<WebSearchObservation> observations,
        WebSearchOpportunityOptions options,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(options);

        var siteId = NormalizeRequired(options.SiteId, nameof(options.SiteId));
        var provider = NormalizeOptional(options.Provider);
        ValidateOptions(options);

        var filtered = observations
            .Where(observation => observation is not null)
            .Where(observation => string.Equals(observation.SiteId, siteId, StringComparison.OrdinalIgnoreCase))
            .Where(observation => provider is null || string.Equals(observation.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Where(observation => !options.FromDate.HasValue || observation.Date >= options.FromDate.Value)
            .Where(observation => !options.ThroughDate.HasValue || observation.Date <= options.ThroughDate.Value)
            .OrderBy(observation => observation.ObservationKey, StringComparer.Ordinal)
            .ToArray();

        var opportunities = new List<WebSearchOpportunity>();
        var groups = filtered
            .Where(observation => !string.IsNullOrWhiteSpace(observation.Page))
            .GroupBy(observation => new ObservationGroupKey(
                observation.Provider,
                observation.SiteId,
                observation.Page!,
                observation.Query,
                observation.Country,
                observation.Device,
                observation.SearchType));

        foreach (var group in groups)
        {
            var aggregate = Aggregate(group);
            if (aggregate.Impressions < options.MinimumImpressions || aggregate.AveragePosition is null)
                continue;

            if (aggregate.AveragePosition >= options.WeakPageMinimumPosition &&
                aggregate.AveragePosition <= options.WeakPageMaximumPosition)
            {
                opportunities.Add(CreateWeakPageOpportunity(group.Key, aggregate, options));
            }

            if (aggregate.AveragePosition <= options.CtrMaximumPosition &&
                aggregate.ClickThroughRate < options.MinimumClickThroughRate)
            {
                opportunities.Add(CreateCtrOpportunity(group.Key, aggregate, options));
            }
        }

        return new WebSearchOpportunityReport
        {
            GeneratedAtUtc = generatedAtUtc.ToUniversalTime(),
            SiteId = siteId,
            Provider = provider,
            ObservationCount = filtered.Length,
            FromDate = filtered.Length == 0 ? null : filtered.Min(observation => observation.Date),
            ThroughDate = filtered.Length == 0 ? null : filtered.Max(observation => observation.Date),
            Opportunities = opportunities
                .OrderByDescending(opportunity => opportunity.Score)
                .ThenBy(opportunity => opportunity.OpportunityId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static WebSearchOpportunity CreateWeakPageOpportunity(
        ObservationGroupKey key,
        ObservationAggregate aggregate,
        WebSearchOpportunityOptions options)
    {
        var volume = Math.Min(1d, Math.Log10(aggregate.Impressions + 1d) / 4d);
        var positionPotential = Math.Clamp(
            (options.WeakPageMaximumPosition + 1d - aggregate.AveragePosition!.Value) /
            (options.WeakPageMaximumPosition + 1d - options.WeakPageMinimumPosition),
            0d,
            1d);
        var score = Round(100d * ((0.65d * volume) + (0.35d * positionPotential)));

        return CreateOpportunity(
            WeakPageRule,
            key,
            aggregate,
            score,
            FormattableString.Invariant($"{aggregate.Impressions} impressions with weighted position {aggregate.AveragePosition.Value:F2} falls inside the configured weak-page range {options.WeakPageMinimumPosition:F2}-{options.WeakPageMaximumPosition:F2}."),
            "Review intent alignment, product proof, freshness, internal links, and competing pages before preparing a technical or content brief.");
    }

    private static WebSearchOpportunity CreateCtrOpportunity(
        ObservationGroupKey key,
        ObservationAggregate aggregate,
        WebSearchOpportunityOptions options)
    {
        var volume = Math.Min(1d, Math.Log10(aggregate.Impressions + 1d) / 4d);
        var gap = Math.Clamp(
            (options.MinimumClickThroughRate - aggregate.ClickThroughRate) / options.MinimumClickThroughRate,
            0d,
            1d);
        var score = Round(100d * ((0.6d * volume) + (0.4d * gap)));

        return CreateOpportunity(
            CtrRule,
            key,
            aggregate,
            score,
            FormattableString.Invariant($"{aggregate.Impressions} impressions at weighted position {aggregate.AveragePosition!.Value:F2} produced CTR {aggregate.ClickThroughRate:P2}, below the configured {options.MinimumClickThroughRate:P2} threshold."),
            "Inspect the query intent, title, description, rich-result eligibility, and SERP competitors before changing the page snippet or content.");
    }

    private static WebSearchOpportunity CreateOpportunity(
        string ruleId,
        ObservationGroupKey key,
        ObservationAggregate aggregate,
        double score,
        string explanation,
        string recommendation)
    {
        return new WebSearchOpportunity
        {
            OpportunityId = ComputeOpportunityId(ruleId, key, aggregate.FromDate, aggregate.ThroughDate),
            RuleId = ruleId,
            Provider = key.Provider,
            SiteId = key.SiteId,
            Page = key.Page,
            Query = key.Query,
            Country = key.Country,
            Device = key.Device,
            SearchType = key.SearchType,
            FromDate = aggregate.FromDate,
            ThroughDate = aggregate.ThroughDate,
            Clicks = aggregate.Clicks,
            Impressions = aggregate.Impressions,
            ClickThroughRate = Round(aggregate.ClickThroughRate, 6),
            AveragePosition = Round(aggregate.AveragePosition!.Value, 4),
            Score = score,
            Confidence = CalculateConfidence(aggregate),
            Explanation = explanation,
            Recommendation = recommendation,
            EvidenceObservationKeys = aggregate.EvidenceObservationKeys
        };
    }

    private static ObservationAggregate Aggregate(IEnumerable<WebSearchObservation> observations)
    {
        var rows = observations.OrderBy(observation => observation.ObservationKey, StringComparer.Ordinal).ToArray();
        var impressions = rows.Sum(observation => observation.Impressions);
        var clicks = rows.Sum(observation => observation.Clicks);
        var positionedRows = rows.Where(observation => observation.AveragePosition.HasValue).ToArray();
        var positionedImpressions = positionedRows.Sum(observation => observation.Impressions);
        double? position = positionedRows.Length == 0
            ? null
            : positionedImpressions > 0
                ? positionedRows.Sum(observation => observation.AveragePosition!.Value * observation.Impressions) /
                  positionedImpressions
                : positionedRows.Average(observation => observation.AveragePosition!.Value);

        return new ObservationAggregate(
            clicks,
            impressions,
            impressions == 0 ? 0d : (double)clicks / impressions,
            position,
            rows.Min(observation => observation.Date),
            rows.Max(observation => observation.Date),
            rows.Select(observation => observation.Date).Distinct().Count(),
            rows.Select(observation => observation.ObservationKey).ToArray());
    }

    private static double CalculateConfidence(ObservationAggregate aggregate)
    {
        var volume = Math.Min(1d, aggregate.Impressions / 1000d);
        var coverage = Math.Min(1d, aggregate.ObservedDayCount / 28d);
        return Round((0.7d * volume) + (0.3d * coverage), 4);
    }

    private static string ComputeOpportunityId(string ruleId, ObservationGroupKey key, DateOnly fromDate, DateOnly throughDate)
    {
        return WebSearchIdentityHasher.Compute(
            ruleId,
            key.Provider,
            key.SiteId,
            key.Page,
            key.Query ?? string.Empty,
            key.Country ?? string.Empty,
            key.Device ?? string.Empty,
            key.SearchType ?? string.Empty,
            fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            throughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static void ValidateOptions(WebSearchOpportunityOptions options)
    {
        if (options.MinimumImpressions < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumImpressions), "Minimum impressions must be positive.");
        if (options.WeakPageMinimumPosition < 0d || options.WeakPageMaximumPosition < options.WeakPageMinimumPosition)
            throw new ArgumentOutOfRangeException(nameof(options.WeakPageMaximumPosition), "Weak-page position range is invalid.");
        if (options.CtrMaximumPosition < 0d)
            throw new ArgumentOutOfRangeException(nameof(options.CtrMaximumPosition), "CTR maximum position cannot be negative.");
        if (options.MinimumClickThroughRate <= 0d || options.MinimumClickThroughRate > 1d)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumClickThroughRate), "Minimum CTR must be greater than zero and at most one.");
        if (options.FromDate.HasValue && options.ThroughDate.HasValue && options.FromDate > options.ThroughDate)
            throw new ArgumentException("Search opportunity from date cannot be after through date.", nameof(options));
    }

    private static string NormalizeRequired(string? value, string name) =>
        NormalizeOptional(value) ?? throw new ArgumentException("A site identifier is required.", name);

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static double Round(double value, int decimals = 2) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    private sealed record ObservationGroupKey(
        string Provider,
        string SiteId,
        string Page,
        string? Query,
        string? Country,
        string? Device,
        string? SearchType);

    private sealed record ObservationAggregate(
        long Clicks,
        long Impressions,
        double ClickThroughRate,
        double? AveragePosition,
        DateOnly FromDate,
        DateOnly ThroughDate,
        int ObservedDayCount,
        string[] EvidenceObservationKeys);
}
