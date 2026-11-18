using System.Text;

namespace PowerForge;

/// <summary>
/// Builds evidence cells (case × operation × library × parameters) from a normalized result.
/// Failed and skipped cells are kept with their reason.
/// </summary>
internal static class BenchmarkEvidenceCellBuilder
{
    internal static BenchmarkEvidenceCell[] Build(
        BenchmarkRunResult result,
        string comparisonId,
        BenchmarkPublishPolicy policy)
    {
        BenchmarkSummaryRow[] rows = result.Summary.Length > 0
            ? result.Summary
            : new BenchmarkSummaryService().Summarize(result.Samples);
        var samplesByCell = result.Samples
            .Where(sample => sample.Status == BenchmarkSampleStatus.Succeeded)
            .GroupBy(sample => CellKey(sample.Scenario, sample.Operation, sample.Engine, sample.Library, sample.Host, sample.Variables), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(sample => sample.DurationMs).ToArray(), StringComparer.Ordinal);
        bool hasSkippedSamples = result.Samples.Any(sample => sample.Status == BenchmarkSampleStatus.Skipped);
        string statistic = string.IsNullOrWhiteSpace(result.Runner?.Statistic)
            ? "median"
            : result.Runner!.Statistic.Trim().ToLowerInvariant();

        var cells = new List<BenchmarkEvidenceCell>();
        foreach (BenchmarkSummaryRow row in rows)
        {
            samplesByCell.TryGetValue(
                CellKey(row.Scenario, row.Operation, row.Engine, row.Library, row.Host, row.Variables),
                out double[]? durations);
            double? value = statistic == "mean" ? row.MeanMs ?? row.MedianMs : row.MedianMs ?? row.MeanMs;
            BenchmarkConfidenceInterval? interval = row.ConfidenceInterval;
            if (statistic == "median" && durations is { Length: > 0 })
                interval = BenchmarkEvidenceIdentity.MedianConfidenceInterval(durations, policy.ConfidenceLevel) ?? interval;
            string status = string.IsNullOrWhiteSpace(row.Status)
                ? row.SampleCount > 0 ? "Succeeded" : hasSkippedSamples ? "Skipped" : "Failed"
                : row.Status;
            (string validation, string detail) = ResolveValidation(result.Validations, row.Library, row.Scenario);
            cells.Add(new BenchmarkEvidenceCell
            {
                ComparisonId = comparisonId,
                Case = row.Scenario,
                Operation = row.Operation,
                Library = row.Library ?? string.Empty,
                Parameters = new Dictionary<string, string?>(row.Variables, StringComparer.OrdinalIgnoreCase),
                Status = status,
                Reason = row.FailureReasons.Count == 0
                    ? string.Empty
                    : string.Join("; ", row.FailureReasons.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key)),
                Statistic = statistic,
                ValueMs = string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? value : null,
                ConfidenceInterval = string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? interval : null,
                SampleCount = row.SampleCount,
                FailureCount = row.FailureCount,
                Validation = validation,
                ValidationDetail = detail
            });
        }

        return cells
            .OrderBy(cell => cell.Case, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cell => cell.Operation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cell => FormatParameters(cell.Parameters), StringComparer.OrdinalIgnoreCase)
            .ThenBy(cell => cell.Library, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string FormatParameters(IReadOnlyDictionary<string, string?> parameters)
        => string.Join(";", parameters
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key.ToLowerInvariant() + "=" + (item.Value ?? string.Empty)));

    private static (string Status, string Detail) ResolveValidation(
        IEnumerable<BenchmarkOutputValidation> validations,
        string? library,
        string scenario)
    {
        BenchmarkOutputValidation[] matches = (validations ?? Array.Empty<BenchmarkOutputValidation>())
            .Where(validation =>
                string.Equals(validation.Library, library, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(validation.Case) ||
                 string.Equals(validation.Case, scenario, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matches.Length == 0)
            return ("missing", string.Empty);
        BenchmarkOutputValidation? failed = matches.FirstOrDefault(validation => validation.Status == BenchmarkValidationStatus.Failed);
        if (failed is not null)
            return ("failed", failed.Detail ?? string.Empty);
        BenchmarkOutputValidation specific = matches.FirstOrDefault(validation => !string.IsNullOrWhiteSpace(validation.Case)) ?? matches[0];
        return ("passed", specific.Detail ?? string.Empty);
    }

    private static string CellKey(
        string scenario,
        string operation,
        string engine,
        string? library,
        string host,
        IReadOnlyDictionary<string, string?> variables)
    {
        var builder = new StringBuilder();
        foreach (string? part in new[] { scenario, operation, engine, library, host, FormatParameters(variables) })
            builder.Append((part ?? string.Empty).Length).Append(':').Append(part).Append(';');
        return builder.ToString();
    }
}
