using System;
using System.Collections.Generic;
using System.Text;

namespace PowerForge;

/// <summary>
/// Column names BenchmarkDotNet writes into its reports that describe how a run was configured
/// (job characteristics) rather than which case a row measures.
/// </summary>
/// <remarks>
/// BenchmarkDotNet CSV reports put job characteristics, statistics and benchmark parameters side by side.
/// Treating job characteristics as parameters turns every row into its own case, so importers and
/// comparison views use this list to keep them apart.
/// </remarks>
public static class BenchmarkDotNetColumns
{
    private static readonly HashSet<string> JobCharacteristics = new(StringComparer.OrdinalIgnoreCase)
    {
        // Identity of the generated benchmark type.
        "Namespace", "Type", "FullName", "BenchmarkDotNetReport", "Job",
        // Accuracy.
        "AnalyzeLaunchVariance", "EvaluateOverhead", "MaxAbsoluteError", "MaxRelativeError", "MinInvokeCount",
        "MinIterationTime", "OutlierMode",
        // Environment and garbage collector.
        "Affinity", "EnvironmentVariables", "Jit", "LargeAddressAware", "Platform", "PowerPlanMode", "Runtime",
        "AllowVeryLargeObjects", "Concurrent", "CpuGroups", "Force", "HeapAffinitizeMask", "HeapCount",
        "NoAffinitize", "RetainVm", "Server",
        // Infrastructure.
        "Arguments", "BuildConfiguration", "Clock", "EngineFactory", "NuGetReferences", "Toolchain", "IsMutator",
        // Run strategy.
        "InvocationCount", "IterationCount", "IterationTime", "LaunchCount", "MaxIterationCount",
        "MaxWarmupIterationCount", "MemoryRandomization", "MinIterationCount", "MinWarmupIterationCount",
        "RunStrategy", "UnrollFactor", "WarmupCount"
    };

    private static readonly HashSet<string> Statistics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mean", "Median", "Min", "Max", "Q1", "Q3",
        "P0", "P25", "P50", "P75", "P90", "P95", "P99", "P100",
        "Error", "StdErr", "StdDev", "StandardError", "StandardDeviation", "Ratio", "RatioSD",
        "Gen0", "Gen1", "Gen2", "Allocated", "AllocRatio", "CodeSize", "OperationsPerSecond", "Op/s", "Rank",
        "CompletedWorkItems", "LockContentions"
    };

    /// <summary>Metadata key prefix used to record job characteristics shared by every row of an imported report.</summary>
    public const string JobMetadataPrefix = "benchmarkdotnet.job.";

    /// <summary>
    /// Returns true when the column is a BenchmarkDotNet statistic, such as "Mean [ms]", "Error" or "Alloc Ratio".
    /// Units in brackets and spaces are ignored.
    /// </summary>
    public static bool IsStatistic(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var text = name!;
        var bracket = text.IndexOf('[');
        if (bracket >= 0) text = text.Substring(0, bracket);
        return IsStatisticName(text.Replace(" ", string.Empty).Trim());
    }

    internal static bool IsStatisticName(string normalizedName) => Statistics.Contains(normalizedName);

    /// <summary>Returns true when the column is a BenchmarkDotNet job characteristic rather than a benchmark parameter.</summary>
    public static bool IsJobCharacteristic(string? name)
        => !string.IsNullOrWhiteSpace(name) && JobCharacteristics.Contains(name!.Replace(" ", string.Empty));

    /// <summary>
    /// Reads benchmark parameters from a BenchmarkDotNet full name such as
    /// <c>Bench.Merge(Scale: Easy, Producer: IText)</c> or <c>Bench.Decode(Case: "a, b")</c>.
    /// Quoted values keep their commas; returns false when the name has no parameter list.
    /// </summary>
    public static bool TryParseFullNameParameters(string? fullName, out IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        parameters = Array.Empty<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(fullName) || !fullName!.EndsWith(")", StringComparison.Ordinal))
            return false;
        var open = fullName.IndexOf('(');
        if (open < 0 || open >= fullName.Length - 2)
            return false;

        var list = new List<KeyValuePair<string, string>>();
        var text = fullName.Substring(open + 1, fullName.Length - open - 2);
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i <= text.Length; i++)
        {
            var end = i == text.Length;
            var c = end ? '\0' : text[i];
            if (!end && c == '"' && (i == 0 || text[i - 1] != '\\'))
                quoted = !quoted;
            if (end || (!quoted && c == ',' && i + 1 < text.Length && text[i + 1] == ' '))
            {
                var separator = field.ToString().IndexOf(": ", StringComparison.Ordinal);
                if (separator <= 0)
                    return false;
                var name = field.ToString(0, separator).Trim();
                var value = field.ToString(separator + 2, field.Length - separator - 2).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"");
                list.Add(new KeyValuePair<string, string>(name, value));
                field.Clear();
                if (!end) i++;
                continue;
            }
            field.Append(c);
        }

        parameters = list;
        return list.Count > 0;
    }
}
