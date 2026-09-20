using PowerForge;
using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

internal sealed class ToolchainConnectionSource : IWorkspaceConnectionSource
{
    private readonly IProcessRunner _runner;
    public string Provider => "Local toolchains";

    internal ToolchainConnectionSource(IProcessRunner? runner = null) => _runner = runner ?? new ProcessRunner();

    public async Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        ToolProbe[] probes =
        [
            new("git", "Git", ["--version"], ["Repository operations", "Worktrees", "Commits"]),
            new("dotnet", ".NET SDK", ["--version"], ["Build", "Test", "NuGet pack"]),
            new("pwsh", "PowerShell", ["--version"], ["Module builds", "Scripts", "PSGallery"]),
            new("gh", "GitHub CLI", ["--version"], ["Pull requests", "Issues", "Releases"])
        ];
        var entries = await Task.WhenAll(probes.Select(probe => InspectAsync(probe, workspaceRoot, cancellationToken))).ConfigureAwait(false);
        var verified = entries.Count(static entry => entry.IsVerified);
        return new ConnectionSourceResult(entries, new WorkspaceConnectionSourceState(
            Provider, verified == entries.Length ? "Available" : "Partial", entries.Length,
            $"{verified} of {entries.Length} build tools responded to version checks."));
    }

    private async Task<WorkspaceConnectionEntry> InspectAsync(ToolProbe probe, string root, CancellationToken token)
    {
        var request = new ProcessRunRequest(probe.Executable, root, probe.Arguments, TimeSpan.FromSeconds(5))
        {
            MaxCapturedOutputCharacters = 8_192,
            RequireDirectStart = true
        };
        var result = await _runner.RunAsync(request, token).ConfigureAwait(false);
        var version = FirstLine(result.StdOut, result.StdErr);
        var state = result.Succeeded ? "Verified" : "Unavailable";
        return new WorkspaceConnectionEntry(
            "toolchain:" + probe.Executable, probe.Name, Provider, "Toolchains", state,
            "Local process", "No credential required", probe.Capabilities,
            result.Succeeded ? DateTimeOffset.UtcNow : null,
            result.Succeeded ? $"{version}. Read-only version check completed." : "Executable did not complete a version check.",
            "Installed tool");
    }

    private static string FirstLine(params string[] values)
    {
        foreach (var value in values)
        {
            var line = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(line)) return line.Length <= 120 ? line : line[..120];
        }
        return "Version reported";
    }

    private sealed record ToolProbe(string Executable, string Name, IReadOnlyList<string> Arguments, IReadOnlyList<string> Capabilities);
}
