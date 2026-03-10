using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseBuildExecutionService
{
    private readonly RepositoryCatalogScanner _catalogScanner = new();
    private readonly PowerShellCommandRunner _commandRunner = new();

    public async Task<ReleaseBuildExecutionResult> ExecuteAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var repository = _catalogScanner.InspectRepository(repositoryRoot);
        if (!repository.IsReleaseManaged)
        {
            return new ReleaseBuildExecutionResult(
                RootPath: repositoryRoot,
                Succeeded: false,
                Summary: "No supported build contract was detected for this repository.",
                DurationSeconds: 0,
                AdapterResults: []);
        }

        var results = new List<ReleaseBuildAdapterResult>();
        var startedAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
        {
            results.Add(await ExecuteProjectBuildAsync(repository, cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
        {
            results.Add(await ExecuteModuleBuildAsync(repository, cancellationToken));
        }

        var succeeded = results.Count > 0 && results.All(result => result.Succeeded);
        var summary = succeeded
            ? $"Build completed for {results.Count} adapter(s) without publish/install side effects."
            : FirstLine(results.FirstOrDefault(result => !result.Succeeded)?.ErrorTail
                ?? results.FirstOrDefault(result => !result.Succeeded)?.OutputTail
                ?? "Build execution failed.");

        return new ReleaseBuildExecutionResult(
            RootPath: repositoryRoot,
            Succeeded: succeeded,
            Summary: summary,
            DurationSeconds: Math.Round((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 2),
            AdapterResults: results);
    }

    private async Task<ReleaseBuildAdapterResult> ExecuteProjectBuildAsync(PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository, CancellationToken cancellationToken)
    {
        var scriptPath = repository.ProjectBuildScriptPath!;
        var configPath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "project.build.json");
        string? sanitizedConfigPath = null;

        if (File.Exists(configPath))
        {
            sanitizedConfigPath = PrepareProjectRuntimeConfig(repository.Name, configPath);
        }

        var script = BuildProjectScript(repository.RootPath, sanitizedConfigPath, ResolvePSPublishModulePath());
        var execution = await _commandRunner.RunCommandAsync(repository.RootPath, script, cancellationToken);
        var artifactInfo = CollectProjectArtifacts(sanitizedConfigPath, configPath);
        var succeeded = execution.ExitCode == 0;

        return new ReleaseBuildAdapterResult(
            AdapterKind: ReleaseBuildAdapterKind.ProjectBuild,
            Succeeded: succeeded,
            Summary: succeeded ? "Project build completed with publish disabled." : "Project build failed.",
            ExitCode: execution.ExitCode,
            DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
            ArtifactDirectories: artifactInfo.Directories,
            ArtifactFiles: artifactInfo.Files,
            OutputTail: TrimTail(execution.StandardOutput),
            ErrorTail: TrimTail(execution.StandardError));
    }

    private async Task<ReleaseBuildAdapterResult> ExecuteModuleBuildAsync(PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository, CancellationToken cancellationToken)
    {
        var scriptPath = repository.ModuleBuildScriptPath!;
        var modulePath = ResolvePSPublishModulePath();
        var script = BuildModuleScript(repository.RootPath, scriptPath, modulePath);
        var execution = await _commandRunner.RunCommandAsync(repository.RootPath, script, cancellationToken);
        var artifactInfo = CollectModuleArtifacts(scriptPath);
        var succeeded = execution.ExitCode == 0;

        return new ReleaseBuildAdapterResult(
            AdapterKind: ReleaseBuildAdapterKind.ModuleBuild,
            Succeeded: succeeded,
            Summary: succeeded ? "Module build completed with signing disabled and install skipped." : "Module build failed.",
            ExitCode: execution.ExitCode,
            DurationSeconds: Math.Round(execution.Duration.TotalSeconds, 2),
            ArtifactDirectories: artifactInfo.Directories,
            ArtifactFiles: artifactInfo.Files,
            OutputTail: TrimTail(execution.StandardOutput),
            ErrorTail: TrimTail(execution.StandardError));
    }

    private static string BuildProjectScript(string repositoryRoot, string? configPath, string modulePath)
    {
        var parts = new List<string> {
            "$ErrorActionPreference = 'Stop'",
            BuildModuleImportClause(modulePath),
            $"Set-Location -LiteralPath {QuoteLiteral(repositoryRoot)}"
        };

        var command = new StringBuilder();
        command.Append("Invoke-ProjectBuild -Build:$true -PublishNuget:$false -PublishGitHub:$false -UpdateVersions:$false");
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            command.Append(" -ConfigPath ").Append(QuoteLiteral(configPath));
        }

        parts.Add(command.ToString());
        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildModuleScript(string repositoryRoot, string scriptPath, string modulePath)
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

    private static string PrepareProjectRuntimeConfig(string repositoryName, string originalConfigPath)
    {
        var json = JsonNode.Parse(File.ReadAllText(originalConfigPath))?.AsObject()
                   ?? throw new InvalidOperationException($"Unable to parse project config {originalConfigPath}.");
        var configDirectory = Path.GetDirectoryName(originalConfigPath)!;

        NormalizeProjectPath(json, "RootPath", configDirectory);
        NormalizeProjectPath(json, "OutputPath", configDirectory);
        NormalizeProjectPath(json, "StagingPath", configDirectory);
        NormalizeProjectPath(json, "PlanOutputPath", configDirectory);

        json["Build"] = true;
        json["UpdateVersions"] = false;
        json["PublishNuget"] = false;
        json["PublishGitHub"] = false;
        json["CertificateThumbprint"] = null;
        json["CertificatePFXPath"] = null;
        json["CertificatePFXBase64"] = null;
        json["CertificatePFXPassword"] = null;

        var runtimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerForgeStudio",
            "runtime",
            SanitizePathSegment(repositoryName),
            "project");
        Directory.CreateDirectory(runtimeDirectory);

        var runtimeConfigPath = Path.Combine(runtimeDirectory, "project.build.runtime.json");
        File.WriteAllText(runtimeConfigPath, json.ToJsonString(new JsonSerializerOptions {
            WriteIndented = true
        }));

        return runtimeConfigPath;
    }

    private static void NormalizeProjectPath(JsonObject json, string propertyName, string configDirectory)
    {
        if (json[propertyName] is not JsonValue value || !value.TryGetValue<string>(out var propertyValue) || string.IsNullOrWhiteSpace(propertyValue))
        {
            return;
        }

        if (Path.IsPathRooted(propertyValue))
        {
            return;
        }

        json[propertyName] = Path.GetFullPath(Path.Combine(configDirectory, propertyValue));
    }

    private static ArtifactCollection CollectProjectArtifacts(string? sanitizedConfigPath, string originalConfigPath)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(sanitizedConfigPath) && File.Exists(sanitizedConfigPath))
        {
            var json = JsonNode.Parse(File.ReadAllText(sanitizedConfigPath))?.AsObject();
            AddArtifactDirectory(json?["StagingPath"], directories);
            AddArtifactDirectory(json?["OutputPath"], directories);
        }

        var defaultProjectBuild = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(originalConfigPath)!, "..", "Artefacts", "ProjectBuild"));
        if (Directory.Exists(defaultProjectBuild))
        {
            directories.Add(defaultProjectBuild);
        }

        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ArtifactCollection CollectModuleArtifacts(string moduleBuildScriptPath)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moduleRoot = Directory.GetParent(Path.GetDirectoryName(moduleBuildScriptPath)!)?.FullName;

        if (!string.IsNullOrWhiteSpace(moduleRoot))
        {
            var unpacked = Path.Combine(moduleRoot, "Artefacts", "Unpacked");
            var packed = Path.Combine(moduleRoot, "Artefacts", "Packed");

            if (Directory.Exists(unpacked))
            {
                directories.Add(unpacked);
            }

            if (Directory.Exists(packed))
            {
                directories.Add(packed);
            }
        }

        CollectArtifactFiles(directories, files);
        return new ArtifactCollection(directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void AddArtifactDirectory(JsonNode? node, ISet<string> directories)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var path) || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            directories.Add(path);
        }
    }

    private static void CollectArtifactFiles(IEnumerable<string> directories, ISet<string> files)
    {
        foreach (var directory in directories)
        {
            foreach (var extension in new[] { "*.nupkg", "*.snupkg", "*.zip", "*.psd1", "*.psm1", "*.dll" })
            {
                foreach (var file in Directory.EnumerateFiles(directory, extension, SearchOption.AllDirectories).Take(50))
                {
                    files.Add(file);
                }
            }
        }
    }

    private static string BuildModuleImportClause(string modulePath)
    {
        if (File.Exists(modulePath))
        {
            return $"try {{ Import-Module {QuoteLiteral(modulePath)} -Force -ErrorAction Stop }} catch {{ Import-Module PSPublishModule -Force -ErrorAction Stop }}";
        }

        return "Import-Module PSPublishModule -Force -ErrorAction Stop";
    }

    private static string ResolvePSPublishModulePath()
    {
        return PSPublishModuleLocator.ResolveModulePath();
    }

    private static string QuoteLiteral(string value)
        => "'" + value.Replace("'", "''") + "'";

    private static string SanitizePathSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static string FirstLine(string value)
        => value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? value;

    private static string? TrimTail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        const int maxLength = 600;
        return text.Length <= maxLength ? text.Trim() : text[^maxLength..].Trim();
    }

    private readonly record struct ArtifactCollection(
        IReadOnlyList<string> Directories,
        IReadOnlyList<string> Files);
}
