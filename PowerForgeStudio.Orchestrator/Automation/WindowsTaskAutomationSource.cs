using System.Text;
using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Domain.Automation;

namespace PowerForgeStudio.Orchestrator.Automation;

internal sealed class WindowsTaskAutomationSource
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $items = @(Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' } | ForEach-Object {
            $task = $_
            $info = $null
            try { $info = $task | Get-ScheduledTaskInfo -ErrorAction Stop } catch { }
            $action = @($task.Actions)[0]
            $trigger = @($task.Triggers)[0]
            $last = if ($info -and $info.LastRunTime.Year -gt 2000) { $info.LastRunTime.ToString('o') } else { $null }
            $next = if ($info -and $info.NextRunTime.Year -gt 2000) { $info.NextRunTime.ToString('o') } else { $null }
            [pscustomobject]@{
                Id = $task.TaskPath + $task.TaskName
                Name = $task.TaskName
                TaskPath = $task.TaskPath
                State = [string]$task.State
                Description = [string]$task.Description
                LastRun = $last
                NextRun = $next
                LastResult = if ($info) { [long]$info.LastTaskResult } else { $null }
                Execute = if ($action) { [string]$action.Execute } else { '' }
                WorkingDirectory = if ($action) { [string]$action.WorkingDirectory } else { '' }
                TriggerType = if ($trigger) { [string]$trigger.CimClass.CimClassName } else { '' }
                StartBoundary = if ($trigger -and $trigger.StartBoundary) { ([datetime]$trigger.StartBoundary).ToString('o') } else { $null }
                RepetitionInterval = if ($trigger -and $trigger.Repetition) { [string]$trigger.Repetition.Interval } else { '' }
            }
        })
        ConvertTo-Json -InputObject $items -Compress -Depth 4
        """;

    private readonly IProcessRunner _runner;

    internal WindowsTaskAutomationSource(IProcessRunner? runner = null) => _runner = runner ?? new ProcessRunner();

    internal async Task<AutomationSourceResult> ReadAsync(string workspaceRoot, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            return Unavailable("Windows Task Scheduler is available only on Windows.");
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable))
            return Unavailable("Windows PowerShell with the ScheduledTasks module was not found.");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
        var request = new ProcessRunRequest(executable, workspaceRoot,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            TimeSpan.FromSeconds(30))
        {
            MaxCapturedOutputCharacters = 2_000_000,
            RequireDirectStart = true
        };
        var result = await _runner.RunAsync(request, token).ConfigureAwait(false);
        if (!result.Succeeded)
            return Unavailable(result.TimedOut ? "Windows task inspection timed out." : "Windows Task Scheduler could not be inspected.");
        try
        {
            var rows = JsonSerializer.Deserialize<WindowsTaskRow[]>(result.StdOut, JsonOptions) ?? [];
            var root = Path.GetFullPath(workspaceRoot);
            var entries = rows.Select(row => Map(row, root)).OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(entries, new("Windows Task Scheduler", "Available", entries.Length,
                "Runtime evidence is read locally. Action arguments are never collected."));
        }
        catch (JsonException)
        {
            return Unavailable("Windows Task Scheduler returned an unreadable inventory.");
        }
    }

    private static WorkspaceAutomationEntry Map(WindowsTaskRow row, string workspaceRoot)
    {
        var last = ParseDate(row.LastRun);
        var next = ParseDate(row.NextRun);
        var enabled = !string.Equals(row.State, "Disabled", StringComparison.OrdinalIgnoreCase);
        if (!enabled) next = null;
        var hasResult = last is not null && row.LastResult is not null;
        var failed = hasResult && row.LastResult != 0;
        var state = !enabled ? "Paused"
            : string.Equals(row.State, "Running", StringComparison.OrdinalIgnoreCase) ? "Running"
            : failed ? "Failed"
            : next is not null ? "Upcoming"
            : hasResult ? "Healthy"
            : "Idle";
        var relevant = ContainsProductName(row.Name) || IsWithin(workspaceRoot, row.Execute) || IsWithin(workspaceRoot, row.WorkingDirectory);
        var schedule = FormatSchedule(row.TriggerType, row.StartBoundary, row.RepetitionInterval);
        var lastResult = !hasResult ? "Not observed" : row.LastResult == 0 ? "Succeeded" : $"Result 0x{row.LastResult:X8}";
        return new(row.Id ?? row.Name ?? Guid.NewGuid().ToString("N"), row.Name ?? "Unnamed task",
            "Windows Task Scheduler", row.TaskPath ?? "\\", "", schedule, state, next, last, lastResult,
            true, enabled, relevant, row.TaskPath ?? "\\", row.Description ?? "");
    }

    private static string FormatSchedule(string? type, string? startBoundary, string? repetition)
    {
        var label = type?.Replace("MSFT_Task", "", StringComparison.Ordinal).Replace("Trigger", "", StringComparison.Ordinal) ?? "Trigger";
        var start = ParseDate(startBoundary);
        var parts = new List<string> { string.IsNullOrWhiteSpace(label) ? "Trigger" : label };
        if (start is not null) parts.Add(start.Value.ToLocalTime().ToString("HH:mm"));
        if (!string.IsNullOrWhiteSpace(repetition)) parts.Add(repetition);
        return string.Join(" · ", parts);
    }

    private static bool ContainsProductName(string? value)
        => !string.IsNullOrWhiteSpace(value) && new[] { "Evotec", "PowerForge", "ForgeFlow", "Codex", "PasswordSolutionX" }
            .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsWithin(string root, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate)) return false;
        try
        {
            var path = Path.GetFullPath(candidate);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(path, root, comparison) ||
                   path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static AutomationSourceResult Unavailable(string message)
        => new([], new("Windows Task Scheduler", "Unavailable", 0, message));

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record WindowsTaskRow(
        string? Id,
        string? Name,
        string? TaskPath,
        string? State,
        string? Description,
        string? LastRun,
        string? NextRun,
        long? LastResult,
        string? Execute,
        string? WorkingDirectory,
        string? TriggerType,
        string? StartBoundary,
        string? RepetitionInterval);
}
