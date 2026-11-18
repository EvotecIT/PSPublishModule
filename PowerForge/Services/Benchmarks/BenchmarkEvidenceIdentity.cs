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
        if (recorded.Length > 0 && !string.Equals(recorded, requested, StringComparison.OrdinalIgnoreCase))
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
    public static string RecordedFramework(BenchmarkRunResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (result.Metadata.TryGetValue("benchmark.workload.framework", out string? metadataFramework) &&
            !string.IsNullOrWhiteSpace(metadataFramework))
        {
            string declared = metadataFramework.Trim();
            if (!IsTargetFramework(declared))
                throw new InvalidOperationException($"Framework '{declared}' is not a target framework moniker such as net10.0.");
            return declared.ToLowerInvariant();
        }

        Match match = DotNetRuntime.Match(result.Environment.RuntimeVersion ?? string.Empty);
        if (match.Success && int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture) >= 5)
            return $"net{match.Groups["major"].Value}.{match.Groups["minor"].Value}";
        return string.Empty;
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

    /// <summary>True when two confidence intervals overlap, so the results are treated as even.</summary>
    /// <param name="left">First interval.</param>
    /// <param name="right">Second interval.</param>
    /// <returns>True when the intervals overlap.</returns>
    public static bool Overlaps(BenchmarkConfidenceInterval left, BenchmarkConfidenceInterval right)
        => left.LowerMs <= right.UpperMs && right.LowerMs <= left.UpperMs;
}
