using System.Diagnostics;
using System.Text;

namespace PowerForge;

/// <summary>
/// Shared host service for invoking repository <c>Build-Module.ps1</c> scripts.
/// </summary>
public sealed class ModuleBuildHostService
{
    private readonly IPowerShellRunner _powerShellRunner;

    /// <summary>
    /// Creates a new host service using the default PowerShell runner.
    /// </summary>
    public ModuleBuildHostService()
        : this(new PowerShellRunner())
    {
    }

    internal ModuleBuildHostService(IPowerShellRunner powerShellRunner)
    {
        _powerShellRunner = powerShellRunner ?? throw new ArgumentNullException(nameof(powerShellRunner));
    }

    /// <summary>
    /// Exports pipeline JSON from a module build script.
    /// </summary>
    public Task<ModuleBuildHostExecutionResult> ExportPipelineJsonAsync(ModuleBuildHostExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredPath(request.RepositoryRoot, nameof(request.RepositoryRoot));
        ValidateRequiredPath(request.ScriptPath, nameof(request.ScriptPath));
        ValidateRequiredPath(request.ModulePath, nameof(request.ModulePath));
        ValidateRequiredPath(request.OutputPath, nameof(request.OutputPath));

        var script = BuildExportScript(request.RepositoryRoot, request.ScriptPath, request.OutputPath, request.ModulePath);
        return RunCommandAsync(request.RepositoryRoot, script, cancellationToken);
    }

    /// <summary>
    /// Executes a module build script while disabling signing-specific configuration overrides.
    /// </summary>
    public Task<ModuleBuildHostExecutionResult> ExecuteBuildAsync(ModuleBuildHostBuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredPath(request.RepositoryRoot, nameof(request.RepositoryRoot));
        ValidateRequiredPath(request.ScriptPath, nameof(request.ScriptPath));
        ValidateRequiredPath(request.ModulePath, nameof(request.ModulePath));

        var script = BuildBuildScript(request.RepositoryRoot, request.ScriptPath, request.ModulePath);
        return RunCommandAsync(request.RepositoryRoot, script, cancellationToken);
    }

    private async Task<ModuleBuildHostExecutionResult> RunCommandAsync(string workingDirectory, string script, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var result = await Task.Run(() => _powerShellRunner.Run(PowerShellRunRequest.ForCommand(
            commandText: script,
            timeout: TimeSpan.FromMinutes(15),
            preferPwsh: !OperatingSystem.IsWindows(),
            workingDirectory: workingDirectory,
            executableOverride: Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE"))), cancellationToken).ConfigureAwait(false);
        startedAt.Stop();

        return new ModuleBuildHostExecutionResult {
            ExitCode = result.ExitCode,
            Duration = startedAt.Elapsed,
            StandardOutput = result.StdOut,
            StandardError = result.StdErr,
            Executable = result.Executable
        };
    }

    private static string BuildExportScript(string repositoryRoot, string scriptPath, string outputPath, string modulePath)
    {
        var moduleRoot = Directory.GetParent(Path.GetDirectoryName(scriptPath)!)?.FullName ?? repositoryRoot;
        return string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            $"Set-Location -LiteralPath {QuoteLiteral(moduleRoot)}",
            BuildModuleImportClause(modulePath),
            $"$targetJson = {QuoteLiteral(outputPath)}",
            "function Invoke-ModuleBuild {",
            "  [CmdletBinding(PositionalBinding = $false)]",
            "  param(",
            "    [Parameter(Position = 0)][string]$ModuleName,",
            "    [Parameter(Position = 1)][scriptblock]$Settings,",
            "    [string]$Path,",
            "    [switch]$ExitCode,",
            "    [Parameter(ValueFromRemainingArguments = $true)][object[]]$RemainingArgs",
            "  )",
            "  if (-not $Settings -and $RemainingArgs.Count -gt 0 -and $RemainingArgs[0] -is [scriptblock]) {",
            "    $Settings = [scriptblock]$RemainingArgs[0]",
            "    if ($RemainingArgs.Count -gt 1) {",
            "      $RemainingArgs = $RemainingArgs[1..($RemainingArgs.Count - 1)]",
            "    } else {",
            "      $RemainingArgs = @()",
            "    }",
            "  }",
            "  $cmd = Get-Command -Name Invoke-ModuleBuild -CommandType Cmdlet -Module PSPublishModule",
            "  $invokeArgs = @{ ModuleName = $ModuleName; JsonOnly = $true; JsonPath = $targetJson; NoInteractive = $true }",
            "  if ($null -ne $Settings) { $invokeArgs.Settings = $Settings }",
            "  if (-not [string]::IsNullOrWhiteSpace($Path)) { $invokeArgs.Path = $Path }",
            "  if ($ExitCode) { $invokeArgs.ExitCode = $true }",
            "  if ($RemainingArgs.Count -gt 0) {",
            "    & $cmd @invokeArgs @RemainingArgs",
            "  } else {",
            "    & $cmd @invokeArgs",
            "  }",
            "}",
            "function Build-Module {",
            "  [CmdletBinding(PositionalBinding = $false)]",
            "  param(",
            "    [Parameter(Position = 0)][string]$ModuleName,",
            "    [Parameter(Position = 1)][scriptblock]$Settings,",
            "    [string]$Path,",
            "    [switch]$ExitCode,",
            "    [Parameter(ValueFromRemainingArguments = $true)][object[]]$RemainingArgs",
            "  )",
            "  if (-not $Settings -and $RemainingArgs.Count -gt 0 -and $RemainingArgs[0] -is [scriptblock]) {",
            "    $Settings = [scriptblock]$RemainingArgs[0]",
            "    if ($RemainingArgs.Count -gt 1) {",
            "      $RemainingArgs = $RemainingArgs[1..($RemainingArgs.Count - 1)]",
            "    } else {",
            "      $RemainingArgs = @()",
            "    }",
            "  }",
            "  $forwardArgs = @{ ModuleName = $ModuleName }",
            "  if ($null -ne $Settings) { $forwardArgs.Settings = $Settings }",
            "  if (-not [string]::IsNullOrWhiteSpace($Path)) { $forwardArgs.Path = $Path }",
            "  if ($ExitCode) { $forwardArgs.ExitCode = $true }",
            "  if ($RemainingArgs.Count -gt 0) {",
            "    Invoke-ModuleBuild @forwardArgs @RemainingArgs",
            "  } else {",
            "    Invoke-ModuleBuild @forwardArgs",
            "  }",
            "}",
            "Set-Alias -Name Invoke-ModuleBuilder -Value Invoke-ModuleBuild -Scope Local",
            $". {QuoteLiteral(scriptPath)}"
        });
    }

    private static string BuildBuildScript(string repositoryRoot, string scriptPath, string modulePath)
    {
        var moduleRoot = Directory.GetParent(Path.GetDirectoryName(scriptPath)!)?.FullName ?? repositoryRoot;
        return string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            $"Set-Location -LiteralPath {QuoteLiteral(moduleRoot)}",
            BuildModuleImportClause(modulePath),
            "function New-ConfigurationBuild {",
            "  param([Parameter(ValueFromRemainingArguments = $true)][object[]]$RemainingArgs)",
            "  $cmd = Get-Command -Name New-ConfigurationBuild -Module PSPublishModule",
            "  if ($RemainingArgs.Count -eq 1 -and $RemainingArgs[0] -is [System.Collections.IDictionary]) {",
            "    $params = @{}",
            "    foreach ($key in $RemainingArgs[0].Keys) { $params[$key] = $RemainingArgs[0][$key] }",
            "    $params['SignModule'] = $false",
            "    $params['CertificateThumbprint'] = $null",
            "    & $cmd @params",
            "    return",
            "  }",
            "  & $cmd @RemainingArgs -SignModule:$false",
            "}",
            $". {QuoteLiteral(scriptPath)}"
        });
    }

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
