using PowerForge;
using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

internal sealed class GitHubConnectionSource : IWorkspaceConnectionSource
{
    private readonly IProcessRunner _runner;
    public string Provider => "GitHub";

    internal GitHubConnectionSource(IProcessRunner? runner = null) => _runner = runner ?? new ProcessRunner();

    public async Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var request = new ProcessRunRequest("gh", workspaceRoot,
            ["auth", "status", "--hostname", "github.com", "--active"], TimeSpan.FromSeconds(8))
        {
            MaxCapturedOutputCharacters = 16_384,
            RequireDirectStart = true
        };
        var result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        var unavailable = result.StartFailed || result.TimedOut || result.StandardOutputLimitExceeded || result.StandardErrorLimitExceeded;
        var state = result.Succeeded ? "Verified" : unavailable ? "Unavailable" : "Authentication required";
        var evidence = result.Succeeded
            ? "GitHub CLI confirmed an active github.com account. Command output was intentionally discarded."
            : unavailable ? "GitHub CLI was not available." : "GitHub CLI did not confirm an active github.com account.";
        WorkspaceConnectionEntry entry = new(
            "github:github.com", "GitHub", Provider, "Source control", state, "https://github.com",
            "GitHub CLI credential store", result.Succeeded ? ["Authenticated CLI session"] : unavailable ? [] : ["GitHub CLI available"],
            result.Succeeded ? DateTimeOffset.UtcNow : null, evidence, "GitHub CLI and PowerForge GitHub services");
        return new ConnectionSourceResult([entry], new WorkspaceConnectionSourceState(
            Provider, state, 1, evidence));
    }
}
