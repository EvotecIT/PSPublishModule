using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Lane identity and statistics shared by the evidence catalog, the publish policy and declaration checks.
/// </summary>
public static class BenchmarkEvidenceIdentity
{
    private static readonly Regex FrameworkSuffix = new(
        @"-(net\d+(\.\d+)?(-[a-z]+)?|netcoreapp\d+\.\d+|netstandard\d+\.\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AffinitySegment = new(
        @"-affinity-",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetFramework = new(
        @"^(net\d+(\.\d+)?(-[a-z]+(\d+(\.\d+)*)?)?|netcoreapp\d+\.\d+|netstandard\d+\.\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DotNetRuntime = new(
        @"^\.NET (?<major>\d+)\.(?<minor>\d+)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns why a comparison identifier is not a clean schema 5 identifier, or null when it is.
    /// Framework and affinity are lane dimensions and never part of the identifier.
    /// </summary>
    /// <param name="comparisonId">Comparison identifier.</param>
    /// <returns>Problem description, or null.</returns>
    public static string? DescribeComparisonIdProblem(string? comparisonId)
    {
        if (string.IsNullOrWhiteSpace(comparisonId))
            return "Comparison identifier is required.";
        string value = comparisonId!.Trim();
        if (FrameworkSuffix.IsMatch(value))
            return $"Comparison identifier '{value}' ends with a target framework. Framework is a lane dimension: record it with the framework argument, not in the identifier.";
        if (AffinitySegment.IsMatch(value))
            return $"Comparison identifier '{value}' encodes processor affinity. Affinity is a lane parameter: record it as the 'affinity' lane parameter, not in the identifier.";
        if (value.Any(char.IsWhiteSpace) || value.IndexOf('/') >= 0)
            return $"Comparison identifier '{value}' cannot contain whitespace or '/'.";
        return null;
    }

    /// <summary>True when the value is a target framework moniker such as net10.0 or net472.</summary>
    /// <param name="value">Candidate value.</param>
    /// <returns>True for a target framework moniker.</returns>
    public static bool IsTargetFramework(string? value)
        => !string.IsNullOrWhiteSpace(value) && TargetFramework.IsMatch(value!.Trim());

    /// <summary>
    /// Resolves the lane framework from an explicit value, the <c>benchmark.workload.framework</c> metadata,
    /// or a modern .NET runtime description such as ".NET 10.0.2".
    /// </summary>
    /// <param name="result">Benchmark result.</param>
    /// <param name="explicitFramework">Framework supplied by the caller.</param>
    /// <returns>Normalized framework, or empty when none can be determined.</returns>
    public static string ResolveFramework(BenchmarkRunResult result, string? explicitFramework)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        string recorded = RecordedFramework(result);
        if (string.IsNullOrWhiteSpace(explicitFramework))
            return recorded;

        string requested = explicitFramework!.Trim();
        if (!IsTargetFramework(requested))
            throw new InvalidOperationException($"Framework '{requested}' is not a target framework moniker such as net10.0.");
        requested = requested.ToLowerInvariant();
        if (ConflictsWithRecordedFramework(result, requested))
        {
            throw new InvalidOperationException(
                $"Framework '{requested}' conflicts with the framework the result records ('{recorded}', from benchmark.workload.framework or the measured runtime).");
        }
        return requested;
    }

    /// <summary>
    /// Framework the result itself records: the provenance-bound <c>benchmark.workload.framework</c> metadata, or a
    /// modern .NET runtime description. Empty when the result records none.
    /// </summary>
    /// <param name="result">Benchmark result.</param>
    /// <returns>Normalized framework, or empty.</returns>
    public static string RecordedFramework(BenchmarkRunResult result) => Recorded(result).Framework;

    /// <summary>
    /// True when a framework contradicts what the result records. Bound workload metadata must match exactly; a
    /// runtime only establishes netX.Y, so a platform suffix such as net8.0-windows does not contradict it.
    /// </summary>
    /// <param name="result">Benchmark result.</param>
    /// <param name="framework">Framework claimed for the lane.</param>
    /// <returns>True when the framework conflicts.</returns>
    public static bool ConflictsWithRecordedFramework(BenchmarkRunResult result, string? framework)
    {
        (string recorded, bool fromMetadata) = Recorded(result);
        if (recorded.Length == 0)
            return false;
        string claimed = (framework ?? string.Empty).Trim().ToLowerInvariant();
        if (fromMetadata)
            return !string.Equals(recorded, claimed, StringComparison.OrdinalIgnoreCase);
        int suffix = claimed.IndexOf('-');
        string claimedBase = suffix >= 0 ? claimed.Substring(0, suffix) : claimed;
        return !string.Equals(recorded, claimedBase, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Framework, bool FromMetadata) Recorded(BenchmarkRunResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (result.Metadata.TryGetValue("benchmark.workload.framework", out string? metadataFramework) &&
            !string.IsNullOrWhiteSpace(metadataFramework))
        {
            string declared = metadataFramework.Trim();
            if (!IsTargetFramework(declared))
                throw new InvalidOperationException($"Framework '{declared}' is not a target framework moniker such as net10.0.");
            return (declared.ToLowerInvariant(), true);
        }

        Match match = DotNetRuntime.Match(result.Environment.RuntimeVersion ?? string.Empty);
        if (match.Success && int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture) >= 5)
            return ($"net{match.Groups["major"].Value}.{match.Groups["minor"].Value}", false);
        return (string.Empty, false);
    }

    /// <summary>Builds the canonical lane key.</summary>
    /// <param name="comparisonId">Comparison identifier.</param>
    /// <param name="platform">Platform.</param>
    /// <param name="framework">Framework.</param>
    /// <param name="profile">Profile.</param>
    /// <param name="parameters">Lane parameters.</param>
    /// <returns>Lane key such as <c>pdf-merge/windows/net10.0/full/affinity=0xffff0000</c>.</returns>
    public static string CreateLaneKey(
        string comparisonId,
        string platform,
        string framework,
        BenchmarkEvidenceProfile profile,
        IReadOnlyDictionary<string, string>? parameters)
    {
        var parts = new List<string>
        {
            comparisonId.Trim(),
            platform.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(framework) ? "-" : framework.Trim().ToLowerInvariant(),
            BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>.Format(profile)
        };
        if (parameters is not null)
        {
            parts.AddRange(parameters
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Key.Trim().ToLowerInvariant() + "=" + item.Value.Trim().ToLowerInvariant()));
        }
        return string.Join("/", parts);
    }

    /// <summary>
    /// Distribution-free confidence interval of the median from order statistics.
    /// Returns null when there are too few samples for the requested level (six for 95%).
    /// </summary>
    /// <param name="values">Measured values.</param>
    /// <param name="level">Confidence level between 0 and 1.</param>
    /// <returns>Interval, or null.</returns>
    public static BenchmarkConfidenceInterval? MedianConfidenceInterval(IEnumerable<double> values, double level)
    {
        if (level <= 0 || level >= 1)
            throw new ArgumentOutOfRangeException(nameof(level), "Confidence level must be between 0 and 1.");
        double[] sorted = (values ?? Array.Empty<double>())
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .OrderBy(value => value)
            .ToArray();
        int n = sorted.Length;
        if (n == 0)
            return null;

        double tail = (1 - level) / 2;
        // Largest rank k (1-based) with P(X <= k - 1) <= tail for X ~ Binomial(n, 0.5).
        double logHalfPower = -n * Math.Log(2);
        double logChoose = 0;
        double cumulative = 0;
        int rank = 0;
        for (int k = 0; k <= n / 2; k++)
        {
            if (k > 0)
                logChoose += Math.Log(n - k + 1) - Math.Log(k);
            cumulative += Math.Exp(logChoose + logHalfPower);
            if (cumulative > tail)
                break;
            rank = k + 1;
        }

        if (rank == 0)
            return null;
        return new BenchmarkConfidenceInterval
        {
            Level = level,
            LowerMs = sorted[rank - 1],
            UpperMs = sorted[n - rank]
        };
    }

    /// <summary>
    /// Confidence interval of the mean using Student's t distribution. Returns null for fewer than two samples.
    /// </summary>
    /// <param name="values">Measured values.</param>
    /// <param name="level">Confidence level between 0 and 1.</param>
    /// <returns>Interval, or null.</returns>
    public static BenchmarkConfidenceInterval? MeanConfidenceInterval(IEnumerable<double> values, double level)
    {
        if (level <= 0 || level >= 1)
            throw new ArgumentOutOfRangeException(nameof(level), "Confidence level must be between 0 and 1.");
        double[] data = (values ?? Array.Empty<double>()).Where(value => !double.IsNaN(value) && !double.IsInfinity(value)).ToArray();
        int n = data.Length;
        if (n < 2)
            return null;
        double mean = data.Average();
        double variance = data.Sum(value => (value - mean) * (value - mean)) / (n - 1);
        double margin = StudentT(1 - (1 - level) / 2, n - 1) * Math.Sqrt(variance / n);
        return new BenchmarkConfidenceInterval { Level = level, LowerMs = Math.Max(0, mean - margin), UpperMs = mean + margin };
    }

    // Student's t quantile from the normal quantile with the Cornish-Fisher expansion; accurate to about 1e-3 for
    // the sample sizes benchmark cells use (ten or more).
    private static double StudentT(double probability, int degreesOfFreedom)
    {
        double z = NormalQuantile(probability);
        double v = degreesOfFreedom;
        double z3 = z * z * z;
        double z5 = z3 * z * z;
        double z7 = z5 * z * z;
        return z + (z3 + z) / (4 * v) + (5 * z5 + 16 * z3 + 3 * z) / (96 * v * v) +
               (3 * z7 + 19 * z5 + 17 * z3 - 15 * z) / (384 * v * v * v);
    }

    // Acklam's rational approximation of the inverse standard normal distribution.
    private static double NormalQuantile(double p)
    {
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double low = 0.02425;
        if (p < low)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p > 1 - low)
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        double r = p - 0.5;
        double s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r / (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1);
    }

    /// <summary>True when two confidence intervals overlap, so the results are treated as even.</summary>
    /// <param name="left">First interval.</param>
    /// <param name="right">Second interval.</param>
    /// <returns>True when the intervals overlap.</returns>
    public static bool Overlaps(BenchmarkConfidenceInterval left, BenchmarkConfidenceInterval right)
        => left.LowerMs <= right.UpperMs && right.LowerMs <= left.UpperMs;
}
