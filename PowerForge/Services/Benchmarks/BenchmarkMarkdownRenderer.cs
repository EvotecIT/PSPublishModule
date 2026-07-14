using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>
/// Renders normalized benchmark summaries as Markdown.
/// </summary>
public sealed class BenchmarkMarkdownRenderer
{
    /// <summary>
    /// Renders a compact comparison table.
    /// </summary>
    /// <param name="summary">Summary rows.</param>
    /// <returns>Markdown table.</returns>
    public string RenderSummaryTable(IEnumerable<BenchmarkSummaryRow> summary)
    {
        var rows = (summary ?? Array.Empty<BenchmarkSummaryRow>()).ToArray();
        var markdown = new StringBuilder();
        markdown.AppendLine("| Scenario | Variables | Operation | Host | OS | RunMode | Engine | Samples | Failures | Median | Mean | P95 | StdDev | Status |");
        markdown.AppendLine("| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var row in rows.OrderBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => FormatVariables(r.Variables), StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Operation, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Os, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.RunMode, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Engine, StringComparer.OrdinalIgnoreCase))
        {
            markdown.AppendLine(
                $"| {Cell(row.Scenario)} | {Cell(FormatVariables(row.Variables))} | {Cell(row.Operation)} | {Cell(row.Host)} | {Cell(row.Os)} | {Cell(row.RunMode)} | {Cell(row.Engine)} | {row.SampleCount} | {row.FailureCount} | {Number(row.MedianMs)} | {Number(row.MeanMs)} | {Number(row.P95Ms)} | {Number(row.StdDevMs)} | {Cell(row.Status)} |");
        }

        return markdown.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Renders comparison rows as Markdown.
    /// </summary>
    /// <param name="comparison">Comparison rows.</param>
    /// <returns>Markdown table.</returns>
    public string RenderComparisonTable(IEnumerable<BenchmarkComparisonRow> comparison)
    {
        var rows = (comparison ?? Array.Empty<BenchmarkComparisonRow>()).ToArray();
        var markdown = new StringBuilder();
        var baselines = rows
            .Select(r => r.BaselineEngine)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primaryBaseline = baselines.FirstOrDefault() ?? "Baseline";
        var includeBaseline = baselines.Length > 1;
        var includeVariables = rows.Any(static row => !string.IsNullOrWhiteSpace(FormatComparisonVariables(row.Variables)));
        var includeOs = rows.Select(static row => row.Os ?? string.Empty).Where(static value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
        var includeRunMode = rows.Select(static row => row.RunMode ?? string.Empty).Where(static value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
        var includeMetric = rows
            .Select(static row => string.IsNullOrWhiteSpace(row.Metric) ? "MedianMs" : row.Metric)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1
            || rows.Any(static row => !IsDefaultDurationMetric(row.Metric));
        var engines = rows
            .Select(r => r.Engine)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(engine => !includeBaseline && string.Equals(engine, primaryBaseline, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(engine => engine, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        markdown.Append("| Scenario |");
        if (includeVariables) markdown.Append(" Variables |");
        markdown.Append(" Host |");
        if (includeOs) markdown.Append(" OS |");
        if (includeRunMode) markdown.Append(" RunMode |");
        markdown.Append(" Operation |");
        if (includeMetric) markdown.Append(" Metric |");
        if (includeBaseline) markdown.Append(" Baseline |");
        foreach (var engine in engines)
            markdown.Append(' ').Append(Cell(engine)).Append(" |");
        markdown.AppendLine(" Result |");

        markdown.Append("| --- |");
        if (includeVariables) markdown.Append(" --- |");
        markdown.Append(" --- |");
        if (includeOs) markdown.Append(" --- |");
        if (includeRunMode) markdown.Append(" --- |");
        markdown.Append(" --- |");
        if (includeMetric) markdown.Append(" --- |");
        if (includeBaseline) markdown.Append(" --- |");
        foreach (var _ in engines)
            markdown.Append(" ---: |");
        markdown.AppendLine(" --- |");

        foreach (var group in rows
                     .GroupBy(r => string.Join("\u001f", r.Scenario, FormatVariables(r.Variables), r.Operation, r.Host, r.Os, r.RunMode, r.Metric, r.BaselineEngine, r.TieTolerance.ToString("G17", CultureInfo.InvariantCulture)), StringComparer.Ordinal)
                     .OrderBy(g => DisplayScenario(g.First()), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.First().Operation, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.First().Host, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.First().BaselineEngine, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.First().Metric, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var baseline = string.IsNullOrWhiteSpace(first.BaselineEngine) ? primaryBaseline : first.BaselineEngine;
            var byEngine = group
                .GroupBy(r => r.Engine, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            markdown.Append("| ")
                .Append(Cell(DisplayScenario(first)))
                .Append(" |");
            if (includeVariables)
                markdown.Append(' ').Append(Cell(FormatComparisonVariables(first.Variables))).Append(" |");
            markdown.Append(' ').Append(Cell(first.Host)).Append(" |");
            if (includeOs)
                markdown.Append(' ').Append(Cell(first.Os)).Append(" |");
            if (includeRunMode)
                markdown.Append(' ').Append(Cell(first.RunMode)).Append(" |");
            markdown.Append(' ').Append(Cell(first.Operation)).Append(" |");
            if (includeMetric)
                markdown.Append(' ').Append(Cell(string.IsNullOrWhiteSpace(first.Metric) ? "MedianMs" : first.Metric)).Append(" |");
            if (includeBaseline)
                markdown.Append(' ').Append(Cell(baseline)).Append(" |");
            foreach (var engine in engines)
            {
                byEngine.TryGetValue(engine, out var row);
                markdown.Append(' ').Append(Cell(FormatComparisonValue(row))).Append(" |");
            }
            markdown.AppendLine(
                $" {Cell(FormatComparisonResult(baseline, first.Metric, group))} |");
        }

        return markdown.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string DisplayScenario(BenchmarkComparisonRow row)
        => row.Scenario;

    private static string FormatComparisonValue(BenchmarkComparisonRow? row)
    {
        if (row is null)
            return "n/a";
        if (row.Actual is null)
        {
            if (string.Equals(row.Status, "Skipped", StringComparison.OrdinalIgnoreCase))
                return "Skipped";
            return "Failed";
        }

        var ratio = row.Ratio.HasValue ? row.Ratio.Value.ToString("0.00", CultureInfo.InvariantCulture) + "x" : "n/a";
        var value = IsDurationMetric(row.Metric)
            ? FormatDuration(row.Actual.Value)
            : Number(row.Actual.Value);
        return $"{ratio} ({value})";
    }

    private static string FormatComparisonResult(string baseline, string? metric, IEnumerable<BenchmarkComparisonRow> rows)
    {
        if (!IsDurationMetric(metric))
            return $"{baseline} baseline";

        var successful = rows
            .Where(static row => row.Actual.HasValue)
            .OrderBy(static row => row.Actual!.Value)
            .ToArray();
        if (successful.Length == 0)
            return "No successful rows";

        var fastest = successful[0];
        var baselineSucceeded = successful.Any(row => string.Equals(row.Engine, baseline, StringComparison.OrdinalIgnoreCase));
        if (!baselineSucceeded)
            return $"{baseline} failed";
        if (successful.Length == 1)
            return $"{baseline} only successful";

        var tieTolerance = Math.Max(0, successful[0].TieTolerance);
        if (tieTolerance > 0)
        {
            var tiedEngines = successful
                .Where(row => row.Actual!.Value <= fastest.Actual!.Value * (1 + tieTolerance))
                .Select(row => row.Engine)
                .ToArray();
            if (tiedEngines.Any(engine => string.Equals(engine, baseline, StringComparison.OrdinalIgnoreCase)) && tiedEngines.Length > 1)
            {
                var peers = tiedEngines.Where(engine => !string.Equals(engine, baseline, StringComparison.OrdinalIgnoreCase));
                return $"{baseline} tied with {string.Join(", ", peers)}";
            }
        }

        if (string.Equals(fastest.Engine, baseline, StringComparison.OrdinalIgnoreCase))
            return $"{baseline} fastest";

        return $"{baseline} slower than {fastest.Engine}";
    }

    private static bool IsDefaultDurationMetric(string? metric)
        => string.IsNullOrWhiteSpace(metric) || string.Equals(metric, "MedianMs", StringComparison.OrdinalIgnoreCase);

    private static bool IsDurationMetric(string? metric)
    {
        var name = string.IsNullOrWhiteSpace(metric) ? "MedianMs" : metric!.Trim();
        return name.EndsWith("Ms", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "P95", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "P99", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "StdDev", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "StdErr", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDuration(double milliseconds)
        => milliseconds >= 1000
            ? (milliseconds / 1000).ToString("0.00", CultureInfo.InvariantCulture) + "s"
            : milliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms";

    private static string Cell(string? value)
        => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Number(double? value)
        => value.HasValue ? value.Value.ToString("G15", CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatVariables(IReadOnlyDictionary<string, string?> variables)
        => string.Join(
            ", ",
            (variables ?? new Dictionary<string, string?>())
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(k.Key, "=", k.Value ?? string.Empty)));

    private static string FormatComparisonVariables(IReadOnlyDictionary<string, string?> variables)
        => string.Join(
            ", ",
            (variables ?? new Dictionary<string, string?>())
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(k.Key, "=", k.Value ?? string.Empty)));
}
