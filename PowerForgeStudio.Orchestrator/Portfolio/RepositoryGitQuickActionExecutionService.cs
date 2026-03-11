using System.Diagnostics;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryGitQuickActionExecutionService : IRepositoryGitQuickActionExecutionService
{
    private readonly Func<string, string, CancellationToken, Task<PowerShellExecutionResult>> _runPowerShellAsync;

    public RepositoryGitQuickActionExecutionService()
        : this(null)
    {
    }

    internal RepositoryGitQuickActionExecutionService(
        Func<string, string, CancellationToken, Task<PowerShellExecutionResult>>? runPowerShellAsync)
    {
        var runner = new PowerShellCommandRunner();
        _runPowerShellAsync = runPowerShellAsync
            ?? ((workingDirectory, script, cancellationToken) => runner.RunCommandAsync(workingDirectory, script, cancellationToken));
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

            var result = await _runPowerShellAsync(repositoryRoot, action.Payload, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0
                ? new RepositoryGitQuickActionExecutionResult(
                    Succeeded: true,
                    Summary: $"{action.Title} completed successfully.",
                    OutputTail: Tail(result.StandardOutput),
                    ErrorTail: Tail(result.StandardError))
                : new RepositoryGitQuickActionExecutionResult(
                    Succeeded: false,
                    Summary: $"{action.Title} failed with exit code {result.ExitCode}.",
                    OutputTail: Tail(result.StandardOutput),
                    ErrorTail: Tail(result.StandardError));
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
}
