using System.Text;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryPlanPreviewService
{
    private readonly PowerShellCommandRunner _commandRunner = new();

    public async Task<IReadOnlyList<RepositoryPortfolioItem>> PopulatePlanPreviewAsync(
        IEnumerable<RepositoryPortfolioItem> items,
        PlanPreviewOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PlanPreviewOptions();
        var materialized = items.ToList();
        var targetCount = Math.Max(0, options.MaxRepositories);
        var planTargets = materialized
            .OrderBy(item => GetPreviewPriority(item.Repository.RepositoryKind))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(targetCount)
            .Select(item => item.RootPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updated = new List<RepositoryPortfolioItem>(materialized.Count);
        for (var index = 0; index < materialized.Count; index++)
        {
            var item = materialized[index];
            if (!planTargets.Contains(item.RootPath))
            {
                updated.Add(item with {
                    PlanResults = []
                });
                continue;
            }

            var results = new List<RepositoryPlanResult>();
            if (!string.IsNullOrWhiteSpace(item.Repository.ModuleBuildScriptPath))
            {
                results.Add(await RunModulePlanAsync(item, cancellationToken));
            }

            if (!string.IsNullOrWhiteSpace(item.Repository.ProjectBuildScriptPath))
            {
                results.Add(await RunProjectPlanAsync(item, cancellationToken));
            }

            updated.Add(item with {
                PlanResults = results
            });
        }

        return updated;
    }

    private static int GetPreviewPriority(Domain.Catalog.ReleaseRepositoryKind repositoryKind)
        => repositoryKind switch
        {
            Domain.Catalog.ReleaseRepositoryKind.Mixed => 0,
            Domain.Catalog.ReleaseRepositoryKind.Library => 1,
            Domain.Catalog.ReleaseRepositoryKind.Module => 2,
            _ => 3
        };

    private async Task<RepositoryPlanResult> RunModulePlanAsync(RepositoryPortfolioItem item, CancellationToken cancellationToken)
    {
        var modulePath = ResolvePSPublishModulePath();
        var outputPath = BuildPlanOutputPath(item.Name, RepositoryPlanAdapterKind.ModuleJsonExport, "powerforge.json");
        var script = BuildModuleScript(item.Repository.RootPath, item.Repository.ModuleBuildScriptPath!, outputPath, modulePath);
        var execution = await _commandRunner.RunCommandAsync(item.Repository.RootPath, script, cancellationToken);

        return BuildResult(
            RepositoryPlanAdapterKind.ModuleJsonExport,
            outputPath,
            execution,
            successSummary: "Module JSON config exported.",
            failureSummary: "Module JSON export failed.");
    }

    private async Task<RepositoryPlanResult> RunProjectPlanAsync(RepositoryPortfolioItem item, CancellationToken cancellationToken)
    {
        var modulePath = ResolvePSPublishModulePath();
        var outputPath = BuildPlanOutputPath(item.Name, RepositoryPlanAdapterKind.ProjectPlan, "project.plan.json");
        var configPath = ResolveProjectConfigPath(item.Repository.ProjectBuildScriptPath!, item.Repository.RootPath);
        var script = BuildProjectScript(item.Repository.RootPath, outputPath, modulePath, configPath);
        var execution = await _commandRunner.RunCommandAsync(item.Repository.RootPath, script, cancellationToken);

        return BuildResult(
            RepositoryPlanAdapterKind.ProjectPlan,
            outputPath,
            execution,
            successSummary: "Project build plan generated.",
            failureSummary: "Project build plan failed.");
    }

    private static RepositoryPlanResult BuildResult(
        RepositoryPlanAdapterKind adapterKind,
        string outputPath,
        PowerShellExecutionResult execution,
        string successSummary,
        string failureSummary)
    {
        var success = execution.ExitCode == 0 && File.Exists(outputPath);
        return new RepositoryPlanResult(
            AdapterKind: adapterKind,
            Status: success ? RepositoryPlanStatus.Succeeded : RepositoryPlanStatus.Failed,
            Summary: success ? successSummary : failureSummary,
            PlanPath: success ? outputPath : null,
            ExitCode: execution.ExitCode,
            DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
            OutputTail: TrimTail(execution.StandardOutput),
            ErrorTail: TrimTail(execution.StandardError));
    }

    private static string BuildPlanOutputPath(string repositoryName, RepositoryPlanAdapterKind adapterKind, string fileName)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerForgeStudio",
            "plans",
            SanitizePathSegment(repositoryName),
            adapterKind.ToString());

        Directory.CreateDirectory(root);
        return Path.Combine(root, fileName);
    }

    internal static string? ResolveProjectConfigPath(string projectBuildScriptPath, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectBuildScriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var buildDirectory = Path.GetDirectoryName(projectBuildScriptPath);
        if (!string.IsNullOrWhiteSpace(buildDirectory))
        {
            var siblingConfig = Path.Combine(buildDirectory, "project.build.json");
            if (File.Exists(siblingConfig))
            {
                return siblingConfig;
            }
        }

        var rootConfig = Path.Combine(repositoryRoot, "Build", "project.build.json");
        return File.Exists(rootConfig) ? rootConfig : null;
    }

    private static string BuildProjectScript(string repositoryRoot, string outputPath, string modulePath, string? configPath)
    {
        var lines = new List<string> {
            "$ErrorActionPreference = 'Stop'",
            BuildModuleImportClause(modulePath),
            $"Set-Location -LiteralPath {PowerShellScriptEscaping.QuoteLiteral(repositoryRoot)}"
        };

        var command = new StringBuilder("Invoke-ProjectBuild -Plan:$true -PlanPath ");
        command.Append(PowerShellScriptEscaping.QuoteLiteral(outputPath));
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            command.Append(" -ConfigPath ").Append(PowerShellScriptEscaping.QuoteLiteral(configPath));
        }

        lines.Add(command.ToString());
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildModuleScript(string repositoryRoot, string scriptPath, string outputPath, string modulePath)
    {
        var moduleRoot = Directory.GetParent(Path.GetDirectoryName(scriptPath)!)?.FullName ?? repositoryRoot;
        return string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            $"Set-Location -LiteralPath {PowerShellScriptEscaping.QuoteLiteral(moduleRoot)}",
            BuildModuleImportClause(modulePath),
            $"$targetJson = {PowerShellScriptEscaping.QuoteLiteral(outputPath)}",
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
            $". {PowerShellScriptEscaping.QuoteLiteral(scriptPath)}"
        });
    }

    private static string BuildModuleImportClause(string modulePath)
    {
        if (File.Exists(modulePath))
        {
            return $"try {{ Import-Module {PowerShellScriptEscaping.QuoteLiteral(modulePath)} -Force -ErrorAction Stop }} catch {{ Import-Module PSPublishModule -Force -ErrorAction Stop }}";
        }

        return "Import-Module PSPublishModule -Force -ErrorAction Stop";
    }

    private static string ResolvePSPublishModulePath()
    {
        return PSPublishModuleLocator.ResolveModulePath();
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static string? TrimTail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const int maxLength = 600;
        return text.Length <= maxLength ? text.Trim() : text[^maxLength..].Trim();
    }
}
