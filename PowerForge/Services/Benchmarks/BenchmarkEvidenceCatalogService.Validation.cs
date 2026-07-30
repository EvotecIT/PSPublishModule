using System.Text;

namespace PowerForge;

public sealed partial class BenchmarkEvidenceCatalogService
{
    private static bool IsValidComparison(BenchmarkComparisonRow row)
    {
        if (!IsFinite(row.Actual) ||
            !IsFinite(row.Baseline) ||
            double.IsNaN(row.TieTolerance) ||
            double.IsInfinity(row.TieTolerance) ||
            row.TieTolerance < 0)
        {
            return false;
        }

        double baseline = row.Baseline!.Value;
        if (Math.Abs(baseline) <= double.Epsilon)
            return !row.Ratio.HasValue;

        if (!IsFinite(row.Ratio))
            return false;

        return NearlyEqual(row.Ratio!.Value, row.Actual!.Value / baseline);
    }

    private static void ValidateSummaryStatistics(
        IReadOnlyCollection<BenchmarkSummaryRow> rows)
    {
        foreach (BenchmarkSummaryRow row in rows)
        {
            if (row.SampleCount < 0 ||
                row.FailureCount < 0 ||
                row.OutlierCount < 0)
            {
                throw new InvalidOperationException(
                    "Publishable benchmark summary counts cannot be negative.");
            }

            ValidateOptionalPositiveDuration(row, nameof(row.MedianMs), row.MedianMs);
            ValidateOptionalPositiveDuration(row, nameof(row.MeanMs), row.MeanMs);
            ValidateOptionalPositiveDuration(row, nameof(row.MinMs), row.MinMs);
            ValidateOptionalPositiveDuration(row, nameof(row.MaxMs), row.MaxMs);
            ValidateOptionalPositiveDuration(row, nameof(row.P95Ms), row.P95Ms);
            ValidateOptionalPositiveDuration(row, nameof(row.P99Ms), row.P99Ms);
            ValidateOptionalNonNegativeDuration(row, nameof(row.StdDevMs), row.StdDevMs);
            ValidateOptionalNonNegativeDuration(row, nameof(row.StdErrMs), row.StdErrMs);

            ValidateOrderedDuration(row, nameof(row.MinMs), row.MinMs, nameof(row.MaxMs), row.MaxMs);
            ValidateOrderedDuration(row, nameof(row.MinMs), row.MinMs, nameof(row.MedianMs), row.MedianMs);
            ValidateOrderedDuration(row, nameof(row.MedianMs), row.MedianMs, nameof(row.MaxMs), row.MaxMs);
            ValidateOrderedDuration(row, nameof(row.MinMs), row.MinMs, nameof(row.MeanMs), row.MeanMs);
            ValidateOrderedDuration(row, nameof(row.MeanMs), row.MeanMs, nameof(row.MaxMs), row.MaxMs);
            ValidateOrderedDuration(row, nameof(row.MinMs), row.MinMs, nameof(row.P95Ms), row.P95Ms);
            ValidateOrderedDuration(row, nameof(row.P95Ms), row.P95Ms, nameof(row.MaxMs), row.MaxMs);
            ValidateOrderedDuration(row, nameof(row.MinMs), row.MinMs, nameof(row.P99Ms), row.P99Ms);
            ValidateOrderedDuration(row, nameof(row.P99Ms), row.P99Ms, nameof(row.MaxMs), row.MaxMs);
            ValidateOrderedDuration(row, nameof(row.P95Ms), row.P95Ms, nameof(row.P99Ms), row.P99Ms);
            ValidateOrderedDuration(row, nameof(row.StdErrMs), row.StdErrMs, nameof(row.StdDevMs), row.StdDevMs);

            if (row.FailureReasons.Any(item => item.Value < 0) ||
                row.Metrics.Any(item =>
                    double.IsNaN(item.Value) ||
                    double.IsInfinity(item.Value)))
            {
                throw new InvalidOperationException(
                    "Publishable benchmark summary failure counts and custom metrics must be finite and non-negative where applicable.");
            }
        }
    }

    private static void ValidateOptionalPositiveDuration(
        BenchmarkSummaryRow row,
        string name,
        double? value)
    {
        if (value.HasValue && !IsValidDuration(value.Value))
        {
            throw new InvalidOperationException(
                $"Publishable benchmark summary '{CreateSummaryIdentity(row)}' has invalid {name} value {value.Value}.");
        }
    }

    private static void ValidateOptionalNonNegativeDuration(
        BenchmarkSummaryRow row,
        string name,
        double? value)
    {
        if (value.HasValue &&
            (value.Value < 0 ||
             double.IsNaN(value.Value) ||
             double.IsInfinity(value.Value)))
        {
            throw new InvalidOperationException(
                $"Publishable benchmark summary '{CreateSummaryIdentity(row)}' has invalid {name} value {value.Value}.");
        }
    }

    private static void ValidateOrderedDuration(
        BenchmarkSummaryRow row,
        string lowerName,
        double? lower,
        string upperName,
        double? upper)
    {
        if (lower.HasValue && upper.HasValue && lower.Value > upper.Value)
        {
            throw new InvalidOperationException(
                $"Publishable benchmark summary '{CreateSummaryIdentity(row)}' requires {lowerName} <= {upperName}.");
        }
    }

    private static void ValidateSummariesMatchSamples(BenchmarkRunResult result)
    {
        if (result.Samples.Length == 0 || result.Summary.Length == 0)
            return;

        PowerShellBenchmarkOutlierMode outlierMode = ResolveOutlierMode(result.Metadata);
        BenchmarkSummaryRow[] expected = new BenchmarkSummaryService()
            .Summarize(result.Samples, outlierMode);
        var expectedByKey = expected.ToDictionary(CreateSummaryIdentity, StringComparer.Ordinal);
        var actualByKey = result.Summary.ToDictionary(CreateSummaryIdentity, StringComparer.Ordinal);

        bool matches = expectedByKey.Count == actualByKey.Count &&
                       expectedByKey.All(item =>
                           actualByKey.TryGetValue(item.Key, out BenchmarkSummaryRow? actual) &&
                           SummariesMatch(item.Value, actual));
        if (!matches)
        {
            throw new InvalidOperationException(
                "Publishable benchmark summaries must match aggregates recomputed from the raw samples.");
        }
    }

    private static void ValidateComparisonsMatchSummaries(BenchmarkRunResult result)
    {
        if (result.Comparison.Length == 0)
            return;
        if (result.Summary.Length == 0)
        {
            throw new InvalidOperationException(
                "Publishable benchmark comparisons require the summary rows from which they were computed.");
        }

        var expectedRows = new List<BenchmarkComparisonRow>();
        var comparer = new BenchmarkSummaryService();
        foreach (var definition in result.Comparison
                     .Select(row => new
                     {
                         Baseline = row.BaselineEngine,
                         Metric = row.Metric,
                         row.TieTolerance
                     })
                     .GroupBy(
                         value => string.Join(
                             "\u001f",
                             value.Baseline,
                             value.Metric,
                             value.TieTolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
                         StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            try
            {
                expectedRows.AddRange(comparer.Compare(
                    result.Summary,
                    definition.Baseline,
                    definition.Metric,
                    definition.TieTolerance));
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "Publishable benchmark comparisons must be reproducible from their summary rows.",
                    exception);
            }
        }

        var expectedByKey = expectedRows.ToDictionary(
            CreateComparisonIdentity,
            StringComparer.Ordinal);
        var actualByKey = result.Comparison.ToDictionary(
            CreateComparisonIdentity,
            StringComparer.Ordinal);
        bool matches = expectedByKey.Count == actualByKey.Count &&
                       expectedByKey.All(item =>
                           actualByKey.TryGetValue(item.Key, out BenchmarkComparisonRow? actual) &&
                           ComparisonsMatch(item.Value, actual));
        if (!matches)
        {
            throw new InvalidOperationException(
                "Publishable benchmark comparisons must match values recomputed from the validated summary rows.");
        }
    }

    private static string CreateComparisonIdentity(BenchmarkComparisonRow row)
    {
        var builder = new StringBuilder();
        AppendIdentityPart(builder, "suite", row.Suite);
        AppendIdentityPart(builder, "scenario", row.Scenario);
        AppendIdentityPart(builder, "operation", row.Operation);
        AppendIdentityPart(builder, "host", row.Host);
        AppendIdentityPart(builder, "os", row.Os);
        AppendIdentityPart(builder, "runMode", row.RunMode);
        AppendIdentityPart(builder, "engine", row.Engine);
        AppendIdentityPart(builder, "baselineEngine", row.BaselineEngine);
        AppendIdentityPart(builder, "metric", row.Metric);
        AppendIdentityPart(
            builder,
            "tieTolerance",
            row.TieTolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var variable in row.Variables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            AppendIdentityPart(builder, variable.Key.ToUpperInvariant(), variable.Value);
        return builder.ToString();
    }

    private static bool ComparisonsMatch(
        BenchmarkComparisonRow expected,
        BenchmarkComparisonRow actual)
        => string.Equals(expected.Status, actual.Status, StringComparison.OrdinalIgnoreCase) &&
           NearlyEqual(expected.Actual, actual.Actual) &&
           NearlyEqual(expected.Baseline, actual.Baseline) &&
           NearlyEqual(expected.Ratio, actual.Ratio);

    private static PowerShellBenchmarkOutlierMode ResolveOutlierMode(
        IReadOnlyDictionary<string, string> metadata)
    {
        string? value = MetadataValue(
            metadata,
            "outlierMode",
            "benchmark.outlierMode",
            "benchmark.execution.outlierMode");
        if (string.IsNullOrWhiteSpace(value))
            return PowerShellBenchmarkOutlierMode.None;
        if (Enum.TryParse(value, ignoreCase: true, out PowerShellBenchmarkOutlierMode mode) &&
            Enum.IsDefined(typeof(PowerShellBenchmarkOutlierMode), mode))
        {
            return mode;
        }

        throw new InvalidOperationException(
            $"Publishable benchmark evidence declares unsupported outlier mode '{value}'.");
    }

    private static string CreateSummaryIdentity(BenchmarkSummaryRow row)
    {
        var builder = new StringBuilder();
        AppendIdentityPart(builder, "suite", row.Suite);
        AppendIdentityPart(builder, "scenario", row.Scenario);
        AppendIdentityPart(builder, "operation", row.Operation);
        AppendIdentityPart(builder, "engine", row.Engine);
        AppendIdentityPart(builder, "host", row.Host);
        AppendIdentityPart(builder, "os", row.Os);
        AppendIdentityPart(builder, "runMode", row.RunMode);
        foreach (var variable in row.Variables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            AppendIdentityPart(builder, variable.Key.ToUpperInvariant(), variable.Value);
        return builder.ToString();
    }

    private static bool SummariesMatch(BenchmarkSummaryRow expected, BenchmarkSummaryRow actual)
        => expected.SampleCount == actual.SampleCount &&
           expected.FailureCount == actual.FailureCount &&
           expected.OutlierCount == actual.OutlierCount &&
           string.Equals(expected.Status, actual.Status, StringComparison.OrdinalIgnoreCase) &&
           NearlyEqual(expected.MedianMs, actual.MedianMs) &&
           NearlyEqual(expected.MeanMs, actual.MeanMs) &&
           NearlyEqual(expected.MinMs, actual.MinMs) &&
           NearlyEqual(expected.MaxMs, actual.MaxMs) &&
           NearlyEqual(expected.P95Ms, actual.P95Ms) &&
           NearlyEqual(expected.P99Ms, actual.P99Ms) &&
           NearlyEqual(expected.StdDevMs, actual.StdDevMs) &&
           NearlyEqual(expected.StdErrMs, actual.StdErrMs) &&
           DictionariesMatch(expected.FailureReasons, actual.FailureReasons) &&
           DictionariesMatch(expected.Metrics, actual.Metrics);

    private static bool DictionariesMatch(
        IReadOnlyDictionary<string, int> expected,
        IReadOnlyDictionary<string, int> actual)
        => expected.Count == actual.Count &&
           expected.All(item =>
               TryGetValueIgnoreCase(actual, item.Key, out int value) &&
               value == item.Value);

    private static bool DictionariesMatch(
        IReadOnlyDictionary<string, double> expected,
        IReadOnlyDictionary<string, double> actual)
        => expected.Count == actual.Count &&
           expected.All(item =>
               TryGetValueIgnoreCase(actual, item.Key, out double value) &&
               NearlyEqual(item.Value, value));

    private static bool IsFinite(double? value)
        => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);

    private static bool NearlyEqual(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
            return left.HasValue == right.HasValue;
        return NearlyEqual(left.Value, right.Value);
    }

    private static bool NearlyEqual(double left, double right)
    {
        if (left.Equals(right))
            return true;
        double scale = Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
        return Math.Abs(left - right) <= scale * 1e-12;
    }

    private static bool TryGetValueIgnoreCase<T>(
        IReadOnlyDictionary<string, T> values,
        string key,
        out T value)
    {
        foreach (var item in values)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;

            value = item.Value;
            return true;
        }

        value = default!;
        return false;
    }
}
