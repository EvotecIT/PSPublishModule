using System.Diagnostics;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryGitQuickActionExecutionService : IRepositoryGitQuickActionExecutionService
{
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

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{EscapeForPowerShell(action.Payload)}\"",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            return process.ExitCode == 0
                ? new RepositoryGitQuickActionExecutionResult(
                    Succeeded: true,
                    Summary: $"{action.Title} completed successfully.",
                    OutputTail: Tail(output),
                    ErrorTail: Tail(error))
                : new RepositoryGitQuickActionExecutionResult(
                    Succeeded: false,
                    Summary: $"{action.Title} failed with exit code {process.ExitCode}.",
                    OutputTail: Tail(output),
                    ErrorTail: Tail(error));
        }
        catch (Exception exception)
        {
            return new RepositoryGitQuickActionExecutionResult(
                Succeeded: false,
                Summary: $"{action.Title} failed to start.",
                ErrorTail: exception.Message);
        }
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("\"", "`\"", StringComparison.Ordinal);

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
