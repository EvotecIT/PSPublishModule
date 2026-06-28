using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Writes managed module benchmark results as durable report artifacts.
/// </summary>
public sealed class ManagedModuleBenchmarkReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Writes benchmark results to a JSON file.
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="result">Benchmark result to write.</param>
    public void WriteJson(string path, ManagedModuleBenchmarkResult result)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Report path is required.", nameof(path));
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        EnsureDirectory(path);
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
    }

    /// <summary>
    /// Writes benchmark results to a Markdown file.
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="result">Benchmark result to write.</param>
    public void WriteMarkdown(string path, ManagedModuleBenchmarkResult result)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Report path is required.", nameof(path));
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        EnsureDirectory(path);
        File.WriteAllText(path, BuildMarkdown(result));
    }

    /// <summary>
    /// Builds a Markdown benchmark report.
    /// </summary>
    /// <param name="result">Benchmark result.</param>
    /// <returns>Markdown report text.</returns>
    public string BuildMarkdown(ManagedModuleBenchmarkResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var runs = result.Runs ?? Array.Empty<ManagedModuleBenchmarkRunResult>();
        var successful = runs.Count(static run => run.Succeeded);
        var failed = runs.Count - successful;
        var markdown = new StringBuilder();
        markdown.AppendLine("# Managed Module Benchmark Report");
        markdown.AppendLine();
        markdown.AppendLine($"Started UTC: `{result.StartedAtUtc:O}`");
        markdown.AppendLine($"Completed UTC: `{result.CompletedAtUtc:O}`");
        markdown.AppendLine($"Runs: `{runs.Count}`");
        markdown.AppendLine($"Succeeded: `{successful}`");
        markdown.AppendLine($"Failed: `{failed}`");
        markdown.AppendLine();
        AppendEnvironment(markdown, result.Environment);
        markdown.AppendLine();
        AppendSummary(markdown, runs);
        markdown.AppendLine();
        AppendTransitionGates(markdown, result.TransitionGates ?? Array.Empty<ManagedModuleBenchmarkTransitionGateResult>());
        markdown.AppendLine();
        markdown.AppendLine("| Scenario | Module | Engine | Operation | Iteration | Status | Version | Previous | Elapsed ms | Requests | Packages | Package bytes | Extracted bytes | Extraction ms | Files | Disk bytes | Version check | Import check | Error |");
        markdown.AppendLine("| --- | --- | --- | --- | ---: | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- |");

        foreach (var run in runs)
        {
            markdown.Append("| ")
                .Append(Escape(run.ScenarioId))
                .Append(" | ")
                .Append(Escape(run.ModuleName))
                .Append(" | ")
                .Append(Escape(run.Engine))
                .Append(" | ")
                .Append(run.Operation)
                .Append(" | ")
                .Append(run.Iteration)
                .Append(" | ")
                .Append(Escape(run.Status))
                .Append(" | ")
                .Append(Escape(run.Version))
                .Append(" | ")
                .Append(Escape(run.PreviousVersion))
                .Append(" | ")
                .Append(run.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(run.RepositoryRequestCount)
                .Append(" | ")
                .Append(run.PackageCount)
                .Append(" | ")
                .Append(ResolveTotal(run.TotalPackageBytes, run.PackageBytes))
                .Append(" | ")
                .Append(ResolveTotal(run.TotalExtractedBytes, run.ExtractedBytes))
                .Append(" | ")
                .Append(FormatMilliseconds(ResolveTotal(run.TotalExtractionElapsed, run.ExtractionElapsed)))
                .Append(" | ")
                .Append(ResolveTotal(run.TotalFileCount, run.FileCount))
                .Append(" | ")
                .Append(run.FinalDiskBytes)
                .Append(" | ")
                .Append(FormatVersionValidation(run))
                .Append(" | ")
                .Append(FormatImportValidation(run))
                .Append(" | ")
                .Append(Escape(run.ErrorMessage))
                .AppendLine(" |");
        }

        return markdown.ToString();
    }

    private static void AppendEnvironment(StringBuilder markdown, ManagedModuleBenchmarkEnvironment? environment)
    {
        markdown.AppendLine("## Environment");
        markdown.AppendLine();
        if (environment is null)
        {
            markdown.AppendLine("_No runtime environment metadata was recorded._");
            return;
        }

        markdown.AppendLine("| Field | Value |");
        markdown.AppendLine("| --- | --- |");
        AppendEnvironmentRow(markdown, "PowerShell version", environment.PowerShellVersion);
        AppendEnvironmentRow(markdown, "PowerShell edition", environment.PowerShellEdition);
        AppendEnvironmentRow(markdown, "PowerShell host", environment.PowerShellHostName);
        AppendEnvironmentRow(markdown, "PowerShell host version", environment.PowerShellHostVersion);
        AppendEnvironmentRow(markdown, ".NET runtime", environment.RuntimeDescription);
        AppendEnvironmentRow(markdown, "Runtime identifier", environment.RuntimeIdentifier);
        AppendEnvironmentRow(markdown, "Operating system", environment.OperatingSystemDescription);
        AppendEnvironmentRow(markdown, "Process architecture", environment.ProcessArchitecture);
    }

    private static void AppendEnvironmentRow(StringBuilder markdown, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        markdown.Append("| ")
            .Append(Escape(field))
            .Append(" | ")
            .Append(Escape(value))
            .AppendLine(" |");
    }

    private static void AppendSummary(StringBuilder markdown, IReadOnlyList<ManagedModuleBenchmarkRunResult> runs)
    {
        markdown.AppendLine("## Scenario Summary");
        markdown.AppendLine();
        if (runs.Count == 0)
        {
            markdown.AppendLine("_No benchmark runs were recorded._");
            return;
        }

        markdown.AppendLine("| Scenario | Operation | Engine | Runs | Succeeded | Failed | Avg ms | Median ms | Min ms | Max ms | Packages | Package bytes | Disk bytes |");
        markdown.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var group in runs
                     .GroupBy(static run => new { run.ScenarioId, run.Operation, run.Engine })
                     .OrderBy(static group => group.Key.ScenarioId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static group => group.Key.Operation)
                     .ThenBy(static group => group.Key.Engine, StringComparer.OrdinalIgnoreCase))
        {
            var statistics = CalculateStatistics(group.Select(static run => run.Elapsed));
            markdown.Append("| ")
                .Append(Escape(group.Key.ScenarioId))
                .Append(" | ")
                .Append(group.Key.Operation)
                .Append(" | ")
                .Append(Escape(group.Key.Engine))
                .Append(" | ")
                .Append(group.Count())
                .Append(" | ")
                .Append(group.Count(static run => run.Succeeded))
                .Append(" | ")
                .Append(group.Count(static run => !run.Succeeded))
                .Append(" | ")
                .Append(FormatMilliseconds(statistics.Average))
                .Append(" | ")
                .Append(FormatMilliseconds(statistics.Median))
                .Append(" | ")
                .Append(FormatMilliseconds(statistics.Minimum))
                .Append(" | ")
                .Append(FormatMilliseconds(statistics.Maximum))
                .Append(" | ")
                .Append(group.Sum(static run => ResolveTotal(run.PackageCount, string.IsNullOrWhiteSpace(run.PackagePath) ? 0 : 1)))
                .Append(" | ")
                .Append(group.Sum(static run => ResolveTotal(run.TotalPackageBytes, run.PackageBytes)))
                .Append(" | ")
                .Append(group.Sum(static run => run.FinalDiskBytes))
                .AppendLine(" |");
        }
    }

    private static void AppendTransitionGates(StringBuilder markdown, IReadOnlyList<ManagedModuleBenchmarkTransitionGateResult> gates)
    {
        markdown.AppendLine("## Transition Gates");
        markdown.AppendLine();
        if (gates.Count == 0)
        {
            markdown.AppendLine("_No install, save, update, or publish transition gates were evaluated._");
            return;
        }

        markdown.AppendLine("| Operation | Status | Default ready | Fallback | Managed | Compatibility | Covered baselines | Performance | Provider limitations | Reasons |");
        markdown.AppendLine("| --- | --- | --- | --- | ---: | ---: | --- | --- | --- | --- |");
        foreach (var gate in gates.OrderBy(static gate => gate.Operation))
        {
            markdown.Append("| ")
                .Append(gate.Operation)
                .Append(" | ")
                .Append(gate.Status)
                .Append(" | ")
                .Append(gate.ReadyForDefaultManagedTransport ? "yes" : "no")
                .Append(" | ")
                .Append(Escape(FormatFallback(gate)))
                .Append(" | ")
                .Append(gate.SuccessfulManagedRunCount)
                .Append("/")
                .Append(gate.ManagedRunCount)
                .Append(" | ")
                .Append(gate.SuccessfulCompatibilityRunCount)
                .Append("/")
                .Append(gate.CompatibilityRunCount)
                .Append(" | ")
                .Append(Escape(FormatCompatibilityCoverage(gate)))
                .Append(" | ")
                .Append(Escape(FormatPerformance(gate)))
                .Append(" | ")
                .Append(Escape(FormatProviderLimitations(gate)))
                .Append(" | ")
                .Append(Escape(string.Join("; ", gate.Reasons ?? Array.Empty<string>())))
                .AppendLine(" |");
        }
    }

    private static string FormatFallback(ManagedModuleBenchmarkTransitionGateResult gate)
    {
        if (!gate.CompatibilityFallbackRequired)
            return "not required";

        if (!string.IsNullOrWhiteSpace(gate.CompatibilityFallbackReason))
            return gate.CompatibilityFallbackReason!;

        return gate.NativeIsolationRequired ? "native isolation required" : "required";
    }

    private static string FormatCompatibilityCoverage(ManagedModuleBenchmarkTransitionGateResult gate)
    {
        var covered = gate.CoveredCompatibilityEngines ?? Array.Empty<string>();
        var required = gate.RequiredCompatibilityEngines ?? Array.Empty<string>();
        if (required.Count == 0)
            return string.Empty;

        return string.Join(
            ", ",
            required.Select(engine => covered.Contains(engine, StringComparer.OrdinalIgnoreCase) ? engine + ":ok" : engine + ":missing"));
    }

    private static string FormatPerformance(ManagedModuleBenchmarkTransitionGateResult gate)
    {
        if (gate.PerformanceWithinPolicy is null)
            return "not evaluated";

        var state = gate.PerformanceWithinPolicy == true ? "ok" : "blocked";
        return state +
               " (managed median " + FormatNullableMilliseconds(gate.ManagedMedianMilliseconds) +
               ", compatibility median " + FormatNullableMilliseconds(gate.CompatibilityMedianMilliseconds) +
               ", allowed " + FormatNullableMilliseconds(gate.AllowedManagedMilliseconds) + ")";
    }

    private static string FormatProviderLimitations(ManagedModuleBenchmarkTransitionGateResult gate)
    {
        var limitations = gate.CompatibilityProviderLimitations ?? Array.Empty<string>();
        return limitations.Count == 0
            ? "none"
            : string.Join("; ", limitations);
    }

    private static string FormatNullableMilliseconds(double? milliseconds)
        => milliseconds.HasValue
            ? milliseconds.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ms"
            : "n/a";

    private static BenchmarkElapsedStatistics CalculateStatistics(IEnumerable<TimeSpan> elapsedValues)
    {
        var values = elapsedValues
            .OrderBy(static elapsed => elapsed)
            .ToArray();
        if (values.Length == 0)
            return new BenchmarkElapsedStatistics();

        var median = values.Length % 2 == 1
            ? values[values.Length / 2]
            : TimeSpan.FromTicks((values[(values.Length / 2) - 1].Ticks + values[values.Length / 2].Ticks) / 2);
        return new BenchmarkElapsedStatistics
        {
            Average = TimeSpan.FromTicks((long)values.Average(static elapsed => elapsed.Ticks)),
            Median = median,
            Minimum = values[0],
            Maximum = values[values.Length - 1]
        };
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value!.Replace("|", "\\|").Replace(Environment.NewLine, " ");
    }

    private static long ResolveTotal(long total, long fallback)
        => total > 0 ? total : fallback;

    private static int ResolveTotal(int total, int fallback)
        => total > 0 ? total : fallback;

    private static TimeSpan? ResolveTotal(TimeSpan? total, TimeSpan? fallback)
        => total is { Ticks: > 0 } ? total : fallback;

    private static string FormatMilliseconds(TimeSpan? value)
        => value is null
            ? string.Empty
            : value.Value.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatVersionValidation(ManagedModuleBenchmarkRunResult run)
    {
        if (run.VersionValidationSucceeded is null)
            return Escape(run.VersionValidationMessage);

        var status = run.VersionValidationSucceeded.Value ? "ok" : "failed";
        return string.IsNullOrWhiteSpace(run.ValidatedVersion)
            ? status
            : status + " " + Escape(run.ValidatedVersion);
    }

    private static string FormatImportValidation(ManagedModuleBenchmarkRunResult run)
    {
        var validations = run.ImportValidations ?? Array.Empty<ManagedModuleImportValidationResult>();
        if (validations.Count == 0)
            return string.Empty;

        return string.Join(
            ", ",
            validations.Select(validation =>
            {
                var status = validation.Succeeded ? "ok" : "failed";
                var version = string.IsNullOrWhiteSpace(validation.ImportedVersion)
                    ? string.Empty
                    : " " + validation.ImportedVersion;
                return validation.Host + ":" + status + Escape(version);
            }));
    }

    private struct BenchmarkElapsedStatistics
    {
        internal TimeSpan Average { get; set; }

        internal TimeSpan Median { get; set; }

        internal TimeSpan Minimum { get; set; }

        internal TimeSpan Maximum { get; set; }
    }
}
