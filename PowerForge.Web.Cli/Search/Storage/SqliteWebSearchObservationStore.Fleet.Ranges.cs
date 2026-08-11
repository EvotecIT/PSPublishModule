using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal sealed partial class SqliteWebSearchObservationStore
{
    private static WebSearchFleetCompletedRange[] MergeDates(IEnumerable<DateOnly> dates) => MergeRanges(
        dates.Distinct().Select(value => new WebSearchFleetCompletedRange { FromDate = value, ThroughDate = value }));

    private static WebSearchFleetCompletedRange[] CombineSearchDimensionCoverage(IEnumerable<FleetRun> values)
    {
        var runs = values.ToArray();
        var pageRanges = MergeRanges(runs
            .SelectMany(value => value.DimensionCoverage)
            .Where(value => value.Scope == "page")
            .SelectMany(value => value.CompletedRanges));
        var queryRanges = MergeRanges(runs
            .SelectMany(value => value.DimensionCoverage)
            .Where(value => value.Scope == "query")
            .SelectMany(value => value.CompletedRanges));
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

    private static IEnumerable<string> ExpandDimensionScope(string scope) => scope switch
    {
        "page-query" => ["page", "query"],
        _ => [scope]
    };

    private static bool SearchSnapshotCoversFleetCapability(SearchFleetRunMetadata run, IReadOnlyCollection<string> scopes)
    {
        if (!string.Equals(run.CoverageMode, "snapshot", StringComparison.Ordinal))
            return true;
        var dimensionScopes = scopes.ToHashSet(StringComparer.Ordinal);
        return dimensionScopes.Count == 0 || dimensionScopes.Contains("page") && dimensionScopes.Contains("query");
    }
}
