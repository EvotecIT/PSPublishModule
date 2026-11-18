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
            // The statistic the runner declared is required; a missing one is never replaced by the other.
            // Without a declared statistic the result has no runner block and cannot be published anyway.
            bool declared = !string.IsNullOrWhiteSpace(result.Runner?.Statistic);
            double? value = statistic == "mean"
                ? row.MeanMs ?? (declared ? null : row.MedianMs)
                : row.MedianMs ?? (declared ? null : row.MeanMs);
            // With raw samples the interval is computed from the same samples the statistic used (after the summary's
            // outlier policy); a producer's interval is used only for summary-only results.
            BenchmarkConfidenceInterval? interval = row.ConfidenceInterval;
            if (durations is { Length: > 0 })
            {
                double[] used = TrimOutliers(durations, row.OutlierCount);
                interval = statistic == "mean"
                    ? BenchmarkEvidenceIdentity.MeanConfidenceInterval(used, policy.ConfidenceLevel)
                    : BenchmarkEvidenceIdentity.MedianConfidenceInterval(used, policy.ConfidenceLevel);
            }
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
                Parameters = CellParameters(row),
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

        string[] duplicates = cells
            .GroupBy(Identity, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.First().Library} / {group.First().Case} / {group.First().Operation}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                "Several benchmark rows resolve to the same cell (" + string.Join(", ", duplicates) +
                "). Give each method of a library its own case with [BenchmarkCategory(\"case:<name>\")] or a case map.");
        }

        return cells
            .OrderBy(cell => cell.Case, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cell => cell.Operation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cell => FormatParameters(cell.Parameters), StringComparer.OrdinalIgnoreCase)
            .ThenBy(cell => cell.Library, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Producer identity keys: how a row was produced (BenchmarkDotNet method, type, namespace, full name,
    /// categories, report) rather than what was measured. A workload parameter with one of these names keeps
    /// its name and the importer stores the producer value under <c>BenchmarkDotNet&lt;Name&gt;</c>, so the plain
    /// name is producer identity only when that prefixed copy is absent.
    /// </summary>
    private static readonly string[] ProducerIdentityNames = { "Method", "FullName", "Type", "Namespace", "Categories" };

    internal static bool IsProducerIdentity(string key, IReadOnlyDictionary<string, string?> variables)
    {
        if (key.StartsWith("BenchmarkDotNet", StringComparison.OrdinalIgnoreCase))
            return true;
        return ProducerIdentityNames.Contains(key, StringComparer.OrdinalIgnoreCase) &&
               !variables.Keys.Any(other => string.Equals(other, "BenchmarkDotNet" + key, StringComparison.OrdinalIgnoreCase));
    }

    internal static Dictionary<string, string?> CellParameters(IReadOnlyDictionary<string, string?> variables)
    {
        var source = variables ?? new Dictionary<string, string?>();
        var parameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> item in source)
        {
            if (!IsProducerIdentity(item.Key, source))
                parameters[item.Key] = item.Value;
        }
        return parameters;
    }

    /// <summary>
    /// Cell parameters of a summary row: its workload variables plus the host it ran on, when the runner recorded
    /// one (an Invoke-BenchmarkSuite host axis), so rows that differ only by host stay separate cells.
    /// </summary>
    internal static Dictionary<string, string?> CellParameters(BenchmarkSummaryRow row)
    {
        Dictionary<string, string?> parameters = CellParameters(row.Variables);
        if (!string.IsNullOrWhiteSpace(row.Host) && !parameters.ContainsKey("Host"))
            parameters["Host"] = row.Host;
        return parameters;
    }

    private static double[] TrimOutliers(double[] durations, int outlierCount)
    {
        if (outlierCount <= 0 || durations.Length <= outlierCount)
            return durations;
        int each = outlierCount / 2;
        return durations.OrderBy(value => value).Skip(each).Take(durations.Length - outlierCount).ToArray();
    }

    /// <summary>Identity of the cell a summary row produces: case, operation, library and parameters.</summary>
    internal static string Identity(BenchmarkSummaryRow row)
        => string.Join("\u001f", Fold(row.Scenario), Fold(row.Operation), Fold(row.Library), FormatParameters(CellParameters(row)));

    internal static string Identity(BenchmarkEvidenceCell cell)
        => string.Join("\u001f", Fold(cell.Case), Fold(cell.Operation), Fold(cell.Library), FormatParameters(cell.Parameters));

    // Case, operation and library names ignore case; parameter values are compared exactly.
    private static string Fold(string? value) => (value ?? string.Empty).ToLowerInvariant();

    /// <summary>Identity of the workload a cell measures, shared by every library in the comparison.</summary>
    internal static string WorkloadIdentity(BenchmarkEvidenceCell cell)
        => string.Join("\u001f", Fold(cell.Case), Fold(cell.Operation), FormatParameters(cell.Parameters));

    // Length-prefixed so a value containing ';' or '=' can never read as another parameter.
    internal static string FormatParameters(IReadOnlyDictionary<string, string?> parameters)
        => string.Concat(parameters
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                string key = item.Key.ToLowerInvariant();
                string value = item.Value ?? string.Empty;
                return key.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + key + "=" +
                       value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value + ";";
            }));

    private static (string Status, string Detail) ResolveValidation(
        IEnumerable<BenchmarkOutputValidation> validations,
        string? library,
        string scenario)
    {
        BenchmarkOutputValidation[] matches = (validations ?? Array.Empty<BenchmarkOutputValidation>())
            .Where(validation => validation.Status is not null)
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
