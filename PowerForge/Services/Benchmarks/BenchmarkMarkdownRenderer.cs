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
        markdown.AppendLine("| Scenario | Variables | Operation | Host | OS | Engine | Samples | Median | Mean | Status |");
        markdown.AppendLine("| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var row in rows.OrderBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => FormatVariables(r.Variables), StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Operation, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Os, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Engine, StringComparer.OrdinalIgnoreCase))
        {
            markdown.AppendLine(
                $"| {Cell(row.Scenario)} | {Cell(FormatVariables(row.Variables))} | {Cell(row.Operation)} | {Cell(row.Host)} | {Cell(row.Os)} | {Cell(row.Engine)} | {row.SampleCount} | {Number(row.MedianMs)} | {Number(row.MeanMs)} | {Cell(row.Status)} |");
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
        markdown.AppendLine("| Scenario | Variables | Operation | Host | OS | Engine | Metric | Actual | Baseline | Ratio |");
        markdown.AppendLine("| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: |");
        foreach (var row in rows.OrderBy(r => r.Scenario, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(r => FormatVariables(r.Variables), StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Operation, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Os, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(r => r.Engine, StringComparer.OrdinalIgnoreCase))
        {
            markdown.AppendLine(
                $"| {Cell(row.Scenario)} | {Cell(FormatVariables(row.Variables))} | {Cell(row.Operation)} | {Cell(row.Host)} | {Cell(row.Os)} | {Cell(row.Engine)} | {Cell(row.Metric)} | {Number(row.Actual)} | {Number(row.Baseline)} | {Number(row.Ratio)} |");
        }

        return markdown.ToString().TrimEnd() + Environment.NewLine;
    }

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
