using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Builds portable JSON and Markdown reports for GitHub housekeeping runs.
/// </summary>
public sealed class GitHubHousekeepingReportService
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Creates a report payload from a completed housekeeping result.
    /// </summary>
    /// <param name="result">Completed housekeeping result.</param>
    /// <returns>Portable housekeeping report payload.</returns>
    public GitHubHousekeepingReport CreateSuccessReport(GitHubHousekeepingResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        return new GitHubHousekeepingReport
        {
            Success = result.Success,
            ExitCode = result.Success ? 0 : 1,
            Result = result
        };
    }

    /// <summary>
    /// Creates a failure report when the housekeeping command fails before producing a result.
    /// </summary>
    /// <param name="exitCode">Associated process exit code.</param>
    /// <param name="error">Failure message.</param>
    /// <returns>Portable failure report payload.</returns>
    public GitHubHousekeepingReport CreateFailureReport(int exitCode, string error)
    {
        return new GitHubHousekeepingReport
        {
            Success = false,
            ExitCode = exitCode,
            Error = error
        };
    }

    /// <summary>
    /// Serializes a housekeeping report to indented JSON.
    /// </summary>
    /// <param name="report">Report payload to serialize.</param>
    /// <returns>Indented JSON report.</returns>
    public string BuildJson(GitHubHousekeepingReport report)
    {
        if (report is null)
            throw new ArgumentNullException(nameof(report));
        return JsonSerializer.Serialize(report, ReportJsonOptions);
    }

    /// <summary>
    /// Renders a human-readable Markdown report for GitHub Actions summaries and artifacts.
    /// </summary>
    /// <param name="report">Report payload to render.</param>
    /// <returns>Markdown representation of the report.</returns>
    public string BuildMarkdown(GitHubHousekeepingReport report)
    {
        if (report is null)
            throw new ArgumentNullException(nameof(report));

        var markdown = new StringBuilder();
        markdown.AppendLine("# PowerForge GitHub Housekeeping Report");
        markdown.AppendLine();

        if (report.Result is null)
        {
            markdown.AppendLine("> ❌ **Housekeeping failed before section results were produced**");
            markdown.AppendLine();
            markdown.AppendLine("| Field | Value |");
            markdown.AppendLine("| --- | --- |");
            markdown.AppendLine($"| Success | {(report.Success ? "Yes" : "No")} |");
            markdown.AppendLine($"| Exit code | {report.ExitCode} |");
            markdown.AppendLine($"| Error | {EscapeCell(report.Error)} |");
            return markdown.ToString();
        }

        var result = report.Result;
        var repository = string.IsNullOrWhiteSpace(result.Repository) ? "(runner-only)" : result.Repository;
        markdown.AppendLine($"> {(report.Success ? "✅" : "❌")} **{repository}** ran in **{(result.DryRun ? "dry-run" : "apply")}** mode");
        markdown.AppendLine();
        markdown.AppendLine("| Field | Value |");
        markdown.AppendLine("| --- | --- |");
        markdown.AppendLine($"| Success | {(report.Success ? "Yes" : "No")} |");
        markdown.AppendLine($"| Requested sections | {EscapeCell(string.Join(", ", result.RequestedSections))} |");
        markdown.AppendLine($"| Completed sections | {EscapeCell(string.Join(", ", result.CompletedSections))} |");
        markdown.AppendLine($"| Failed sections | {EscapeCell(string.Join(", ", result.FailedSections))} |");
        if (!string.IsNullOrWhiteSpace(result.Message))
            markdown.AppendLine($"| Message | {EscapeCell(result.Message)} |");

        AppendStorageSummary(markdown, result);
        AppendArtifactDetails(markdown, result.Artifacts);
        AppendCacheDetails(markdown, result.Caches);
        AppendRunnerDetails(markdown, result.Runner);
        return markdown.ToString();
    }

    /// <summary>
    /// Writes the JSON report to disk using UTF-8 without BOM.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="report">Report payload to persist.</param>
    public void WriteJsonReport(string path, GitHubHousekeepingReport report)
        => WriteUtf8(path, BuildJson(report));

    /// <summary>
    /// Writes the Markdown report to disk using UTF-8 without BOM.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="report">Report payload to persist.</param>
    public void WriteMarkdownReport(string path, GitHubHousekeepingReport report)
        => WriteUtf8(path, BuildMarkdown(report));

    private static void AppendStorageSummary(StringBuilder markdown, GitHubHousekeepingResult result)
    {
        var rows = new List<string>();

        if (result.Artifacts is not null)
        {
            rows.Add($"| Artifacts | {Status(result.Artifacts.Success, result.Artifacts.FailedDeletes)} | {CountAndBytes(result.Artifacts.PlannedDeletes, result.Artifacts.PlannedDeleteBytes)} | {CountAndBytes(result.Artifacts.DeletedArtifacts, result.Artifacts.DeletedBytes)} | {result.Artifacts.FailedDeletes} | - | - |");
        }

        if (result.Caches is not null)
        {
            rows.Add($"| Caches | {Status(result.Caches.Success, result.Caches.FailedDeletes)} | {CountAndBytes(result.Caches.PlannedDeletes, result.Caches.PlannedDeleteBytes)} | {CountAndBytes(result.Caches.DeletedCaches, result.Caches.DeletedBytes)} | {result.Caches.FailedDeletes} | {CacheUsage(result.Caches.UsageBefore)} | {CacheUsage(result.Caches.UsageAfter)} |");
        }

        if (result.Runner is not null)
        {
            rows.Add($"| Runner | {Status(result.Runner.Success, result.Runner.Success ? 0 : 1)} | - | - | {(result.Runner.Success ? "0" : "1")} | {FormatGiB(result.Runner.FreeBytesBefore)} | {FormatGiB(result.Runner.FreeBytesAfter)} |");
        }

        if (rows.Count == 0)
            return;

        markdown.AppendLine();
        markdown.AppendLine("## Storage Summary");
        markdown.AppendLine();
        markdown.AppendLine("| Section | Status | Planned | Deleted | Failed | Before | After |");
        markdown.AppendLine("| --- | --- | ---: | ---: | ---: | --- | --- |");
        foreach (var row in rows)
            markdown.AppendLine(row);
    }

    private static void AppendArtifactDetails(StringBuilder markdown, GitHubArtifactCleanupResult? result)
    {
        if (result is null)
            return;

        AppendArtifactTable(markdown, "Planned artifacts", result.Planned);
        AppendArtifactTable(markdown, "Deleted artifacts", result.Deleted);
        AppendArtifactTable(markdown, "Failed artifacts", result.Failed);
    }

    private static void AppendArtifactTable(StringBuilder markdown, string title, IReadOnlyList<GitHubArtifactCleanupItem> items)
    {
        if (items.Count == 0)
            return;

        markdown.AppendLine();
        markdown.AppendLine("<details>");
        markdown.AppendLine($"<summary>{title} ({items.Count})</summary>");
        markdown.AppendLine();
        markdown.AppendLine("| Name | Size | Created | Updated | Reason | Delete status |");
        markdown.AppendLine("| --- | ---: | --- | --- | --- | --- |");

        foreach (var item in items.Take(20))
        {
            markdown.AppendLine($"| {EscapeCell(item.Name)} | {FormatGiB(item.SizeInBytes)} | {FormatDate(item.CreatedAt)} | {FormatDate(item.UpdatedAt)} | {EscapeCell(item.Reason)} | {EscapeCell(DeleteState(item.DeleteStatusCode, item.DeleteError))} |");
        }

        AppendTruncationNotice(markdown, items.Count);
        markdown.AppendLine();
        markdown.AppendLine("</details>");
    }

    private static void AppendCacheDetails(StringBuilder markdown, GitHubActionsCacheCleanupResult? result)
    {
        if (result is null)
            return;

        AppendCacheTable(markdown, "Planned caches", result.Planned);
        AppendCacheTable(markdown, "Deleted caches", result.Deleted);
        AppendCacheTable(markdown, "Failed caches", result.Failed);
    }

    private static void AppendCacheTable(StringBuilder markdown, string title, IReadOnlyList<GitHubActionsCacheCleanupItem> items)
    {
        if (items.Count == 0)
            return;

        markdown.AppendLine();
        markdown.AppendLine("<details>");
        markdown.AppendLine($"<summary>{title} ({items.Count})</summary>");
        markdown.AppendLine();
        markdown.AppendLine("| Key | Size | Created | Last accessed | Reason | Delete status |");
        markdown.AppendLine("| --- | ---: | --- | --- | --- | --- |");

        foreach (var item in items.Take(20))
        {
            markdown.AppendLine($"| {EscapeCell(item.Key)} | {FormatGiB(item.SizeInBytes)} | {FormatDate(item.CreatedAt)} | {FormatDate(item.LastAccessedAt)} | {EscapeCell(item.Reason)} | {EscapeCell(DeleteState(item.DeleteStatusCode, item.DeleteError))} |");
        }

        AppendTruncationNotice(markdown, items.Count);
        markdown.AppendLine();
        markdown.AppendLine("</details>");
    }

    private static void AppendRunnerDetails(StringBuilder markdown, RunnerHousekeepingResult? result)
    {
        if (result is null || result.Steps.Length == 0)
            return;

        markdown.AppendLine();
        markdown.AppendLine("<details>");
        markdown.AppendLine($"<summary>Runner steps ({result.Steps.Length})</summary>");
        markdown.AppendLine();
        markdown.AppendLine("| Step | Status | Entries | Message |");
        markdown.AppendLine("| --- | --- | ---: | --- |");

        foreach (var step in result.Steps.Take(20))
        {
            markdown.AppendLine($"| {EscapeCell(step.Title)} | {(step.Success ? "ok" : "warning")} | {step.EntriesAffected} | {EscapeCell(step.Message)} |");
        }

        AppendTruncationNotice(markdown, result.Steps.Length);
        markdown.AppendLine();
        markdown.AppendLine("</details>");
    }

    private static void AppendTruncationNotice(StringBuilder markdown, int count)
    {
        if (count > 20)
        {
            markdown.AppendLine();
            markdown.AppendLine("_Showing first 20 items._");
        }
    }

    private static string CountAndBytes(int count, long bytes)
        => $"{count} ({FormatGiB(bytes)})";

    private static string CacheUsage(GitHubActionsCacheUsage? usage)
        => usage is null ? "-" : $"{usage.ActiveCachesCount} caches / {FormatGiB(usage.ActiveCachesSizeInBytes)}";

    private static string Status(bool success, int failed)
        => !success ? "failed" : failed > 0 ? "warnings" : "ok";

    private static string DeleteState(int? statusCode, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return $"failed ({statusCode?.ToString() ?? "error"})";
        if (statusCode.HasValue)
            return $"deleted ({statusCode.Value})";
        return "planned";
    }

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToString("u") ?? "-";

    private static string EscapeCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        var text = value ?? string.Empty;
        return text.Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", "<br/>");
    }

    private static string FormatGiB(long bytes)
    {
        if (bytes <= 0)
            return "0.0 GiB";

        return $"{bytes / (double)(1L << 30):N1} GiB";
    }

    private static void WriteUtf8(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
    }
}
