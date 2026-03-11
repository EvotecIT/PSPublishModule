using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService : IReleasePublishExecutionService
{
    private readonly RepositoryCatalogScanner _catalogScanner = new();
    private readonly PowerShellCommandRunner _commandRunner = new();
    private const string GitHubPublishTokenEnvironmentVariable = "POWERFORGESTUDIO_GITHUB_RELEASE_TOKEN";
    private const string NuGetPushResponseDirectoryName = "nuget-push";
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<ReleasePublishTarget> BuildPendingTargets(IEnumerable<ReleaseQueueItem> queueItems)
    {
        ArgumentNullException.ThrowIfNull(queueItems);

        var targets = new List<ReleasePublishTarget>();
        foreach (var item in queueItems.Where(candidate => candidate.Stage == ReleaseQueueStage.Publish && candidate.Status == ReleaseQueueItemStatus.ReadyToRun))
        {
            var signingResult = TryDeserializeSigningResult(item);
            if (signingResult is null)
            {
                continue;
            }

            var receipts = signingResult.Receipts ?? [];
            var grouped = receipts.GroupBy(receipt => receipt.AdapterKind, StringComparer.OrdinalIgnoreCase);
            foreach (var group in grouped)
            {
                var adapterKind = group.Key;
                var paths = group.Select(receipt => receipt.ArtifactPath).ToArray();
                if (paths.Any(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
                {
                    targets.Add(new ReleasePublishTarget(
                        RootPath: item.RootPath,
                        RepositoryName: item.RepositoryName,
                        AdapterKind: adapterKind,
                        TargetName: $"{group.Count(path => path.ArtifactPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))} NuGet package(s)",
                        TargetKind: "NuGet",
                        SourcePath: paths.FirstOrDefault(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)),
                        Destination: "Configured NuGet feed"));
                }

                if (paths.Any(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    targets.Add(new ReleasePublishTarget(
                        RootPath: item.RootPath,
                        RepositoryName: item.RepositoryName,
                        AdapterKind: adapterKind,
                        TargetName: $"{paths.Count(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))} GitHub asset(s)",
                        TargetKind: "GitHub",
                        SourcePath: paths.FirstOrDefault(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)),
                        Destination: "Configured GitHub release"));
                }

                if (string.Equals(adapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    group.Any(receipt => string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)))
                {
                    targets.Add(new ReleasePublishTarget(
                        RootPath: item.RootPath,
                        RepositoryName: item.RepositoryName,
                        AdapterKind: adapterKind,
                        TargetName: "Module package",
                        TargetKind: "PowerShellRepository",
                        SourcePath: group.First(receipt => string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)).ArtifactPath,
                        Destination: "Configured PowerShell repository"));
                }
            }
        }

        return targets
            .DistinctBy(target => $"{target.RootPath}|{target.AdapterKind}|{target.TargetKind}|{target.SourcePath}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ReleasePublishExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueItem);

        var signingResult = TryDeserializeSigningResult(queueItem);
        if (signingResult is null)
        {
            return new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Publish checkpoint could not be read from queue state.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Publish", "Queue checkpoint", null, "Queue state is missing the signing checkpoint.")
                ]);
        }

        var pendingTargets = BuildPendingTargets([queueItem]);
        if (pendingTargets.Count == 0)
        {
            return new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: true,
                Summary: "No publish targets were detected for this queue item.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    SkippedReceipt(
                        queueItem.RootPath,
                        queueItem.RepositoryName,
                        "Publish",
                        "Publish",
                        null,
                        "No external publish targets were detected for this queue item, so verification can be skipped.")
                ]);
        }

        if (!IsPublishEnabled())
        {
            return new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Publish is disabled. Set RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true to unlock external publishing.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: pendingTargets.Select(target => FailedReceipt(
                    queueItem.RootPath,
                    queueItem.RepositoryName,
                    target.AdapterKind,
                    target.TargetKind,
                    target.Destination,
                    "Publish is disabled. Set RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true to unlock external publishing.")).ToList());
        }

        var repository = _catalogScanner.InspectRepository(queueItem.RootPath);
        var receipts = new List<ReleasePublishReceipt>();

        if (!string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
        {
            receipts.AddRange(await ExecuteProjectPublishAsync(repository, signingResult, cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
        {
            receipts.AddRange(await ExecuteModulePublishAsync(repository, signingResult, cancellationToken));
        }

        if (receipts.Count == 0)
        {
            receipts.Add(FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Publish", "Publish", null, "No publish-capable adapter execution was produced."));
        }

        var published = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Published);
        var skipped = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleasePublishReceiptStatus.Failed);
        var summary = failed > 0
            ? $"Publish completed with {published} published, {skipped} skipped, and {failed} failed target(s)."
            : $"Publish completed with {published} published and {skipped} skipped target(s).";

        return new ReleasePublishExecutionResult(
            RootPath: queueItem.RootPath,
            Succeeded: failed == 0,
            Summary: summary,
            SourceCheckpointStateJson: queueItem.CheckpointStateJson,
            Receipts: receipts);
    }
}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<(bool Succeeded, string? ErrorMessage)> PublishNugetPackageAsync(string packagePath, string apiKey, string source, CancellationToken cancellationToken)
    {
        var responseFilePath = await CreateNuGetPushResponseFileAsync(packagePath, apiKey, source, cancellationToken).ConfigureAwait(false);
        using var process = new Process();
        try
        {
            process.StartInfo = new ProcessStartInfo {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
            process.StartInfo.ArgumentList.Add($"@{responseFilePath}");

            process.Start();
            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdOut = await stdOutTask.ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                return (true, null);
            }

            return (false, FirstLine(stdErr) ?? FirstLine(stdOut) ?? $"dotnet nuget push failed with exit code {process.ExitCode}.");
        }
        finally
        {
            TryDeleteFile(responseFilePath);
        }
    }

    private static async Task<string> CreateNuGetPushResponseFileAsync(string packagePath, string apiKey, string source, CancellationToken cancellationToken)
    {
        var runtimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerForgeStudio",
            "runtime",
            NuGetPushResponseDirectoryName);
        Directory.CreateDirectory(runtimeDirectory);

        var responseFilePath = Path.Combine(runtimeDirectory, $"nuget-push-{Guid.NewGuid():N}.rsp");
        var content = string.Join(Environment.NewLine, new[] {
            "nuget",
            "push",
            QuoteResponseFileValue(packagePath),
            "--api-key",
            QuoteResponseFileValue(apiKey),
            "--source",
            QuoteResponseFileValue(source),
            "--skip-duplicate"
        });

        await File.WriteAllTextAsync(responseFilePath, content, cancellationToken).ConfigureAwait(false);
        return responseFilePath;
    }

    private static string QuoteResponseFileValue(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for temporary response files.
        }
    }

    private async Task<GitHubReleaseExecutionResult> PublishGitHubReleaseAsync(
        string repositoryRoot,
        string owner,
        string repo,
        string token,
        string tag,
        string releaseName,
        IReadOnlyList<string> assetPaths,
        bool generateReleaseNotes,
        bool isPreRelease,
        CancellationToken cancellationToken)
    {
        var script = string.Join("; ", new[] {
            "$ErrorActionPreference = 'Stop'",
            BuildModuleImportClause(ResolvePSPublishModulePath()),
            $"$gitHubToken = $env:{GitHubPublishTokenEnvironmentVariable}",
            "if ([string]::IsNullOrWhiteSpace($gitHubToken)) { throw 'GitHub access token was not provided to the publish process.' }",
            $"$result = Send-GitHubRelease -GitHubUsername {PowerShellScriptEscaping.QuoteLiteral(owner)} -GitHubRepositoryName {PowerShellScriptEscaping.QuoteLiteral(repo)} -GitHubAccessToken $gitHubToken -TagName {PowerShellScriptEscaping.QuoteLiteral(tag)} -ReleaseName {PowerShellScriptEscaping.QuoteLiteral(releaseName)} -AssetFilePaths @({string.Join(", ", assetPaths.Select(PowerShellScriptEscaping.QuoteLiteral))}) -GenerateReleaseNotes:${generateReleaseNotes.ToString().ToLowerInvariant()} -IsPreRelease:${isPreRelease.ToString().ToLowerInvariant()} -ReuseExistingReleaseOnConflict:$true",
            "$result | ConvertTo-Json -Compress"
        });

        var execution = await _commandRunner.RunCommandAsync(
            repositoryRoot,
            script,
            new Dictionary<string, string?> {
                [GitHubPublishTokenEnvironmentVariable] = token
            },
            cancellationToken);
        if (execution.ExitCode != 0)
        {
            return new GitHubReleaseExecutionResult(false, null, FirstLine(execution.StandardError) ?? FirstLine(execution.StandardOutput) ?? "GitHub publish failed.");
        }

        var parsed = JsonSerializer.Deserialize<GitHubReleaseJson>(execution.StandardOutput.Trim(), JsonOptions);
        if (parsed is null)
        {
            return new GitHubReleaseExecutionResult(false, null, "GitHub publish returned unexpected output.");
        }

        return new GitHubReleaseExecutionResult(parsed.Succeeded, parsed.ReleaseUrl, parsed.Succeeded ? null : parsed.ErrorMessage);
    }

    private static bool IsPublishEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_ENABLE_PUBLISH"), "true", StringComparison.OrdinalIgnoreCase);

    private static ReleaseSigningExecutionResult? TryDeserializeSigningResult(ReleaseQueueItem queueItem)
    {
        if (string.IsNullOrWhiteSpace(queueItem.CheckpointStateJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReleaseSigningExecutionResult>(queueItem.CheckpointStateJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static ReleasePublishReceipt FailedReceipt(string rootPath, string repositoryName, string adapterKind, string targetKind, string? destination, string summary)
    {
        return new ReleasePublishReceipt(
            RootPath: rootPath,
            RepositoryName: repositoryName,
            AdapterKind: adapterKind,
            TargetName: targetKind,
            TargetKind: targetKind,
            Destination: destination,
            SourcePath: null,
            Status: ReleasePublishReceiptStatus.Failed,
            Summary: summary,
            PublishedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ReleasePublishReceipt SkippedReceipt(string rootPath, string repositoryName, string adapterKind, string targetKind, string? destination, string summary)
    {
        return new ReleasePublishReceipt(
            RootPath: rootPath,
            RepositoryName: repositoryName,
            AdapterKind: adapterKind,
            TargetName: targetKind,
            TargetKind: targetKind,
            Destination: destination,
            SourcePath: null,
            Status: ReleasePublishReceiptStatus.Skipped,
            Summary: summary,
            PublishedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string ResolveModuleRepositoryName(ModulePublishConfig publishConfig)
        => publishConfig.Repository?.Name
           ?? publishConfig.RepositoryName
           ?? "PSGallery";

    private static string? FindZipAsset(ReleaseSigningExecutionResult signingResult, string? projectName = null)
    {
        var zipAssets = signingResult.Receipts
            .Select(receipt => receipt.ArtifactPath)
            .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (zipAssets.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            return zipAssets[0];
        }

        return zipAssets.FirstOrDefault(path => Path.GetFileName(path).Contains(projectName, StringComparison.OrdinalIgnoreCase))
               ?? zipAssets[0];
    }

    private static string? ResolveGitHubBaseVersion(ProjectPublishConfig config, ProjectReleasePlan release)
    {
        if (!string.IsNullOrWhiteSpace(config.GitHubPrimaryProject))
        {
            var match = release.Projects.FirstOrDefault(project => string.Equals(project.ProjectName, config.GitHubPrimaryProject, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.NewVersion ?? match.OldVersion;
            }
        }

        var versions = release.Projects
            .Where(project => project.IsPackable && !string.IsNullOrWhiteSpace(project.NewVersion))
            .Select(project => project.NewVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return versions.Length == 1 ? versions[0] : null;
    }

    private static ProjectPublishConfig LoadProjectPublishConfig(string configPath)
        => JsonSerializer.Deserialize<ProjectPublishConfig>(File.ReadAllText(configPath), JsonOptions) ?? new ProjectPublishConfig();

    private static string? ResolveSecret(string? inline, string? filePath, string? envName, string basePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var fullPath = Path.IsPathRooted(filePath)
                ? filePath
                : Path.GetFullPath(Path.Combine(basePath, filePath));
            if (File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath).Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(envName))
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(inline) ? null : inline.Trim();
    }

    private static string BuildProjectPlanScript(string repositoryRoot, string planPath, string? configPath, string modulePath)
    {
        var command = new StringBuilder();
        command.Append("Invoke-ProjectBuild -Plan:$true -PlanPath ").Append(PowerShellScriptEscaping.QuoteLiteral(planPath));
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            command.Append(" -ConfigPath ").Append(PowerShellScriptEscaping.QuoteLiteral(configPath));
        }

        return string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            BuildModuleImportClause(modulePath),
            $"Set-Location -LiteralPath {PowerShellScriptEscaping.QuoteLiteral(repositoryRoot)}",
            command.ToString()
        });
    }

    private static string BuildModuleExportScript(string repositoryRoot, string scriptPath, string outputPath, string modulePath)
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
            "    if ($RemainingArgs.Count -gt 1) { $RemainingArgs = $RemainingArgs[1..($RemainingArgs.Count - 1)] } else { $RemainingArgs = @() }",
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
            "    if ($RemainingArgs.Count -gt 1) { $RemainingArgs = $RemainingArgs[1..($RemainingArgs.Count - 1)] } else { $RemainingArgs = @() }",
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

    private static string BuildModuleRepositoryPublishScript(string packagePath, string repositoryName, string apiKey, string tool)
    {
        var publishPsResource = string.Join("; ", new[] {
            "$cmd = Get-Command -Name Publish-PSResource -ErrorAction SilentlyContinue",
            "if ($null -ne $cmd) { Publish-PSResource -Path " + PowerShellScriptEscaping.QuoteLiteral(packagePath) + " -Repository " + PowerShellScriptEscaping.QuoteLiteral(repositoryName) + " -ApiKey " + PowerShellScriptEscaping.QuoteLiteral(apiKey) + " -SkipDependencyCheck -ErrorAction Stop | Out-Null; exit 0 }"
        });

        var publishPowerShellGet = $"Publish-Module -Path {PowerShellScriptEscaping.QuoteLiteral(packagePath)} -Repository {PowerShellScriptEscaping.QuoteLiteral(repositoryName)} -NuGetApiKey {PowerShellScriptEscaping.QuoteLiteral(apiKey)} -ErrorAction Stop | Out-Null";
        return string.Join("; ", new[] {
            "$ErrorActionPreference = 'Stop'",
            tool.Equals("PowerShellGet", StringComparison.OrdinalIgnoreCase)
                ? publishPowerShellGet
                : $"{publishPsResource}; {publishPowerShellGet}"
        });
    }

    private static string ApplyProjectTemplate(string template, string project, string version, string primaryProject, string primaryVersion, string repo, string date, string utcDate, string dateTime, string utcDateTime, string timestamp, string utcTimestamp)
        => template
            .Replace("{Project}", project ?? string.Empty)
            .Replace("{Version}", version ?? string.Empty)
            .Replace("{PrimaryProject}", primaryProject ?? string.Empty)
            .Replace("{PrimaryVersion}", primaryVersion ?? string.Empty)
            .Replace("{Repo}", repo ?? string.Empty)
            .Replace("{Repository}", repo ?? string.Empty)
            .Replace("{Date}", date ?? string.Empty)
            .Replace("{UtcDate}", utcDate ?? string.Empty)
            .Replace("{DateTime}", dateTime ?? string.Empty)
            .Replace("{UtcDateTime}", utcDateTime ?? string.Empty)
            .Replace("{Timestamp}", timestamp ?? string.Empty)
            .Replace("{UtcTimestamp}", utcTimestamp ?? string.Empty);

    private static string ReplaceModuleTokens(string template, string moduleName, string resolvedVersion, string? preRelease)
    {
        var versionWithPreRelease = string.IsNullOrWhiteSpace(preRelease) ? resolvedVersion : $"{resolvedVersion}-{preRelease}";
        return template
            .Replace("<ModuleName>", moduleName)
            .Replace("{ModuleName}", moduleName)
            .Replace("<ModuleVersion>", resolvedVersion)
            .Replace("{ModuleVersion}", resolvedVersion)
            .Replace("<ModuleVersionWithPreRelease>", versionWithPreRelease)
            .Replace("{ModuleVersionWithPreRelease}", versionWithPreRelease)
            .Replace("<TagModuleVersionWithPreRelease>", $"v{versionWithPreRelease}")
            .Replace("{TagModuleVersionWithPreRelease}", $"v{versionWithPreRelease}");
    }

    private static string BuildModuleImportClause(string modulePath)
        => File.Exists(modulePath)
            ? $"try {{ Import-Module {PowerShellScriptEscaping.QuoteLiteral(modulePath)} -Force -ErrorAction Stop }} catch {{ Import-Module PSPublishModule -Force -ErrorAction Stop }}"
            : "Import-Module PSPublishModule -Force -ErrorAction Stop";

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

    private static string? FirstLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

    private sealed class ProjectPublishConfig
    {
        public bool? PublishNuget { get; set; }
        public bool? PublishGitHub { get; set; }
        public string? PublishSource { get; set; }
        public string? PublishApiKey { get; set; }
        public string? PublishApiKeyFilePath { get; set; }
        public string? PublishApiKeyEnvName { get; set; }
        public string? GitHubAccessToken { get; set; }
        public string? GitHubAccessTokenFilePath { get; set; }
        public string? GitHubAccessTokenEnvName { get; set; }
        public string? GitHubUsername { get; set; }
        public string? GitHubRepositoryName { get; set; }
        public bool GitHubIsPreRelease { get; set; }
        public bool GitHubIncludeProjectNameInTag { get; set; } = true;
        public bool GitHubGenerateReleaseNotes { get; set; }
        public string? GitHubReleaseName { get; set; }
        public string? GitHubTagName { get; set; }
        public string? GitHubTagTemplate { get; set; }
        public string? GitHubReleaseMode { get; set; }
        public string? GitHubPrimaryProject { get; set; }
    }

    private sealed class ProjectReleasePlan
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<ProjectReleaseProject> Projects { get; set; } = [];
    }

    private sealed class ProjectReleaseProject
    {
        public string ProjectName { get; set; } = string.Empty;
        public bool IsPackable { get; set; }
        public string? OldVersion { get; set; }
        public string? NewVersion { get; set; }
        public string? ReleaseZipPath { get; set; }
    }

    private sealed record ModulePublishConfig(
        string Destination,
        string Tool,
        string? ApiKey,
        bool Enabled,
        string? UserName,
        string? RepositoryName,
        string? OverwriteTagName,
        bool DoNotMarkAsPreRelease,
        bool GenerateReleaseNotes,
        ModulePublishRepositoryConfig? Repository);

    private sealed class ModulePublishRepositoryConfig
    {
        public string? Name { get; set; }
        public string? Uri { get; set; }
        public string? SourceUri { get; set; }
        public string? PublishUri { get; set; }
        public ModulePublishRepositoryCredential? Credential { get; set; }
    }

    private sealed class ModulePublishRepositoryCredential
    {
        public string? UserName { get; set; }
        public string? Secret { get; set; }
    }

    private sealed record ModulePackageDetails(string ModuleName, string Version, string? PreRelease, string PackagePath, IReadOnlyList<string> ZipAssets);
    private sealed record ModuleManifestInfo(string ModuleName, string Version, string? PreRelease);

    private sealed class ModuleManifestJson
    {
        public string? ModuleName { get; set; }
        public string? ModuleVersion { get; set; }
        public string? PreRelease { get; set; }
    }

    private sealed class GitHubReleaseJson
    {
        public bool Succeeded { get; set; }
        public string? ReleaseUrl { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed record GitHubReleaseExecutionResult(bool Succeeded, string? ReleaseUrl, string? ErrorMessage);
}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<ProjectReleasePlan?> GenerateProjectPlanAsync(PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository, CancellationToken cancellationToken)
    {
        var scriptPath = repository.ProjectBuildScriptPath!;
        var configPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "project.build.json");
        var runtimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerForgeStudio",
            "runtime",
            SanitizePathSegment(repository.Name),
            "project-publish");
        Directory.CreateDirectory(runtimeDirectory);

        var planPath = Path.Combine(runtimeDirectory, "project.publish.plan.json");
        var script = BuildProjectPlanScript(repository.RootPath, planPath, File.Exists(configPath) ? configPath : null, ResolvePSPublishModulePath());
        var execution = await _commandRunner.RunCommandAsync(repository.RootPath, script, cancellationToken);
        if (execution.ExitCode != 0 || !File.Exists(planPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ProjectReleasePlan>(await File.ReadAllTextAsync(planPath, cancellationToken), JsonOptions);
    }

    private async Task<IReadOnlyList<ModulePublishConfig>> ExportModulePublishConfigsAsync(string repositoryRoot, string scriptPath, CancellationToken cancellationToken)
    {
        var runtimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerForgeStudio",
            "runtime",
            SanitizePathSegment(Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))),
            "module-publish");
        Directory.CreateDirectory(runtimeDirectory);

        var exportPath = Path.Combine(runtimeDirectory, "powerforge.publish.json");
        var script = BuildModuleExportScript(repositoryRoot, scriptPath, exportPath, ResolvePSPublishModulePath());
        var execution = await _commandRunner.RunCommandAsync(repositoryRoot, script, cancellationToken);
        if (execution.ExitCode != 0 || !File.Exists(exportPath))
        {
            return [];
        }

        var root = JsonNode.Parse(await File.ReadAllTextAsync(exportPath, cancellationToken))?.AsObject();
        var segments = root?["Segments"]?.AsArray();
        if (segments is null)
        {
            return [];
        }

        var results = new List<ModulePublishConfig>();
        foreach (var segment in segments)
        {
            var type = segment?["Type"]?.GetValue<string>();
            if (!string.Equals(type, "GalleryNuget", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(type, "GitHubNuget", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (segment?["Configuration"] is not JsonObject configuration)
            {
                continue;
            }

            results.Add(new ModulePublishConfig(
                Destination: configuration["Destination"]?.GetValue<string>() ?? (string.Equals(type, "GitHubNuget", StringComparison.OrdinalIgnoreCase) ? "GitHub" : "PowerShellGallery"),
                Tool: configuration["Tool"]?.GetValue<string>() ?? "Auto",
                ApiKey: configuration["ApiKey"]?.GetValue<string>(),
                Enabled: configuration["Enabled"]?.GetValue<bool>() == true,
                UserName: configuration["UserName"]?.GetValue<string>(),
                RepositoryName: configuration["RepositoryName"]?.GetValue<string>(),
                OverwriteTagName: configuration["OverwriteTagName"]?.GetValue<string>(),
                DoNotMarkAsPreRelease: configuration["DoNotMarkAsPreRelease"]?.GetValue<bool>() == true,
                GenerateReleaseNotes: configuration["GenerateReleaseNotes"]?.GetValue<bool>() == true,
                Repository: configuration["Repository"]?.Deserialize<ModulePublishRepositoryConfig>(JsonOptions)));
        }

        return results;
    }

    private async Task<ModulePackageDetails?> ResolveModulePackageDetailsAsync(
        string repositoryRoot,
        string repositoryName,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        var receipts = signingResult.Receipts
            .Where(receipt => string.Equals(receipt.AdapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var candidateManifest = receipts
            .Where(receipt => receipt.ArtifactPath.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) && File.Exists(receipt.ArtifactPath))
            .Select(receipt => receipt.ArtifactPath)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(candidateManifest))
        {
            foreach (var directory in receipts.Where(receipt => string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)))
            {
                if (!Directory.Exists(directory.ArtifactPath))
                {
                    continue;
                }

                candidateManifest = Directory.EnumerateFiles(directory.ArtifactPath, "*.psd1", SearchOption.AllDirectories)
                    .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}en-US{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(candidateManifest))
                {
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(candidateManifest))
        {
            return null;
        }

        var packagePath = Path.GetDirectoryName(candidateManifest);
        var manifestInfo = await ReadModuleManifestAsync(repositoryRoot, candidateManifest, cancellationToken)
                          ?? new ModuleManifestInfo(repositoryName, "0.0.0", null);
        var zipAssets = receipts
            .Select(receipt => receipt.ArtifactPath)
            .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModulePackageDetails(
            ModuleName: manifestInfo.ModuleName,
            Version: manifestInfo.Version,
            PreRelease: manifestInfo.PreRelease,
            PackagePath: packagePath!,
            ZipAssets: zipAssets);
    }

    private async Task<ModuleManifestInfo?> ReadModuleManifestAsync(string repositoryRoot, string manifestPath, CancellationToken cancellationToken)
    {
        var script = string.Join("; ", new[] {
            "$ErrorActionPreference = 'Stop'",
            $"$manifest = Import-PowerShellDataFile -Path {PowerShellScriptEscaping.QuoteLiteral(manifestPath)}",
            "$preRelease = $null",
            "if ($manifest.ContainsKey('PrivateData') -and $manifest.PrivateData -and $manifest.PrivateData.PSData) { $preRelease = $manifest.PrivateData.PSData.Prerelease }",
            "@{ ModuleName = $manifest.RootModule; ModuleVersion = $manifest.ModuleVersion.ToString(); PreRelease = $preRelease } | ConvertTo-Json -Compress"
        });

        var execution = await _commandRunner.RunCommandAsync(repositoryRoot, script, cancellationToken);
        if (execution.ExitCode != 0)
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<ModuleManifestJson>(execution.StandardOutput.Trim(), JsonOptions);
        if (manifest is null)
        {
            return null;
        }

        var moduleName = string.IsNullOrWhiteSpace(manifest.ModuleName)
            ? Path.GetFileNameWithoutExtension(manifestPath)
            : Path.GetFileNameWithoutExtension(manifest.ModuleName);
        return new ModuleManifestInfo(moduleName, manifest.ModuleVersion ?? "0.0.0", manifest.PreRelease);
    }
}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteModulePublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        var publishConfigs = await ExportModulePublishConfigsAsync(repository.RootPath, repository.ModuleBuildScriptPath!, cancellationToken);
        if (publishConfigs.Count == 0)
        {
            return [];
        }

        var packageDetails = await ResolveModulePackageDetailsAsync(repository.RootPath, repository.Name, signingResult, cancellationToken);
        var receipts = new List<ReleasePublishReceipt>();
        foreach (var publishConfig in publishConfigs.Where(config => config.Enabled))
        {
            if (string.Equals(publishConfig.Destination, "GitHub", StringComparison.OrdinalIgnoreCase))
            {
                receipts.Add(await ExecuteModuleGitHubPublishAsync(repository, publishConfig, packageDetails, cancellationToken));
                continue;
            }

            receipts.Add(await ExecuteModuleRepositoryPublishAsync(repository, publishConfig, packageDetails, cancellationToken));
        }

        return receipts;
    }

    private async Task<ReleasePublishReceipt> ExecuteModuleRepositoryPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ModulePublishConfig publishConfig,
        ModulePackageDetails? packageDetails,
        CancellationToken cancellationToken)
    {
        var destination = ResolveModuleRepositoryName(publishConfig);
        if (packageDetails is null || string.IsNullOrWhiteSpace(packageDetails.PackagePath))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "Module publish", destination, "No publishable module package path was captured from the build artefacts.");
        }

        if (publishConfig.Repository is not null &&
            (!string.IsNullOrWhiteSpace(publishConfig.Repository.Uri) ||
             !string.IsNullOrWhiteSpace(publishConfig.Repository.SourceUri) ||
             !string.IsNullOrWhiteSpace(publishConfig.Repository.PublishUri) ||
             publishConfig.Repository.Credential is not null))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "Module publish", destination, "Custom repository registration flows are not wired yet. Start with PSGallery or an already-registered repository.");
        }

        if (string.IsNullOrWhiteSpace(publishConfig.ApiKey))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "Module publish", destination, "Module publish is enabled but no API key was resolved.");
        }

        var script = BuildModuleRepositoryPublishScript(packageDetails.PackagePath, destination, publishConfig.ApiKey!, publishConfig.Tool);
        var execution = await _commandRunner.RunCommandAsync(repository.RootPath, script, cancellationToken);
        return new ReleasePublishReceipt(
            RootPath: repository.RootPath,
            RepositoryName: repository.Name,
            AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
            TargetName: packageDetails.ModuleName,
            TargetKind: "PowerShellRepository",
            Destination: destination,
            Status: execution.ExitCode == 0 ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
            Summary: execution.ExitCode == 0
                ? $"Module published to {destination}."
                : FirstLine(execution.StandardError) ?? FirstLine(execution.StandardOutput) ?? "Module publish failed.",
            PublishedAtUtc: DateTimeOffset.UtcNow,
            SourcePath: packageDetails.PackagePath);
    }

    private async Task<ReleasePublishReceipt> ExecuteModuleGitHubPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ModulePublishConfig publishConfig,
        ModulePackageDetails? packageDetails,
        CancellationToken cancellationToken)
    {
        if (packageDetails is null || packageDetails.ZipAssets.Count == 0)
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, "No packed module assets were found for GitHub publishing.");
        }

        if (string.IsNullOrWhiteSpace(publishConfig.UserName))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, "GitHub publishing requires UserName.");
        }

        if (string.IsNullOrWhiteSpace(publishConfig.ApiKey))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, "GitHub publishing is enabled but no token was resolved.");
        }

        var repoName = string.IsNullOrWhiteSpace(publishConfig.RepositoryName) ? repository.Name : publishConfig.RepositoryName!.Trim();
        var versionWithPreRelease = string.IsNullOrWhiteSpace(packageDetails.PreRelease)
            ? packageDetails.Version
            : $"{packageDetails.Version}-{packageDetails.PreRelease}";
        var tag = string.IsNullOrWhiteSpace(publishConfig.OverwriteTagName)
            ? $"v{versionWithPreRelease}"
            : ReplaceModuleTokens(publishConfig.OverwriteTagName!, packageDetails.ModuleName, packageDetails.Version, packageDetails.PreRelease);
        var isPreRelease = !string.IsNullOrWhiteSpace(packageDetails.PreRelease) && !publishConfig.DoNotMarkAsPreRelease;

        var execution = await PublishGitHubReleaseAsync(repository.RootPath, publishConfig.UserName!, repoName, publishConfig.ApiKey!, tag, tag, packageDetails.ZipAssets, publishConfig.GenerateReleaseNotes, isPreRelease, cancellationToken);
        return new ReleasePublishReceipt(
            RootPath: repository.RootPath,
            RepositoryName: repository.Name,
            AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
            TargetName: "GitHub release",
            TargetKind: "GitHub",
            Destination: execution.ReleaseUrl ?? $"{publishConfig.UserName}/{repoName}",
            Status: execution.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
            Summary: execution.Succeeded ? $"GitHub release {tag} published." : execution.ErrorMessage!,
            PublishedAtUtc: DateTimeOffset.UtcNow,
            SourcePath: packageDetails.ZipAssets.FirstOrDefault());
    }
}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteProjectPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        var scriptPath = repository.ProjectBuildScriptPath!;
        var configPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "project.build.json");
        if (!File.Exists(configPath))
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "Project publish", null, $"Project config was not found at {configPath}.")
            ];
        }

        var config = LoadProjectPublishConfig(configPath);
        var receipts = new List<ReleasePublishReceipt>();

        if (config.PublishNuget == true)
        {
            var apiKey = ResolveSecret(config.PublishApiKey, config.PublishApiKeyFilePath, config.PublishApiKeyEnvName, Path.GetDirectoryName(configPath)!);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                receipts.Add(FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "NuGet publish", config.PublishSource, "NuGet publishing is enabled but no API key was resolved."));
            }
            else
            {
                var packages = signingResult.Receipts
                    .Where(receipt => string.Equals(receipt.AdapterKind, ReleaseBuildAdapterKind.ProjectBuild.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Where(receipt => receipt.Status == Domain.Signing.ReleaseSigningReceiptStatus.Signed)
                    .Select(receipt => receipt.ArtifactPath)
                    .Where(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (packages.Count == 0)
                {
                    receipts.Add(FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "NuGet publish", config.PublishSource, "No signed .nupkg packages were found for publishing."));
                }
                else
                {
                    foreach (var package in packages)
                    {
                        var result = await PublishNugetPackageAsync(package, apiKey, config.PublishSource ?? "https://api.nuget.org/v3/index.json", cancellationToken);
                        receipts.Add(new ReleasePublishReceipt(
                            RootPath: repository.RootPath,
                            RepositoryName: repository.Name,
                            AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                            TargetName: Path.GetFileName(package),
                            TargetKind: "NuGet",
                            Destination: config.PublishSource ?? "https://api.nuget.org/v3/index.json",
                            Status: result.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                            Summary: result.Succeeded ? "Package pushed with dotnet nuget push." : result.ErrorMessage!,
                            PublishedAtUtc: DateTimeOffset.UtcNow,
                            SourcePath: package));
                    }
                }
            }
        }

        if (config.PublishGitHub == true)
        {
            receipts.AddRange(await ExecuteProjectGitHubPublishAsync(repository, config, signingResult, cancellationToken));
        }

        return receipts;
    }

    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteProjectGitHubPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ProjectPublishConfig config,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(Path.GetDirectoryName(repository.ProjectBuildScriptPath!)!, "project.build.json");
        var configDirectory = Path.GetDirectoryName(configPath)!;
        var token = ResolveSecret(config.GitHubAccessToken, config.GitHubAccessTokenFilePath, config.GitHubAccessTokenEnvName, configDirectory);
        if (string.IsNullOrWhiteSpace(token))
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", null, "GitHub publishing is enabled but no access token was resolved.")
            ];
        }

        if (string.IsNullOrWhiteSpace(config.GitHubUsername) || string.IsNullOrWhiteSpace(config.GitHubRepositoryName))
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", null, "GitHubUsername and GitHubRepositoryName are required for GitHub publishing.")
            ];
        }

        var plan = await GenerateProjectPlanAsync(repository, cancellationToken);
        if (plan is null)
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", $"{config.GitHubUsername}/{config.GitHubRepositoryName}", "Project release plan could not be generated for GitHub publishing.")
            ];
        }

        var releaseMode = string.IsNullOrWhiteSpace(config.GitHubReleaseMode) ? "Single" : config.GitHubReleaseMode!.Trim();
        var perProject = string.Equals(releaseMode, "PerProject", StringComparison.OrdinalIgnoreCase);
        var nowLocal = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        var dateToken = nowLocal.ToString("yyyy.MM.dd");
        var utcDateToken = nowUtc.ToString("yyyy.MM.dd");
        var dateTimeToken = nowLocal.ToString("yyyy.MM.dd-HH.mm.ss");
        var utcDateTimeToken = nowUtc.ToString("yyyy.MM.dd-HH.mm.ss");
        var timestampToken = nowLocal.ToString("yyyyMMddHHmmss");
        var utcTimestampToken = nowUtc.ToString("yyyyMMddHHmmss");
        var repoName = config.GitHubRepositoryName!.Trim();
        var owner = config.GitHubUsername!.Trim();

        if (perProject)
        {
            var receipts = new List<ReleasePublishReceipt>();
            foreach (var project in plan.Projects.Where(candidate => candidate.IsPackable))
            {
                var version = project.NewVersion ?? project.OldVersion;
                if (string.IsNullOrWhiteSpace(version))
                {
                    receipts.Add(FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), $"{project.ProjectName} GitHub release", $"{owner}/{repoName}", "Project version could not be resolved for GitHub publishing."));
                    continue;
                }

                var zipPath = !string.IsNullOrWhiteSpace(project.ReleaseZipPath) && File.Exists(project.ReleaseZipPath)
                    ? project.ReleaseZipPath
                    : FindZipAsset(signingResult, project.ProjectName);
                if (string.IsNullOrWhiteSpace(zipPath))
                {
                    receipts.Add(FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), $"{project.ProjectName} GitHub release", $"{owner}/{repoName}", "No release zip was found for GitHub publishing."));
                    continue;
                }

                var tag = string.IsNullOrWhiteSpace(config.GitHubTagName)
                    ? (config.GitHubIncludeProjectNameInTag == false ? $"v{version}" : $"{project.ProjectName}-v{version}")
                    : config.GitHubTagName!;
                if (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate))
                {
                    tag = ApplyProjectTemplate(
                        config.GitHubTagTemplate!,
                        project.ProjectName,
                        version,
                        config.GitHubPrimaryProject ?? project.ProjectName,
                        version,
                        repoName,
                        dateToken,
                        utcDateToken,
                        dateTimeToken,
                        utcDateTimeToken,
                        timestampToken,
                        utcTimestampToken);
                }

                var releaseName = string.IsNullOrWhiteSpace(config.GitHubReleaseName)
                    ? tag
                    : ApplyProjectTemplate(
                        config.GitHubReleaseName!,
                        project.ProjectName,
                        version,
                        config.GitHubPrimaryProject ?? project.ProjectName,
                        version,
                        repoName,
                        dateToken,
                        utcDateToken,
                        dateTimeToken,
                        utcDateTimeToken,
                        timestampToken,
                        utcTimestampToken);

                var execution = await PublishGitHubReleaseAsync(repository.RootPath, owner, repoName, token, tag, releaseName, [zipPath], config.GitHubGenerateReleaseNotes, config.GitHubIsPreRelease, cancellationToken);
                receipts.Add(new ReleasePublishReceipt(
                    RootPath: repository.RootPath,
                    RepositoryName: repository.Name,
                    AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    TargetName: $"{project.ProjectName} GitHub release",
                    TargetKind: "GitHub",
                    Destination: execution.ReleaseUrl ?? $"{owner}/{repoName}",
                    Status: execution.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                    Summary: execution.Succeeded ? $"GitHub release {tag} published." : execution.ErrorMessage!,
                    PublishedAtUtc: DateTimeOffset.UtcNow,
                    SourcePath: zipPath));
            }

            return receipts;
        }

        var assets = plan.Projects
            .Where(candidate => candidate.IsPackable)
            .Select(candidate => !string.IsNullOrWhiteSpace(candidate.ReleaseZipPath) && File.Exists(candidate.ReleaseZipPath)
                ? candidate.ReleaseZipPath!
                : FindZipAsset(signingResult, candidate.ProjectName))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (assets.Length == 0)
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", $"{owner}/{repoName}", "No release zips were found for GitHub publishing.")
            ];
        }

        var baseVersion = ResolveGitHubBaseVersion(config, plan);
        var tagVersionToken = string.IsNullOrWhiteSpace(baseVersion) ? dateToken : baseVersion!;
        var singleTag = !string.IsNullOrWhiteSpace(config.GitHubTagName)
            ? config.GitHubTagName!
            : (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate)
                ? ApplyProjectTemplate(
                    config.GitHubTagTemplate!,
                    repoName,
                    tagVersionToken,
                    config.GitHubPrimaryProject ?? repoName,
                    tagVersionToken,
                    repoName,
                    dateToken,
                    utcDateToken,
                    dateTimeToken,
                    utcDateTimeToken,
                    timestampToken,
                    utcTimestampToken)
                : $"v{tagVersionToken}");

        var releaseNameSingle = string.IsNullOrWhiteSpace(config.GitHubReleaseName)
            ? singleTag
            : ApplyProjectTemplate(
                config.GitHubReleaseName!,
                repoName,
                tagVersionToken,
                config.GitHubPrimaryProject ?? repoName,
                tagVersionToken,
                repoName,
                dateToken,
                utcDateToken,
                dateTimeToken,
                utcDateTimeToken,
                timestampToken,
                utcTimestampToken);

        var singleExecution = await PublishGitHubReleaseAsync(repository.RootPath, owner, repoName, token, singleTag, releaseNameSingle, assets!, config.GitHubGenerateReleaseNotes, config.GitHubIsPreRelease, cancellationToken);
        return [
            new ReleasePublishReceipt(
                RootPath: repository.RootPath,
                RepositoryName: repository.Name,
                AdapterKind: ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                TargetName: "GitHub release",
                TargetKind: "GitHub",
                Destination: singleExecution.ReleaseUrl ?? $"{owner}/{repoName}",
                Status: singleExecution.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                Summary: singleExecution.Succeeded ? $"GitHub release {singleTag} published with {assets.Length} asset(s)." : singleExecution.ErrorMessage!,
                PublishedAtUtc: DateTimeOffset.UtcNow,
                SourcePath: assets.FirstOrDefault())
        ];
    }
}
