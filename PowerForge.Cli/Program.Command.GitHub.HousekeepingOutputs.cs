using PowerForge;
using System.Text;

internal static partial class Program
{
    private sealed class GitHubHousekeepingOutputOptions
    {
        public string? JsonReportPath { get; init; }
        public string? MarkdownReportPath { get; init; }
        public string? StepSummaryPath { get; init; }
        public string? GitHubOutputPath { get; init; }
    }

    private static GitHubHousekeepingOutputOptions GetGitHubHousekeepingOutputOptions()
    {
        var baseDir = Directory.GetCurrentDirectory();
        return new GitHubHousekeepingOutputOptions
        {
            JsonReportPath = ResolvePathFromBaseNullable(baseDir, Environment.GetEnvironmentVariable("POWERFORGE_GITHUB_HOUSEKEEPING_REPORT_PATH")),
            MarkdownReportPath = ResolvePathFromBaseNullable(baseDir, Environment.GetEnvironmentVariable("POWERFORGE_GITHUB_HOUSEKEEPING_SUMMARY_PATH")),
            StepSummaryPath = ResolvePathFromBaseNullable(baseDir, Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY")),
            GitHubOutputPath = ResolvePathFromBaseNullable(baseDir, Environment.GetEnvironmentVariable("GITHUB_OUTPUT"))
        };
    }

    private static void WriteGitHubHousekeepingOutputs(
        GitHubHousekeepingReportService reports,
        GitHubHousekeepingReport report,
        GitHubHousekeepingOutputOptions options)
    {
        string? markdown = null;
        string GetMarkdown() => markdown ??= reports.BuildMarkdown(report);

        if (!string.IsNullOrWhiteSpace(options.JsonReportPath))
            reports.WriteJsonReport(options.JsonReportPath, report);

        if (!string.IsNullOrWhiteSpace(options.MarkdownReportPath))
            reports.WriteMarkdownReport(options.MarkdownReportPath, GetMarkdown());

        if (!string.IsNullOrWhiteSpace(options.StepSummaryPath))
            AppendUtf8(options.StepSummaryPath, GetMarkdown() + Environment.NewLine);

        if (!string.IsNullOrWhiteSpace(options.GitHubOutputPath))
        {
            if (!string.IsNullOrWhiteSpace(options.JsonReportPath))
                AppendUtf8(options.GitHubOutputPath, $"report-path={options.JsonReportPath}{Environment.NewLine}");
            if (!string.IsNullOrWhiteSpace(options.MarkdownReportPath))
                AppendUtf8(options.GitHubOutputPath, $"summary-path={options.MarkdownReportPath}{Environment.NewLine}");
        }
    }

    private static void AppendUtf8(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.AppendAllText(fullPath, content, new UTF8Encoding(false));
    }
}
