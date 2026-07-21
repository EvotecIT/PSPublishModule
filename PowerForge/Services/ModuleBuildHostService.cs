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
        FrameworkCompatibility.NotNull(request, nameof(request));
        ValidateRequiredPath(request.RepositoryRoot, nameof(request.RepositoryRoot));
        ValidateRequiredPath(request.ScriptPath, nameof(request.ScriptPath));
        ValidateRequiredPath(request.ModulePath, nameof(request.ModulePath));
        ValidateRequiredPath(request.OutputPath, nameof(request.OutputPath));

        var script = BuildExportScript(request.RepositoryRoot, request.ScriptPath, request.OutputPath, request.ModulePath);
        return RunCommandAsync(
            request.RepositoryRoot,
            script,
            TimeSpan.FromMinutes(15),
            preferPwsh: !FrameworkCompatibility.IsWindows(),
            requiredRuntimeMajor: 0,
            cancellationToken);
    }

    /// <summary>
    /// Executes a module build script through shared orchestration.
    /// </summary>
    public Task<ModuleBuildHostExecutionResult> ExecuteBuildAsync(ModuleBuildHostBuildRequest request, CancellationToken cancellationToken = default)
    {
        FrameworkCompatibility.NotNull(request, nameof(request));
        ValidateRequiredPath(request.RepositoryRoot, nameof(request.RepositoryRoot));
        ValidateRequiredPath(request.ScriptPath, nameof(request.ScriptPath));
        ValidateRequiredPath(request.ModulePath, nameof(request.ModulePath));

        var script = BuildBuildScript(request.RepositoryRoot, request.ScriptPath, request.ModulePath, request);
        var timeout = request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(2) : request.Timeout;
        var hostRequirements = ResolveHostRequirements(request.Framework);
        return RunCommandAsync(
            request.RepositoryRoot,
            script,
            timeout,
            preferPwsh: hostRequirements.PreferPwsh,
            requiredRuntimeMajor: hostRequirements.RequiredRuntimeMajor,
            cancellationToken);
    }

    private async Task<ModuleBuildHostExecutionResult> RunCommandAsync(
        string workingDirectory,
        string script,
        TimeSpan timeout,
        bool preferPwsh,
        int requiredRuntimeMajor,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var executableOverride = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_POWERSHELL_EXE");
        var runRequest = requiredRuntimeMajor > 0
            ? PowerShellRunRequest.ForCompatibleCommand(
                commandText: script,
                timeout: timeout,
                requiredRuntimeMajor: requiredRuntimeMajor,
                workingDirectory: workingDirectory,
                executableOverride: executableOverride)
            : PowerShellRunRequest.ForCommand(
                commandText: script,
                timeout: timeout,
                preferPwsh: preferPwsh,
                workingDirectory: workingDirectory,
                executableOverride: executableOverride);
        var result = await Task.Run(() => _powerShellRunner.Run(runRequest), cancellationToken).ConfigureAwait(false);
        startedAt.Stop();

        return new ModuleBuildHostExecutionResult {
            ExitCode = result.ExitCode,
            Duration = startedAt.Elapsed,
            StandardOutput = result.StdOut,
            StandardError = result.StdErr,
            Executable = result.Executable
        };
    }

    private static PowerShellHostRequirements ResolveHostRequirements(string? framework)
    {
        var defaultPreference = !FrameworkCompatibility.IsWindows();

        if (string.IsNullOrWhiteSpace(framework))
            return new PowerShellHostRequirements(defaultPreference, 0);

        var targetFramework = framework!.Trim();
        if (string.Equals(targetFramework, "auto", StringComparison.OrdinalIgnoreCase))
            return new PowerShellHostRequirements(preferPwsh: true, requiredRuntimeMajor: 8);

        if (targetFramework.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
        {
            var coreVersionText = targetFramework.Substring("netcoreapp".Length);
            return Version.TryParse(coreVersionText, out var coreVersion)
                ? new PowerShellHostRequirements(preferPwsh: true, requiredRuntimeMajor: coreVersion.Major)
                : new PowerShellHostRequirements(preferPwsh: true, requiredRuntimeMajor: 0);
        }

        if (!targetFramework.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return new PowerShellHostRequirements(defaultPreference, 0);

        var versionText = targetFramework.Substring(3);
        var platformSeparator = versionText.IndexOf('-');
        if (platformSeparator >= 0)
            versionText = versionText.Substring(0, platformSeparator);

        // Compact TFMs such as net472 target .NET Framework. Modern .NET TFMs
        // use a dotted version (net8.0, net10.0) and require pwsh on Windows.
        // This also prevents self-release from preloading an installed net472
        // PSPublishModule assembly that cannot be unloaded before the local build.
        if (versionText.IndexOf('.') >= 0
            && Version.TryParse(versionText, out var version)
            && version.Major >= 5)
        {
            return new PowerShellHostRequirements(preferPwsh: true, requiredRuntimeMajor: version.Major);
        }

        return new PowerShellHostRequirements(defaultPreference, 0);
    }

    private sealed class PowerShellHostRequirements
    {
        public PowerShellHostRequirements(bool preferPwsh, int requiredRuntimeMajor)
        {
            PreferPwsh = preferPwsh;
            RequiredRuntimeMajor = requiredRuntimeMajor;
        }

        public bool PreferPwsh { get; }
        public int RequiredRuntimeMajor { get; }
    }

    private static string BuildExportScript(string repositoryRoot, string scriptPath, string outputPath, string modulePath)
    {
        var moduleRoot = ResolveModuleRoot(repositoryRoot, scriptPath);
        return string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            $"Set-Location -LiteralPath {QuoteLiteral(moduleRoot)}",
            BuildModuleImportClause(modulePath),
            $"$targetJson = {QuoteLiteral(outputPath)}",
            "Remove-Item -LiteralPath Alias:Build-Module -Force -ErrorAction SilentlyContinue",
            "Remove-Item -LiteralPath Alias:Invoke-ModuleBuilder -Force -ErrorAction SilentlyContinue",
            $"$buildScriptPath = (Get-Item -LiteralPath {QuoteLiteral(scriptPath)} -ErrorAction Stop).FullName",
            "$buildScriptCommand = Get-Command -Name $buildScriptPath -CommandType ExternalScript -ErrorAction Stop",
            "$buildScriptArguments = @{}",
            "if ($buildScriptCommand.Parameters.ContainsKey('RunMode')) {",
            "  $buildScriptArguments['RunMode'] = 'Build'",
            "} elseif ($buildScriptCommand.Parameters.ContainsKey('ConfigurationGateMode')) {",
            "  $buildScriptArguments['ConfigurationGateMode'] = 'Build'",
            "}",
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
            "function Import-Module {",
            "  $cmd = Get-Command -Name Import-Module -CommandType Cmdlet -Module Microsoft.PowerShell.Core",
            "  & $cmd @args",
            "  Remove-Item -LiteralPath Alias:Build-Module -Force -ErrorAction SilentlyContinue",
            "  Remove-Item -LiteralPath Alias:Invoke-ModuleBuilder -Force -ErrorAction SilentlyContinue",
            "  Set-Alias -Name Invoke-ModuleBuilder -Value Invoke-ModuleBuild -Scope Local",
            "}",
            "Set-Alias -Name Invoke-ModuleBuilder -Value Invoke-ModuleBuild -Scope Local",
            ". $buildScriptPath @buildScriptArguments"
        });
    }

    private static string BuildBuildScript(string repositoryRoot, string scriptPath, string modulePath, ModuleBuildHostBuildRequest request)
    {
        var moduleRoot = ResolveModuleRoot(repositoryRoot, scriptPath);
        var invocation = BuildScriptInvocation(scriptPath, request);
        return string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            $"Set-Location -LiteralPath {QuoteLiteral(moduleRoot)}",
            BuildModuleImportClause(modulePath),
            invocation
        });
    }

    private static string BuildScriptInvocation(string scriptPath, ModuleBuildHostBuildRequest request)
    {
        var arguments = new List<string>
        {
            $"$buildScriptPath = (Get-Item -LiteralPath {QuoteLiteral(scriptPath)} -ErrorAction Stop).FullName",
            "$buildScriptCommand = Get-Command -Name $buildScriptPath -CommandType ExternalScript -ErrorAction Stop",
            "$buildScriptArguments = @{}"
        };

        if (!string.IsNullOrWhiteSpace(request.Configuration))
        {
            arguments.Add($"if ($buildScriptCommand.Parameters.ContainsKey('Configuration')) {{ $buildScriptArguments['Configuration'] = {QuoteLiteral(request.Configuration!)} }}");
        }

        if (!string.IsNullOrWhiteSpace(request.Framework))
        {
            arguments.Add($"if ($buildScriptCommand.Parameters.ContainsKey('Framework')) {{ $buildScriptArguments['Framework'] = {QuoteLiteral(request.Framework!)} }}");
        }

        if (request.RunMode.HasValue)
        {
            var runMode = request.RunMode.Value.ToString();
            arguments.Add($"if ($buildScriptCommand.Parameters.ContainsKey('RunMode')) {{ $buildScriptArguments['RunMode'] = {QuoteLiteral(runMode)} }} elseif ($buildScriptCommand.Parameters.ContainsKey('ConfigurationGateMode')) {{ $buildScriptArguments['ConfigurationGateMode'] = {QuoteLiteral(runMode)} }}");
        }

        if (request.PowerForgeReleaseStage)
        {
            arguments.Add("if ($buildScriptCommand.Parameters.ContainsKey('PowerForgeReleaseStage')) { $buildScriptArguments['PowerForgeReleaseStage'] = $true }");
        }

        if (request.UnifiedGitHubRelease)
        {
            arguments.Add("if ($buildScriptCommand.Parameters.ContainsKey('PowerForgeUnifiedGitHubRelease')) { $buildScriptArguments['PowerForgeUnifiedGitHubRelease'] = $true }");
        }

        if (request.NoDotnetBuild)
        {
            arguments.Add("if ($buildScriptCommand.Parameters.ContainsKey('NoDotnetBuild')) { $buildScriptArguments['NoDotnetBuild'] = $true }");
        }

        if (!string.IsNullOrWhiteSpace(request.ModuleVersion))
        {
            arguments.Add($"$buildScriptArguments['ModuleVersion'] = {QuoteLiteral(request.ModuleVersion!)}");
        }

        if (!string.IsNullOrWhiteSpace(request.PreReleaseTag))
        {
            arguments.Add($"$buildScriptArguments['PreReleaseTag'] = {QuoteLiteral(request.PreReleaseTag!)}");
        }

        if (request.NoSign)
            arguments.Add("$buildScriptArguments['NoSign'] = $true");

        if (request.SignModule)
        {
            arguments.Add("if ($buildScriptCommand.Parameters.ContainsKey('SignModule')) { $buildScriptArguments['SignModule'] = $true }");
        }

        arguments.Add($"if ($buildScriptCommand.Parameters.ContainsKey('IncludeProjectPackages')) {{ $buildScriptArguments['IncludeProjectPackages'] = ${request.IncludeProjectPackages.ToString().ToLowerInvariant()} }}");

        AddOptionalStringArgument(arguments, "CertificateThumbprint", request.CertificateThumbprint);
        AddOptionalSwitchArgument(arguments, "SignIncludeBinaries", request.SignIncludeBinaries);
        AddOptionalSwitchArgument(arguments, "SignIncludeInternals", request.SignIncludeInternals);
        AddOptionalSwitchArgument(arguments, "SignIncludeExe", request.SignIncludeExe);
        AddOptionalStringArgument(arguments, "DiagnosticsBaselinePath", request.DiagnosticsBaselinePath);
        AddOptionalSwitchArgument(arguments, "GenerateDiagnosticsBaseline", request.GenerateDiagnosticsBaseline);
        AddOptionalSwitchArgument(arguments, "UpdateDiagnosticsBaseline", request.UpdateDiagnosticsBaseline);
        AddOptionalSwitchArgument(arguments, "FailOnNewDiagnostics", request.FailOnNewDiagnostics);
        AddOptionalStringArgument(arguments, "FailOnDiagnosticsSeverity", request.FailOnDiagnosticsSeverity);

        arguments.Add(". $buildScriptPath @buildScriptArguments");

        return string.Join(Environment.NewLine, arguments);
    }

    private static void AddOptionalStringArgument(List<string> arguments, string parameterName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        arguments.Add(
            $"if ($buildScriptCommand.Parameters.ContainsKey('{parameterName}')) {{ $buildScriptArguments['{parameterName}'] = {QuoteLiteral(value!)} }}");
    }

    private static void AddOptionalSwitchArgument(List<string> arguments, string parameterName, bool? value)
    {
        if (!value.HasValue)
            return;

        arguments.Add(
            $"if ($buildScriptCommand.Parameters.ContainsKey('{parameterName}')) {{ $buildScriptArguments['{parameterName}'] = ${value.Value.ToString().ToLowerInvariant()} }}");
    }

    private static string BuildModuleImportClause(string modulePath)
        => File.Exists(modulePath)
            ? $"try {{ Import-Module {QuoteLiteral(modulePath)} -Force -ErrorAction Stop }} catch {{ Import-Module PSPublishModule -Force -ErrorAction Stop }}"
            : "Import-Module PSPublishModule -Force -ErrorAction Stop";

    private static string ResolveModuleRoot(string repositoryRoot, string scriptPath)
    {
        var scriptDirectory = Path.GetDirectoryName(scriptPath);
        if (!string.IsNullOrWhiteSpace(scriptDirectory))
            return Directory.GetParent(scriptDirectory)?.FullName ?? repositoryRoot;

        var scriptSeparator = Math.Max(scriptPath.LastIndexOf('/'), scriptPath.LastIndexOf('\\'));
        if (scriptSeparator <= 0)
            return repositoryRoot;

        var foreignScriptDirectory = scriptPath.Substring(0, scriptSeparator);
        var parentSeparator = Math.Max(foreignScriptDirectory.LastIndexOf('/'), foreignScriptDirectory.LastIndexOf('\\'));
        return parentSeparator > 0 ? foreignScriptDirectory.Substring(0, parentSeparator) : repositoryRoot;
    }

    private static string QuoteLiteral(string value)
        => $"'{(value ?? string.Empty).Replace("'", "''")}'";

    private static void ValidateRequiredPath(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{argumentName} is required.", argumentName);
    }
}
