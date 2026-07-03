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
        var baseline = rows.Select(r => r.BaselineEngine).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "Baseline";
        var engines = rows
            .Select(r => r.Engine)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(engine => string.Equals(engine, baseline, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(engine => engine, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        markdown.Append("| Scenario | Host | Operation |");
        foreach (var engine in engines)
            markdown.Append(' ').Append(Cell(engine)).Append(" |");
        markdown.AppendLine(" Result |");

        markdown.Append("| --- | --- | --- |");
        foreach (var _ in engines)
            markdown.Append(" ---: |");
        markdown.AppendLine(" --- |");

        foreach (var group in rows
                     .GroupBy(r => string.Join("\u001f", r.Scenario, FormatVariables(r.Variables), r.Operation, r.Host, r.Os, r.RunMode), StringComparer.Ordinal)
                     .OrderBy(g => DisplayScenario(g.First()), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.First().Operation, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.First().Host, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var byEngine = group
                .GroupBy(r => r.Engine, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            markdown.Append("| ")
                .Append(Cell(DisplayScenario(first)))
                .Append(" | ")
                .Append(Cell(first.Host))
                .Append(" | ")
                .Append(Cell(first.Operation))
                .Append(" |");
            foreach (var engine in engines)
            {
                byEngine.TryGetValue(engine, out var row);
                markdown.Append(' ').Append(Cell(FormatComparisonValue(row))).Append(" |");
            }
            markdown.AppendLine(
                $" {Cell(FormatComparisonResult(baseline, group))} |");
        }

        return markdown.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string DisplayScenario(BenchmarkComparisonRow row)
        => row.Variables.TryGetValue("ModuleName", out var moduleName) && !string.IsNullOrWhiteSpace(moduleName)
            ? moduleName!
            : row.Scenario;

    private static string FormatComparisonValue(BenchmarkComparisonRow? row)
    {
        if (row?.Actual is null)
            return "Failed";
        var ratio = row.Ratio.HasValue ? row.Ratio.Value.ToString("0.00", CultureInfo.InvariantCulture) + "x" : "n/a";
        return $"{ratio} ({FormatDuration(row.Actual.Value)})";
    }

    private static string FormatComparisonResult(string baseline, IEnumerable<BenchmarkComparisonRow> rows)
    {
        var successful = rows
            .Where(static row => row.Actual.HasValue)
            .OrderBy(static row => row.Actual!.Value)
            .ToArray();
        if (successful.Length == 0)
            return "No successful rows";

        var fastest = successful[0].Engine;
        var baselineSucceeded = successful.Any(row => string.Equals(row.Engine, baseline, StringComparison.OrdinalIgnoreCase));
        if (!baselineSucceeded)
            return $"{baseline} failed";
        if (successful.Length == 1)
            return $"{baseline} only successful";
        if (string.Equals(fastest, baseline, StringComparison.OrdinalIgnoreCase))
            return $"{baseline} fastest";

        return $"{baseline} slower than {fastest}";
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
}
