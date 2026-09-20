using System.Text.RegularExpressions;
using PowerForgeStudio.Domain.Automation;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Orchestrator.Automation;

internal sealed partial class GitHubWorkflowAutomationSource
{
    private const long MaximumWorkflowBytes = 1_048_576;
    private readonly IWorkspaceRepositorySource _repositories;

    internal GitHubWorkflowAutomationSource(IWorkspaceRepositorySource? repositories = null)
        => _repositories = repositories ?? new WorkspaceRepositorySource();

    internal async Task<AutomationSourceResult> ReadAsync(string workspaceRoot, CancellationToken token)
    {
        var repositories = await _repositories.DiscoverAsync(workspaceRoot, token).ConfigureAwait(false);
        var entries = new List<WorkspaceAutomationEntry>();
        var skipped = 0;
        foreach (var repository in repositories.Where(static item => !item.IsWorktree))
        {
            token.ThrowIfCancellationRequested();
            var workflowRoot = Path.Combine(repository.RootPath, ".github", "workflows");
            if (!Directory.Exists(workflowRoot)) continue;
            foreach (var file in Directory.EnumerateFiles(workflowRoot, "*", SearchOption.TopDirectoryOnly)
                         .Where(static path => path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                                               path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (new FileInfo(file).Length > MaximumWorkflowBytes) { skipped++; continue; }
                    var lines = await File.ReadAllLinesAsync(file, token).ConfigureAwait(false);
                    foreach (var cron in ReadCronDefinitions(lines))
                    {
                        var relative = Path.GetRelativePath(repository.RootPath, file);
                        entries.Add(new($"{repository.RootPath}|{relative}|{cron.Line}",
                            Path.GetFileNameWithoutExtension(file), "GitHub Actions", relative, repository.Name,
                            cron.Expression, "Definition only", null, null, "Runtime not checked", false, true, true,
                            file, $"Local workflow definition at line {cron.Line}. GitHub runtime state is not inferred."));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }
        var ordered = entries.OrderBy(static entry => entry.Project, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var message = skipped == 0
            ? "Local workflow definitions only; enablement, last run and next occurrence require GitHub evidence."
            : $"Local definitions only. {skipped} workflow file(s) could not be read; GitHub runtime evidence is not connected.";
        return new(ordered, new("GitHub Actions", skipped == 0 ? "Definitions" : "Partial", ordered.Length, message));
    }

    internal static IReadOnlyList<CronDefinition> ReadCronDefinitions(IReadOnlyList<string> lines)
    {
        var found = new List<CronDefinition>();
        var onIndent = -1;
        var scheduleIndent = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            var raw = lines[index];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var indent = raw.Length - raw.TrimStart().Length;
            if (OnPattern().IsMatch(trimmed))
            {
                onIndent = indent;
                scheduleIndent = -1;
                continue;
            }
            if (onIndent >= 0 && indent <= onIndent)
            {
                onIndent = -1;
                scheduleIndent = -1;
            }
            if (onIndent >= 0 && SchedulePattern().IsMatch(trimmed))
            {
                scheduleIndent = indent;
                continue;
            }
            if (scheduleIndent >= 0 && indent <= scheduleIndent)
            {
                scheduleIndent = -1;
                continue;
            }
            if (scheduleIndent < 0) continue;
            var match = CronPattern().Match(trimmed);
            if (!match.Success) continue;
            var expression = TrimYamlScalar(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(expression)) found.Add(new(expression, index + 1));
        }
        return found;
    }

    private static string TrimYamlScalar(string value)
    {
        var trimmed = value.Trim();
        char quote = '\0';
        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            if (quote == '\0' && current is '\'' or '"') quote = current;
            else if (quote == current) quote = '\0';
            else if (quote == '\0' && current == '#' && (index == 0 || char.IsWhiteSpace(trimmed[index - 1])))
            {
                trimmed = trimmed[..index].TrimEnd();
                break;
            }
        }
        if (trimmed.Length >= 2 && ((trimmed[0] == '\'' && trimmed[^1] == '\'') ||
                                    (trimmed[0] == '"' && trimmed[^1] == '"')))
            return trimmed[1..^1];
        return trimmed;
    }

    [GeneratedRegex("^(?:on|['\"]on['\"]):\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OnPattern();
    [GeneratedRegex("^schedule:\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SchedulePattern();
    [GeneratedRegex("^-\\s*cron:\\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CronPattern();

    internal sealed record CronDefinition(string Expression, int Line);
}
