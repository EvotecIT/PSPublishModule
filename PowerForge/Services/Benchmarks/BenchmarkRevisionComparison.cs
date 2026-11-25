using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>
/// Pure pieces of the revision comparison: BenchmarkDotNet arguments, run order, aggregation and the text report.
/// </summary>
internal static class BenchmarkRevisionComparison
{
    internal const string BaselineName = "baseline";
    internal const string CandidateName = "candidate";

    internal static readonly string[] DefaultBenchmarkArguments =
    {
        "--launchCount", "1",
        "--warmupCount", "5",
        "--iterationCount", "15",
        "--outliers", "DontRemove",
        "--memory",
        "--exporters", "json"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["-m"] = "--memory",
        ["-e"] = "--exporters",
        ["-f"] = "--filter",
        ["-a"] = "--artifacts",
        ["-j"] = "--job"
    };

    /// <summary>
    /// Merges caller arguments over the defaults. An option the caller gives replaces the default option of the
    /// same name; the JSON exporter is always kept because the comparison reads it.
    /// </summary>
    internal static string[] MergeBenchmarkArguments(IReadOnlyList<string>? overrides)
    {
        var options = ParseOptions(DefaultBenchmarkArguments);
        foreach (var option in ParseOptions(overrides ?? Array.Empty<string>()))
        {
            if (option.Name is "--filter" or "--artifacts")
                throw new ArgumentException($"BenchmarkDotNet option {option.Name} is set by the comparison and cannot be passed through.");
            int existing = options.FindIndex(item => string.Equals(item.Name, option.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) options[existing] = option;
            else options.Add(option);
        }

        int exporters = options.FindIndex(item => string.Equals(item.Name, "--exporters", StringComparison.OrdinalIgnoreCase));
        if (exporters < 0)
            options.Add(new BenchmarkOption("--exporters", new List<string> { "json" }));
        else if (!options[exporters].Values.Any(value => value.StartsWith("json", StringComparison.OrdinalIgnoreCase)))
            options[exporters].Values.Add("json");

        return options.SelectMany(option => new[] { option.Name }.Concat(option.Values)).ToArray();
    }

    /// <summary>
    /// Returns the sides of one round in run order. Odd rounds (1, 3, …) run the baseline first, even rounds the candidate.
    /// </summary>
    internal static string[] RoundOrder(int round)
        => round % 2 == 0
            ? new[] { CandidateName, BaselineName }
            : new[] { BaselineName, CandidateName };

    /// <summary>
    /// Builds one comparison row per benchmark case from the per-round results of each side.
    /// A null entry is a round whose run failed before producing a report.
    /// </summary>
    internal static BenchmarkRevisionComparisonRow[] Aggregate(
        IReadOnlyList<BenchmarkRunResult?> baselineRounds,
        IReadOnlyList<BenchmarkRunResult?> candidateRounds)
    {
        var baseline = Index(baselineRounds);
        var candidate = Index(candidateRounds);
        var keys = baseline.Keys.Concat(candidate.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        var rows = new List<BenchmarkRevisionComparisonRow>(keys.Length);
        foreach (string key in keys)
        {
            var baseRows = Rounds(baseline, key, baselineRounds.Count);
            var candRows = Rounds(candidate, key, candidateRounds.Count);
            var row = new BenchmarkRevisionComparisonRow
            {
                Benchmark = key,
                Baseline = Measure(baseRows),
                Candidate = Measure(candRows)
            };
            row.Ratio = Divide(row.Candidate.MedianMs, row.Baseline.MedianMs);
            row.AllocatedRatio = Divide(row.Candidate.AllocatedBytes, row.Baseline.AllocatedBytes);
            var roundRatios = Enumerable.Range(0, Math.Min(baseRows.Length, candRows.Length))
                .Select(index => Divide(candRows[index]?.MedianMs, baseRows[index]?.MedianMs))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            if (roundRatios.Length > 0)
            {
                row.RatioMin = roundRatios.Min();
                row.RatioMax = roundRatios.Max();
            }
            rows.Add(row);
        }
        return rows.ToArray();
    }

    /// <summary>
    /// Renders one table per benchmark case.
    /// </summary>
    internal static string RenderText(BenchmarkRevisionComparisonResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} vs {1}, {2} ({3}), {4} round(s), order {5}",
            Describe(result.Baseline), Describe(result.Candidate), result.Project, result.Framework,
            result.Rounds, string.Join(" ", result.RunOrder)));
        text.AppendLine("BenchmarkDotNet " + string.Join(" ", result.BenchmarkArguments));
        if (result.Benchmarks.Length == 0)
        {
            text.AppendLine();
            text.AppendLine("No benchmark matched the filter.");
            return text.ToString();
        }

        foreach (var row in result.Benchmarks)
        {
            text.AppendLine();
            text.AppendLine(row.Benchmark);
            var lines = new List<string[]>
            {
                new[] { "Side", "Median", "Allocated", "Rounds", "Spread" },
                Line(result.Baseline.Name, row.Baseline),
                Line(result.Candidate.Name, row.Candidate)
            };
            AppendTable(text, lines);
            string ratio = row.Ratio is double value
                ? value.ToString("0.000", CultureInfo.InvariantCulture)
                : "n/a";
            string range = row.RatioMin is double min && row.RatioMax is double max
                ? string.Format(CultureInfo.InvariantCulture, " (rounds {0:0.000}-{1:0.000})", min, max)
                : string.Empty;
            string allocated = row.AllocatedRatio is double alloc
                ? alloc.ToString("0.000", CultureInfo.InvariantCulture)
                : "n/a";
            text.AppendLine($"Ratio {ratio}{range}, allocation ratio {allocated}");
        }
        return text.ToString();
    }

    internal static string FormatDuration(double? milliseconds)
    {
        if (milliseconds is not double ms) return "n/a";
        if (ms >= 1000) return (ms / 1000).ToString("0.000", CultureInfo.InvariantCulture) + " s";
        if (ms >= 1) return ms.ToString("0.000", CultureInfo.InvariantCulture) + " ms";
        if (ms >= 0.001) return (ms * 1000).ToString("0.000", CultureInfo.InvariantCulture) + " us";
        return (ms * 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + " ns";
    }

    internal static string FormatBytes(double? bytes)
    {
        if (bytes is not double value) return "n/a";
        if (value >= 1024 * 1024) return (value / (1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
        if (value >= 1024) return (value / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " KB";
        return value.ToString("0", CultureInfo.InvariantCulture) + " B";
    }

    private static string Describe(BenchmarkRevisionSide side)
    {
        string commit = side.Commit.Length > 10 ? side.Commit.Substring(0, 10) : side.Commit;
        return $"{side.Name} {side.Revision} ({commit}{(side.Dirty ? ", uncommitted changes" : string.Empty)})";
    }

    private static string[] Line(string name, BenchmarkRevisionMeasurement measurement)
        => new[]
        {
            name,
            FormatDuration(measurement.MedianMs),
            FormatBytes(measurement.AllocatedBytes),
            string.Join(", ", measurement.RoundMediansMs.Select(FormatDuration)),
            measurement.SpreadPercent is double spread
                ? spread.ToString("0.0", CultureInfo.InvariantCulture) + "%"
                : "n/a"
        };

    private static void AppendTable(StringBuilder text, List<string[]> lines)
    {
        int columns = lines[0].Length;
        var widths = Enumerable.Range(0, columns).Select(column => lines.Max(line => line[column].Length)).ToArray();
        for (int index = 0; index < lines.Count; index++)
        {
            text.AppendLine(string.Join("  ", lines[index].Select((cell, column) =>
                column == 0 ? cell.PadRight(widths[column]) : cell.PadLeft(widths[column]))).TrimEnd());
            if (index == 0)
                text.AppendLine(string.Join("  ", widths.Select(width => new string('-', width))));
        }
    }

    private static BenchmarkRevisionMeasurement Measure(BenchmarkSummaryRow?[] rounds)
    {
        var medians = rounds.Select(row => row?.MedianMs).ToArray();
        var measured = medians.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        var allocations = rounds
            .Select(row => row is not null && row.MedianMs.HasValue && row.Metrics.TryGetValue("Allocated", out double bytes) ? bytes : (double?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        double? median = Median(measured);
        return new BenchmarkRevisionMeasurement
        {
            MedianMs = median,
            RoundMediansMs = medians,
            SpreadPercent = measured.Length > 1 && median > 0 ? (measured.Max() - measured.Min()) / median * 100 : null,
            AllocatedBytes = Median(allocations),
            FailedRounds = medians.Count(value => !value.HasValue)
        };
    }

    private static Dictionary<string, Dictionary<int, BenchmarkSummaryRow>> Index(IReadOnlyList<BenchmarkRunResult?> rounds)
    {
        var index = new Dictionary<string, Dictionary<int, BenchmarkSummaryRow>>(StringComparer.Ordinal);
        for (int round = 0; round < rounds.Count; round++)
        {
            var rows = rounds[round]?.Summary ?? Array.Empty<BenchmarkSummaryRow>();
            // Several jobs of one benchmark share its full name; the job then becomes part of the key.
            var sharedNames = new HashSet<string>(rows
                .GroupBy(Key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key), StringComparer.Ordinal);
            foreach (var row in rows)
            {
                string key = Key(row);
                if (sharedNames.Contains(key))
                    key += " [job " + row.Engine + "]";
                if (index.TryGetValue(key, out var existing) && existing.ContainsKey(round))
                    throw new InvalidOperationException($"BenchmarkDotNet reported {key} more than once in one run.");
                if (!index.TryGetValue(key, out var byRound))
                    index[key] = byRound = new Dictionary<int, BenchmarkSummaryRow>();
                byRound[round] = row;
            }
        }
        return index;
    }

    private static BenchmarkSummaryRow?[] Rounds(
        Dictionary<string, Dictionary<int, BenchmarkSummaryRow>> index,
        string key,
        int count)
        => Enumerable.Range(0, count)
            .Select(round => index.TryGetValue(key, out var byRound) && byRound.TryGetValue(round, out var row) ? row : null)
            .ToArray();

    private static string Key(BenchmarkSummaryRow row)
    {
        if (row.Variables.TryGetValue("FullName", out string? fullName) && !string.IsNullOrWhiteSpace(fullName))
            return fullName!;
        string parameters = string.Join(", ", row.Variables
            .Where(item => item.Key is not ("Namespace" or "Type" or "Categories"))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key + "=" + item.Value));
        return parameters.Length == 0 ? row.Scenario : row.Scenario + " [" + parameters + "]";
    }

    private static double? Median(double[] values)
    {
        if (values.Length == 0) return null;
        var sorted = values.OrderBy(value => value).ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static double? Divide(double? numerator, double? denominator)
        => numerator.HasValue && denominator is double value && value > 0 ? numerator.Value / value : null;

    private static List<BenchmarkOption> ParseOptions(IReadOnlyList<string> arguments)
    {
        var options = new List<BenchmarkOption>();
        foreach (string argument in arguments)
        {
            if (IsOptionName(argument))
            {
                string name = Aliases.TryGetValue(argument, out string? alias) ? alias : argument;
                options.Add(new BenchmarkOption(name, new List<string>()));
            }
            else if (options.Count == 0)
            {
                throw new ArgumentException($"BenchmarkDotNet argument '{argument}' must follow an option name.");
            }
            else
            {
                options[options.Count - 1].Values.Add(argument);
            }
        }
        return options;
    }

    private static bool IsOptionName(string argument)
        => argument.Length > 1 && argument[0] == '-' && (char.IsLetter(argument[1]) || argument[1] == '-');

    private sealed class BenchmarkOption
    {
        internal BenchmarkOption(string name, List<string> values)
        {
            Name = name;
            Values = values;
        }

        internal string Name { get; }
        internal List<string> Values { get; }
    }
}
