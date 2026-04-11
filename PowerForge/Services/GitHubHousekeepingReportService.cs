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

        var markdown = new MarkdownDocumentBuilder(blankLineAfterFrontMatter: true);
        markdown.RawLine("# PowerForge GitHub Housekeeping Report");
        markdown.BlankLine();

        if (report.Result is null)
        {
            markdown.RawLine("> ❌ **Housekeeping failed before section results were produced**");
            markdown.BlankLine();
            markdown.RawLine(BuildFieldTable(new[]
            {
                new[] { "Success", report.Success ? "Yes" : "No" },
                new[] { "Exit code", report.ExitCode.ToString() },
                new[] { "Error", NormalizeCell(report.Error) }
            }).TrimEnd());
            return markdown.ToString();
        }

        var result = report.Result;
        var repository = string.IsNullOrWhiteSpace(result.Repository) ? "(runner-only)" : result.Repository;
        markdown.RawLine($"> {(report.Success ? "✅" : "❌")} **{repository}** ran in **{(result.DryRun ? "dry-run" : "apply")}** mode");
        markdown.BlankLine();
        var summaryRows = new List<string[]>
        {
            new[] { "Success", report.Success ? "Yes" : "No" },
            new[] { "Requested sections", NormalizeCell(string.Join(", ", result.RequestedSections)) },
            new[] { "Completed sections", NormalizeCell(string.Join(", ", result.CompletedSections)) },
            new[] { "Failed sections", NormalizeCell(string.Join(", ", result.FailedSections)) }
        };
        if (!string.IsNullOrWhiteSpace(result.Message))
            summaryRows.Add(new[] { "Message", NormalizeCell(result.Message) });
        markdown.RawLine(BuildFieldTable(summaryRows).TrimEnd());

        AppendStorageSummary(markdown, result);
        AppendSelectionSummary(markdown, result);
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

    /// <summary>
    /// Writes pre-rendered Markdown report content to disk using UTF-8 without BOM.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="markdown">Markdown content to persist.</param>
    public void WriteMarkdownReport(string path, string markdown)
        => WriteUtf8(path, markdown);

    private static void AppendStorageSummary(MarkdownDocumentBuilder markdown, GitHubHousekeepingResult result)
    {
        var table = new MarkdownTableBuilder(
            ["Section", "Status", "Planned", "Deleted", "Failed", "Before", "After"],
            [MarkdownTableAlignment.Left, MarkdownTableAlignment.Left, MarkdownTableAlignment.Right, MarkdownTableAlignment.Right, MarkdownTableAlignment.Right, MarkdownTableAlignment.Left, MarkdownTableAlignment.Left]);
        var hasRows = false;

        if (result.Artifacts is not null)
        {
            table.AddRow(
                "Artifacts",
                StorageStatus(result.Artifacts.Success, result.Artifacts.FailedDeletes, result.Artifacts.MatchedArtifacts, result.Artifacts.PlannedDeletes, result.Artifacts.DeletedArtifacts),
                CountAndBytes(result.Artifacts.PlannedDeletes, result.Artifacts.PlannedDeleteBytes),
                CountAndBytes(result.Artifacts.DeletedArtifacts, result.Artifacts.DeletedBytes),
                result.Artifacts.FailedDeletes.ToString(),
                "-",
                "-");
            hasRows = true;
        }

        if (result.Caches is not null)
        {
            table.AddRow(
                "Caches",
                StorageStatus(result.Caches.Success, result.Caches.FailedDeletes, result.Caches.MatchedCaches, result.Caches.PlannedDeletes, result.Caches.DeletedCaches),
                CountAndBytes(result.Caches.PlannedDeletes, result.Caches.PlannedDeleteBytes),
                CountAndBytes(result.Caches.DeletedCaches, result.Caches.DeletedBytes),
                result.Caches.FailedDeletes.ToString(),
                CacheUsage(result.Caches.UsageBefore),
                CacheUsage(result.Caches.UsageAfter));
            hasRows = true;
        }

        if (result.Runner is not null)
        {
            table.AddRow(
                "Runner",
                result.Runner.Success ? "ok" : "failed",
                "-",
                "-",
                result.Runner.Success ? "0" : "1",
                FormatGiB(result.Runner.FreeBytesBefore),
                FormatGiB(result.Runner.FreeBytesAfter));
            hasRows = true;
        }

        if (!hasRows)
            return;

        markdown.BlankLine();
        markdown.RawLine("## Storage Summary");
        markdown.BlankLine();
        markdown.RawLine(table.ToString().TrimEnd());
    }

    private static void AppendSelectionSummary(MarkdownDocumentBuilder markdown, GitHubHousekeepingResult result)
    {
        var table = new MarkdownTableBuilder(
            ["Section", "Scanned", "Matched", "Kept recent", "Kept age", "Eligible", "Note"],
            [MarkdownTableAlignment.Left, MarkdownTableAlignment.Right, MarkdownTableAlignment.Right, MarkdownTableAlignment.Right, MarkdownTableAlignment.Right, MarkdownTableAlignment.Right, MarkdownTableAlignment.Left]);
        var hasRows = false;

        if (result.Artifacts is not null)
        {
            table.AddRow(
                "Artifacts",
                result.Artifacts.ScannedArtifacts.ToString(),
                result.Artifacts.MatchedArtifacts.ToString(),
                result.Artifacts.KeptByRecentWindow.ToString(),
                result.Artifacts.KeptByAgeThreshold.ToString(),
                result.Artifacts.PlannedDeletes.ToString(),
                SelectionNote(result.Artifacts.MatchedArtifacts, result.Artifacts.PlannedDeletes, result.Artifacts.KeptByRecentWindow, result.Artifacts.KeptByAgeThreshold));
            hasRows = true;
        }

        if (result.Caches is not null)
        {
            table.AddRow(
                "Caches",
                result.Caches.ScannedCaches.ToString(),
                result.Caches.MatchedCaches.ToString(),
                result.Caches.KeptByRecentWindow.ToString(),
                result.Caches.KeptByAgeThreshold.ToString(),
                result.Caches.PlannedDeletes.ToString(),
                SelectionNote(result.Caches.MatchedCaches, result.Caches.PlannedDeletes, result.Caches.KeptByRecentWindow, result.Caches.KeptByAgeThreshold));
            hasRows = true;
        }

        if (!hasRows)
            return;

        markdown.BlankLine();
        markdown.RawLine("## Selection Breakdown");
        markdown.BlankLine();
        markdown.RawLine(table.ToString().TrimEnd());
    }

    private static void AppendArtifactDetails(MarkdownDocumentBuilder markdown, GitHubArtifactCleanupResult? result)
    {
        if (result is null)
            return;

        AppendArtifactTable(markdown, "Planned artifacts", result.Planned);
        AppendArtifactTable(markdown, "Deleted artifacts", result.Deleted);
        AppendArtifactTable(markdown, "Failed artifacts", result.Failed);
    }

    private static void AppendArtifactTable(MarkdownDocumentBuilder markdown, string title, IReadOnlyList<GitHubArtifactCleanupItem> items)
    {
        if (items.Count == 0)
            return;

        var table = new MarkdownTableBuilder(
            ["Name", "Size", "Created", "Updated", "Reason", "Delete status"],
            [MarkdownTableAlignment.Left, MarkdownTableAlignment.Right, MarkdownTableAlignment.Left, MarkdownTableAlignment.Left, MarkdownTableAlignment.Left, MarkdownTableAlignment.Left]);

        foreach (var item in items.Take(20))
        {
            table.AddRow(
                NormalizeCell(item.Name),
                FormatGiB(item.SizeInBytes),
                FormatDate(item.CreatedAt),
                FormatDate(item.UpdatedAt),
                NormalizeCell(item.Reason),
                NormalizeCell(DeleteState(item.DeleteStatusCode, item.DeleteError)));
        }

        markdown.BlankLine();
        markdown.RawLine("<details>");
        markdown.RawLine($"<summary>{title} ({items.Count})</summary>");
        markdown.BlankLine();
        markdown.RawLine(table.ToString().TrimEnd());
        AppendTruncationNotice(markdown, items.Count);
        markdown.BlankLine();
        markdown.RawLine("</details>");
    }

    private static void AppendCacheDetails(MarkdownDocumentBuilder markdown, GitHubActionsCacheCleanupResult? result)
    {
        if (result is null)
            return;

        AppendCacheTable(markdown, "Planned caches", result.Planned);
        AppendCacheTable(markdown, "Deleted caches", result.Deleted);
        AppendCacheTable(markdown, "Failed caches", result.Failed);
    }

    private static void AppendCacheTable(MarkdownDocumentBuilder markdown, string title, IReadOnlyList<GitHubActionsCacheCleanupItem> items)
    {
        if (items.Count == 0)
            return;

        var table = new MarkdownTableBuilder(
            ["Key", "Size", "Created", "Last accessed", "Reason", "Delete status"],
            [MarkdownTableAlignment.Left, MarkdownTableAlignment.Right, MarkdownTableAlignment.Left, MarkdownTableAlignment.Left, MarkdownTableAlignment.Left, MarkdownTableAlignment.Left]);

        foreach (var item in items.Take(20))
        {
            table.AddRow(
                NormalizeCell(item.Key),
                FormatGiB(item.SizeInBytes),
                FormatDate(item.CreatedAt),
                FormatDate(item.LastAccessedAt),
                NormalizeCell(item.Reason),
                NormalizeCell(DeleteState(item.DeleteStatusCode, item.DeleteError)));
        }

        markdown.BlankLine();
        markdown.RawLine("<details>");
        markdown.RawLine($"<summary>{title} ({items.Count})</summary>");
        markdown.BlankLine();
        markdown.RawLine(table.ToString().TrimEnd());
        AppendTruncationNotice(markdown, items.Count);
        markdown.BlankLine();
        markdown.RawLine("</details>");
    }

    private static void AppendRunnerDetails(MarkdownDocumentBuilder markdown, RunnerHousekeepingResult? result)
    {
        if (result is null || result.Steps.Length == 0)
            return;

        var table = new MarkdownTableBuilder(
            ["Step", "Status", "Entries", "Message"],
            [MarkdownTableAlignment.Left, MarkdownTableAlignment.Left, MarkdownTableAlignment.Right, MarkdownTableAlignment.Left]);

        foreach (var step in result.Steps.Take(20))
        {
            table.AddRow(
                NormalizeCell(step.Title),
                step.Success ? "ok" : "warning",
                step.EntriesAffected.ToString(),
                NormalizeCell(step.Message));
        }

        markdown.BlankLine();
        markdown.RawLine("<details>");
        markdown.RawLine($"<summary>Runner steps ({result.Steps.Length})</summary>");
        markdown.BlankLine();
        markdown.RawLine(table.ToString().TrimEnd());
        AppendTruncationNotice(markdown, result.Steps.Length);
        markdown.BlankLine();
        markdown.RawLine("</details>");
    }

    private static void AppendTruncationNotice(MarkdownDocumentBuilder markdown, int count)
    {
        if (count > 20)
        {
            markdown.BlankLine();
            markdown.RawLine("_Showing first 20 items._");
        }
    }

    private static string BuildFieldTable(IEnumerable<string[]> rows)
    {
        var table = new MarkdownTableBuilder(["Field", "Value"]);
        foreach (var row in rows)
            table.AddRow(row[0], row[1]);
        return table.ToString();
    }

    private static string CountAndBytes(int count, long bytes)
        => $"{count} ({FormatGiB(bytes)})";

    private static string CacheUsage(GitHubActionsCacheUsage? usage)
        => usage is null ? "-" : $"{usage.ActiveCachesCount} caches / {FormatGiB(usage.ActiveCachesSizeInBytes)}";

    private static string StorageStatus(bool success, int failed, int matched, int eligible, int deleted)
    {
        if (!success)
            return "failed";
        if (failed > 0)
            return "warnings";
        if (deleted > 0)
            return "cleaned";
        if (eligible > 0)
            return "eligible";
        if (matched > 0)
            return "nothing eligible";
        return "no matches";
    }

    private static string SelectionNote(int matched, int eligible, int keptRecent, int keptAge)
    {
        if (matched == 0)
            return "nothing matched the current filters";
        if (eligible > 0)
            return "matched items are eligible for cleanup";
        if (keptRecent > 0 || keptAge > 0)
            return "all matched items were retained by current policy";
        return "nothing eligible";
    }

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

        var text = value!;
        return text.Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", "<br/>");
    }

    private static string NormalizeCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        return value!.Replace("\r", " ")
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
