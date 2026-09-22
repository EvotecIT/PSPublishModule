using System.Diagnostics;
using System.Text;

namespace PowerForge;

/// <summary>
/// Shared PowerShell-host fallback for invoking a discovered project script, or <c>Invoke-ProjectBuild</c> when a config path is supplied.
/// </summary>
public sealed class ProjectBuildCommandHostService
{
    private readonly IPowerShellRunner _powerShellRunner;

    /// <summary>
    /// Creates a new service using the default PowerShell runner.
    /// </summary>
    public ProjectBuildCommandHostService()
        : this(new PowerShellRunner())
    {
    }

    internal ProjectBuildCommandHostService(IPowerShellRunner powerShellRunner)
    {
        _powerShellRunner = powerShellRunner ?? throw new ArgumentNullException(nameof(powerShellRunner));
    }

    /// <summary>
    /// Generates a plan through the discovered script or <c>Invoke-ProjectBuild -Plan</c>.
    /// </summary>
    public Task<ProjectBuildCommandHostExecutionResult> GeneratePlanAsync(ProjectBuildCommandPlanRequest request, CancellationToken cancellationToken = default)
    {
        FrameworkCompatibility.NotNull(request, nameof(request));
        ValidateRequiredPath(request.RepositoryRoot, nameof(request.RepositoryRoot));
        ValidateRequiredPath(request.PlanOutputPath, nameof(request.PlanOutputPath));
        ValidateRequiredPath(request.ModulePath, nameof(request.ModulePath));

        var scriptFallback = string.IsNullOrWhiteSpace(request.ConfigPath) && !string.IsNullOrWhiteSpace(request.ScriptPath);
        var command = new StringBuilder(scriptFallback
            ? $"& {QuoteLiteral(request.ScriptPath!)} -Plan:$true -Build:$false -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false -PlanPath "
            : "Invoke-ProjectBuild -Plan:$true -PlanPath ");
        command.Append(QuoteLiteral(request.PlanOutputPath));
        if (!string.IsNullOrWhiteSpace(request.ConfigPath))
        {
            var configPath = request.ConfigPath!;
            command.Append(" -ConfigPath ").Append(QuoteLiteral(configPath));
        }

        return RunCommandAsync(
            request.RepositoryRoot,
            BuildScript(request.RepositoryRoot, request.ModulePath, command.ToString()),
            cancellationToken);
    }

    /// <summary>
    /// Requests a build through the discovered script or <c>Invoke-ProjectBuild -Build</c> with publish switches disabled.
    /// </summary>
    public Task<ProjectBuildCommandHostExecutionResult> ExecuteBuildAsync(ProjectBuildCommandBuildRequest request, CancellationToken cancellationToken = default)
    {
        FrameworkCompatibility.NotNull(request, nameof(request));
        ValidateRequiredPath(request.RepositoryRoot, nameof(request.RepositoryRoot));
        ValidateRequiredPath(request.ModulePath, nameof(request.ModulePath));

        var scriptFallback = string.IsNullOrWhiteSpace(request.ConfigPath) && !string.IsNullOrWhiteSpace(request.ScriptPath);
        var command = new StringBuilder(scriptFallback
            ? $"& {QuoteLiteral(request.ScriptPath!)} -Build:$true -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false"
            : "Invoke-ProjectBuild -Build:$true -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false");
        if (!string.IsNullOrWhiteSpace(request.ConfigPath))
        {
            var configPath = request.ConfigPath!;
            command.Append(" -ConfigPath ").Append(QuoteLiteral(configPath));
        }

        return RunCommandAsync(
            request.RepositoryRoot,
            BuildScript(request.RepositoryRoot, request.ModulePath, command.ToString()),
            cancellationToken,
            request.OutputLineReceived,
            request.ErrorLineReceived);
    }

    private async Task<ProjectBuildCommandHostExecutionResult> RunCommandAsync(
        string workingDirectory,
        string script,
        CancellationToken cancellationToken,
        Action<string>? outputLineReceived = null,
        Action<string>? errorLineReceived = null)
    {
        var startedAt = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var runRequest = PowerShellRunRequest.ForCommand(
            commandText: script,
            timeout: TimeSpan.FromMinutes(15),
            preferPwsh: !FrameworkCompatibility.IsWindows(),
            workingDirectory: workingDirectory,
            environmentVariables: null,
            executableOverride: Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE"),
            captureOutput: true,
            captureError: true,
            outputLineReceived: outputLineReceived,
            errorLineReceived: errorLineReceived);
        var result = _powerShellRunner is ICancellablePowerShellRunner cancellableRunner
            ? await cancellableRunner.RunAsync(runRequest, cancellationToken).ConfigureAwait(false)
            : await Task.Run(() => _powerShellRunner.Run(runRequest), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        startedAt.Stop();

        return new ProjectBuildCommandHostExecutionResult {
            ExitCode = result.ExitCode,
            Duration = startedAt.Elapsed,
            StandardOutput = result.StdOut,
            StandardError = result.StdErr,
            Executable = result.Executable
        };
    }

    private static string BuildScript(string repositoryRoot, string modulePath, string command)
        => string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            BuildModuleImportClause(modulePath),
            $"Set-Location -LiteralPath {QuoteLiteral(repositoryRoot)}",
            command
        });

    private static string BuildModuleImportClause(string modulePath)
        => File.Exists(modulePath)
            ? $"try {{ Import-Module {QuoteLiteral(modulePath)} -Force -ErrorAction Stop }} catch {{ Import-Module PSPublishModule -Force -ErrorAction Stop }}"
            : "Import-Module PSPublishModule -Force -ErrorAction Stop";

    private static string QuoteLiteral(string value)
        => $"'{(value ?? string.Empty).Replace("'", "''")}'";

    private static void ValidateRequiredPath(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{argumentName} is required.", argumentName);
    }
}
