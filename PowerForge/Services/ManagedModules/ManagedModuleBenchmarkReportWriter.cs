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
        markdown.AppendLine("| Scenario | Engine | Operation | Iteration | Status | Version | Previous | Elapsed ms | Requests | Packages | Package bytes | Extracted bytes | Extraction ms | Files | Disk bytes | Version check | Error |");
        markdown.AppendLine("| --- | --- | --- | ---: | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |");

        foreach (var run in runs)
        {
            markdown.Append("| ")
                .Append(Escape(run.ScenarioId))
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
                .Append(Escape(run.ErrorMessage))
                .AppendLine(" |");
        }

        return markdown.ToString();
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
}
