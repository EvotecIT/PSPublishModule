namespace PowerForge;

/// <summary>
/// Selection applied to a benchmark suite after declaration and before planning or execution.
/// </summary>
public sealed class PowerShellBenchmarkSelection
{
    /// <summary>Case or scenario names to include.</summary>
    public string[] Cases { get; set; } = Array.Empty<string>();

    /// <summary>Engine names to include.</summary>
    public string[] Engines { get; set; } = Array.Empty<string>();

    /// <summary>Operation names to include.</summary>
    public string[] Operations { get; set; } = Array.Empty<string>();

    /// <summary>Host labels to include.</summary>
    public string[] Hosts { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Applies user-facing benchmark selection to declared suites.
/// </summary>
public static class PowerShellBenchmarkSuiteFilter
{
    /// <summary>
    /// Applies case, engine, operation, and host filters to a benchmark suite.
    /// </summary>
    /// <param name="suite">Suite to mutate.</param>
    /// <param name="selection">Selection values.</param>
    public static void Apply(PowerShellBenchmarkSuite suite, PowerShellBenchmarkSelection? selection)
    {
        if (suite is null) throw new ArgumentNullException(nameof(suite));
        if (selection is null) return;

        FilterCases(suite, Normalize(selection.Cases));
        FilterEngines(suite, Normalize(selection.Engines));
        ReplaceAxisWhenRequested(suite, "Operation", Normalize(selection.Operations));
        ReplaceAxisWhenRequested(suite, "Host", Normalize(selection.Hosts));
    }

    private static void FilterCases(PowerShellBenchmarkSuite suite, string[] requested)
    {
        if (requested.Length == 0) return;
        if (suite.Cases.Count == 0)
            throw new InvalidOperationException($"Benchmark suite '{suite.Name}' cannot filter cases because it uses the implicit default case.");

        var selected = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var before = suite.Cases.Select(static benchmarkCase => benchmarkCase.Name).ToArray();
        suite.Cases.RemoveAll(benchmarkCase => !selected.Contains(benchmarkCase.Name));
        ThrowIfMissing(suite.Name, "case", requested, before);
        if (suite.Cases.Count == 0)
            throw new InvalidOperationException($"Benchmark suite '{suite.Name}' case selection did not leave any runnable cases.");
    }

    private static void FilterEngines(PowerShellBenchmarkSuite suite, string[] requested)
    {
        if (requested.Length == 0) return;
        var selected = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var before = suite.Engines.Select(static engine => engine.Name).ToArray();
        suite.Engines.RemoveAll(engine => !selected.Contains(engine.Name));
        ThrowIfMissing(suite.Name, "engine", requested, before);
        if (suite.Engines.Count == 0)
            throw new InvalidOperationException($"Benchmark suite '{suite.Name}' engine selection did not leave any runnable engines.");
        ReplaceAxisWhenRequested(suite, "Engine", requested);
        suite.Comparisons.RemoveAll(comparison =>
            string.Equals(comparison.Dimension, "Engine", StringComparison.OrdinalIgnoreCase)
            && !selected.Contains(comparison.Baseline));
    }

    private static void ReplaceAxisWhenRequested(PowerShellBenchmarkSuite suite, string name, string[] requested)
    {
        if (requested.Length == 0) return;
        var existing = suite.Axes.FirstOrDefault(axis => string.Equals(axis.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new PowerShellBenchmarkAxis { Name = name };
            suite.Axes.Add(existing);
        }
        else
        {
            existing.Values.Clear();
        }

        existing.Values.AddRange(requested);
    }

    private static void ThrowIfMissing(string suiteName, string label, string[] requested, string[] available)
    {
        var availableSet = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requested.Where(value => !availableSet.Contains(value)).ToArray();
        if (missing.Length == 0) return;
        throw new InvalidOperationException($"Benchmark suite '{suiteName}' does not define {label} '{string.Join(", ", missing)}'. Available {label}s: {string.Join(", ", available)}.");
    }

    private static string[] Normalize(IEnumerable<string>? values)
        => values?
            .SelectMany(static value => (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
           ?? Array.Empty<string>();
}
