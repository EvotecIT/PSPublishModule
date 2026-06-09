using System.Diagnostics;
using PowerForge;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryGitQuickActionExecutionService : IRepositoryGitQuickActionExecutionService
{
    private readonly Func<GitCommandRequest, CancellationToken, Task<GitCommandResult>> _runGitAsync;

    public RepositoryGitQuickActionExecutionService()
        : this(null)
    {
    }

    internal RepositoryGitQuickActionExecutionService(
        Func<GitCommandRequest, CancellationToken, Task<GitCommandResult>>? runGitAsync)
    {
        var gitClient = new GitClient();
        _runGitAsync = runGitAsync
            ?? ((request, cancellationToken) => gitClient.RunAsync(request, cancellationToken));
    }

    public async Task<RepositoryGitQuickActionExecutionResult> ExecuteAsync(
        string repositoryRoot,
        RepositoryGitQuickAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            if (action.Kind == RepositoryGitQuickActionKind.BrowserUrl)
            {
                Process.Start(new ProcessStartInfo {
                    FileName = action.Payload,
                    UseShellExecute = true
                });

                return new RepositoryGitQuickActionExecutionResult(
                    Succeeded: true,
                    Summary: $"Opened {action.Title}.");
            }

            var request = BuildRequest(repositoryRoot, action);
            if (request is null)
            {
                return new RepositoryGitQuickActionExecutionResult(
                    Succeeded: false,
                    Summary: $"{action.Title} is not mapped to a supported reusable git operation.",
                    ErrorTail: action.Payload);
            }

            var result = await _runGitAsync(request, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0
                ? new RepositoryGitQuickActionExecutionResult(
                    Succeeded: true,
                    Summary: $"{action.Title} completed successfully.",
                    OutputTail: Tail(result.StdOut),
                    ErrorTail: Tail(result.StdErr))
                : new RepositoryGitQuickActionExecutionResult(
                    Succeeded: false,
                    Summary: $"{action.Title} failed with exit code {result.ExitCode}.",
                    OutputTail: Tail(result.StdOut),
                    ErrorTail: Tail(result.StdErr));
        }
        catch (Exception exception)
        {
            return new RepositoryGitQuickActionExecutionResult(
                Succeeded: false,
                Summary: $"{action.Title} failed to start.",
                ErrorTail: exception.Message);
        }
    }

    private static string? Tail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(
            Environment.NewLine,
            value.Split(["\r\n", "\n"], StringSplitOptions.None).TakeLast(8));
    }

    private static GitCommandRequest? BuildRequest(string repositoryRoot, RepositoryGitQuickAction action)
    {
        var gitOperation = action.GitOperation ?? InferOperation(action.Payload);
        if (gitOperation is null)
            return null;

        return gitOperation.Value switch
        {
            RepositoryGitOperationKind.StatusShortBranch => new GitCommandRequest(repositoryRoot, GitCommandKind.StatusShortBranch),
            RepositoryGitOperationKind.StatusShort => new GitCommandRequest(repositoryRoot, GitCommandKind.StatusShort),
            RepositoryGitOperationKind.ShowTopLevel => new GitCommandRequest(repositoryRoot, GitCommandKind.ShowTopLevel),
            RepositoryGitOperationKind.PullRebase => new GitCommandRequest(repositoryRoot, GitCommandKind.PullRebase),
            RepositoryGitOperationKind.CreateBranch => new GitCommandRequest(
                repositoryRoot,
                GitCommandKind.CreateBranch,
                branchName: action.GitOperationArgument ?? ParseBranchName(action.Payload)),
            RepositoryGitOperationKind.SetUpstream => new GitCommandRequest(
                repositoryRoot,
                GitCommandKind.SetUpstream,
                branchName: action.GitOperationArgument ?? ParseSetUpstream(action.Payload).BranchName,
                remoteName: ParseSetUpstream(action.Payload).RemoteName),
            _ => null
        };
    }

    private static RepositoryGitOperationKind? InferOperation(string payload)
    {
        if (string.Equals(payload, "git status --short --branch", StringComparison.OrdinalIgnoreCase))
            return RepositoryGitOperationKind.StatusShortBranch;
        if (string.Equals(payload, "git status --short", StringComparison.OrdinalIgnoreCase))
            return RepositoryGitOperationKind.StatusShort;
        if (string.Equals(payload, "git rev-parse --show-toplevel", StringComparison.OrdinalIgnoreCase))
            return RepositoryGitOperationKind.ShowTopLevel;
        if (string.Equals(payload, "git pull --rebase", StringComparison.OrdinalIgnoreCase))
            return RepositoryGitOperationKind.PullRebase;
        if (payload.StartsWith("git switch -c ", StringComparison.OrdinalIgnoreCase))
            return RepositoryGitOperationKind.CreateBranch;
        if (payload.StartsWith("git push --set-upstream ", StringComparison.OrdinalIgnoreCase))
            return RepositoryGitOperationKind.SetUpstream;

        return null;
    }

    private static string? ParseBranchName(string payload)
    {
        const string prefix = "git switch -c ";
        return payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? payload[prefix.Length..].Trim()
            : null;
    }

    private static (string RemoteName, string? BranchName) ParseSetUpstream(string payload)
    {
        var parts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 5)
            return (parts[3], parts[4]);

        return ("origin", null);
    }
}
