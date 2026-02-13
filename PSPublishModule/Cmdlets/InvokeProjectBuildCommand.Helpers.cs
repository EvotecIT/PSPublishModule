using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

public sealed partial class InvokeProjectBuildCommand
{
    private string ResolveConfigPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PSArgumentException("ConfigPath is required.");

        try { return SessionState.Path.GetUnresolvedProviderPathFromPSPath(path); }
        catch { return Path.GetFullPath(path); }
    }

    private static ProjectBuildConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Config file not found.", path);

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var config = JsonSerializer.Deserialize<ProjectBuildConfig>(json, options);
        if (config is null)
            throw new InvalidOperationException("Config file could not be parsed.");
        return config;
    }

    private static string? ResolveOptionalPath(string? value, string basePath)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (Path.IsPathRooted(trimmed)) return Path.GetFullPath(trimmed);
        return Path.GetFullPath(Path.Combine(basePath, trimmed));
    }

    private static string? ResolveSecret(string? inline, string? filePath, string? envName, string basePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                var full = Path.IsPathRooted(filePath)
                    ? filePath
                    : Path.GetFullPath(Path.Combine(basePath, filePath));
                if (File.Exists(full))
                    return File.ReadAllText(full).Trim();
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(envName))
        {
            try
            {
                var value = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(inline))
            return inline!.Trim();

        return null;
    }

    private static PowerForge.CertificateStoreLocation ParseCertificateStore(string? store)
    {
        if (string.IsNullOrWhiteSpace(store)) return PowerForge.CertificateStoreLocation.CurrentUser;
        return string.Equals(store!.Trim(), "LocalMachine", StringComparison.OrdinalIgnoreCase)
            ? PowerForge.CertificateStoreLocation.LocalMachine
            : PowerForge.CertificateStoreLocation.CurrentUser;
    }

    private static bool IsTrue(object? value)
    {
        if (value is null) return false;
        if (value is SwitchParameter sp) return sp.IsPresent;
        if (value is bool b) return b;
        if (value is int i) return i != 0;
        if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
        return false;
    }

    private static void EnsureDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
    }

    private static void PrepareStaging(string path, bool clean, ILogger logger)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (clean && Directory.Exists(full))
        {
            if (IsRootPath(full))
                throw new InvalidOperationException($"Refusing to clean staging root: {full}");
            logger.Info($"Cleaning staging path: {full}");
            Directory.Delete(full, true);
        }

        Directory.CreateDirectory(full);
    }

    private static bool IsRootPath(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root)) return false;
        return string.Equals(
            full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryWritePlan(DotNetRepositoryReleaseResult plan, string? path, ILogger logger)
    {
        if (plan is null) return;
        var targetPath = path;
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        try
        {
            var full = Path.GetFullPath(targetPath!.Trim().Trim('"'));
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(plan, options) + Environment.NewLine;
            File.WriteAllText(full, json);
            logger.Info($"Plan written to {full}");
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to write plan file: {ex.Message}");
        }
    }

    private static string? ValidatePreflight(
        bool publishNuget,
        bool publishGitHub,
        bool createReleaseZip,
        string? publishApiKey,
        ProjectBuildConfig config,
        string configDir)
    {
        if (publishNuget && string.IsNullOrWhiteSpace(publishApiKey))
            return "PublishNuget is enabled but no PublishApiKey was resolved (use PublishApiKey, PublishApiKeyFilePath, or PublishApiKeyEnvName).";

        if (publishGitHub && !createReleaseZip)
            return "PublishGitHub is enabled but CreateReleaseZip is false.";

        if (!publishGitHub)
            return null;

        var token = ResolveSecret(config.GitHubAccessToken, config.GitHubAccessTokenFilePath, config.GitHubAccessTokenEnvName, configDir);
        if (string.IsNullOrWhiteSpace(token))
            return "PublishGitHub is enabled but GitHubAccessToken is missing (use GitHubAccessToken, GitHubAccessTokenFilePath, or GitHubAccessTokenEnvName).";

        if (string.IsNullOrWhiteSpace(config.GitHubUsername) || string.IsNullOrWhiteSpace(config.GitHubRepositoryName))
            return "PublishGitHub is enabled but GitHubUsername/GitHubRepositoryName are not set.";

        return null;
    }

    private static string? ResolveGitHubBaseVersion(ProjectBuildConfig config, DotNetRepositoryReleaseResult release)
    {
        if (!string.IsNullOrWhiteSpace(config.GitHubPrimaryProject))
        {
            var match = release.Projects.FirstOrDefault(p =>
                string.Equals(p.ProjectName, config.GitHubPrimaryProject, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.NewVersion ?? match.OldVersion;
        }

        var versions = release.Projects
            .Where(p => p.IsPackable && !string.IsNullOrWhiteSpace(p.NewVersion))
            .Select(p => p.NewVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return versions.Length == 1 ? versions[0] : null;
    }

    private static string ApplyTemplate(
        string template,
        string project,
        string version,
        string primaryProject,
        string primaryVersion,
        string repo,
        string date,
        string utcDate,
        string dateTime,
        string utcDateTime,
        string timestamp,
        string utcTimestamp)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;
        return template
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
    }

    private static GitHubTagConflictPolicy ParseGitHubTagConflictPolicy(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return GitHubTagConflictPolicy.Reuse;

        if (Enum.TryParse<GitHubTagConflictPolicy>(text, ignoreCase: true, out var parsed))
            return parsed;

        return GitHubTagConflictPolicy.Reuse;
    }

    private static string ApplyTagConflictPolicy(
        string tag,
        GitHubTagConflictPolicy policy,
        string utcTimestampToken)
    {
        if (string.IsNullOrWhiteSpace(tag)) return tag;

        return policy switch
        {
            GitHubTagConflictPolicy.AppendUtcTimestamp => $"{tag}-{utcTimestampToken}",
            _ => tag
        };
    }

    private static void WriteGitHubSummary(
        bool perProject,
        string? tag,
        string? releaseUrl,
        int assetsCount,
        IReadOnlyList<ProjectBuildGitHubResult> results)
    {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;
        var title = unicode ? "✅ GitHub Summary" : "GitHub Summary";
        AnsiConsole.Write(new Rule($"[green]{title}[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        if (!perProject)
        {
            table.AddRow("Mode", "Single");
            table.AddRow("Tag", Markup.Escape(tag ?? string.Empty));
            table.AddRow("Assets", assetsCount.ToString());
            if (!string.IsNullOrWhiteSpace(releaseUrl))
                table.AddRow("Release", Markup.Escape(releaseUrl!));
        }
        else
        {
            var ok = results.Count(r => r.Success);
            var fail = results.Count(r => !r.Success);
            table.AddRow("Mode", "PerProject");
            table.AddRow("Projects", results.Count.ToString());
            table.AddRow("Succeeded", ok.ToString());
            table.AddRow("Failed", fail.ToString());
        }

        AnsiConsole.Write(table);
    }

    private sealed class ProjectBuildConfig
    {
        public string? RootPath { get; set; }
        public string? ExpectedVersion { get; set; }
        public Dictionary<string, string>? ExpectedVersionMap { get; set; }
        public bool ExpectedVersionMapAsInclude { get; set; }
        public bool ExpectedVersionMapUseWildcards { get; set; }
        public string[]? IncludeProjects { get; set; }
        public string[]? ExcludeProjects { get; set; }
        public string[]? ExcludeDirectories { get; set; }
        public string[]? NugetSource { get; set; }
        public bool IncludePrerelease { get; set; }
        public string? Configuration { get; set; }
        public string? OutputPath { get; set; }
        public string? ReleaseZipOutputPath { get; set; }
        public string? StagingPath { get; set; }
        public bool? CleanStaging { get; set; }
        public bool? PlanOnly { get; set; }
        public string? PlanOutputPath { get; set; }
        public bool? UpdateVersions { get; set; }
        public bool? Build { get; set; }
        public bool? PublishNuget { get; set; }
        public bool? PublishGitHub { get; set; }
        public bool? CreateReleaseZip { get; set; }
        public string? PublishSource { get; set; }
        public string? PublishApiKey { get; set; }
        public string? PublishApiKeyFilePath { get; set; }
        public string? PublishApiKeyEnvName { get; set; }
        public bool? SkipDuplicate { get; set; }
        public bool? PublishFailFast { get; set; }
        public string? CertificateThumbprint { get; set; }
        public string? CertificateStore { get; set; }
        public string? TimeStampServer { get; set; }
        public string? NugetCredentialUserName { get; set; }
        public string? NugetCredentialSecret { get; set; }
        public string? NugetCredentialSecretFilePath { get; set; }
        public string? NugetCredentialSecretEnvName { get; set; }
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
        public string? GitHubTagConflictPolicy { get; set; }
    }
}
