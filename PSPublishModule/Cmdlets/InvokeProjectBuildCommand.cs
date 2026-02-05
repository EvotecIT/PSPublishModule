using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

/// <summary>
/// Executes a repository-wide .NET build/release pipeline from a JSON configuration.
/// </summary>
/// <remarks>
/// <para>
/// The configuration file follows <c>Schemas/project.build.schema.json</c> in the PSPublishModule repository.
/// Use it to define discovery rules, versioning, staging, NuGet publishing, and GitHub release settings.
/// </para>
/// <para>
/// GitHub tag/release templates support tokens:
/// <c>{Project}</c>, <c>{Version}</c>, <c>{PrimaryProject}</c>, <c>{PrimaryVersion}</c>, <c>{Repo}</c>,
/// <c>{Repository}</c>, <c>{Date}</c>, <c>{UtcDate}</c>.
/// When GitHub release mode is Single and multiple project versions are present, <c>{Version}</c> defaults to
/// the local date (<c>yyyy.MM.dd</c>) unless a primary project version is available.
/// </para>
/// </remarks>
[Cmdlet(VerbsLifecycle.Invoke, "ProjectBuild", SupportsShouldProcess = true)]
[OutputType(typeof(ProjectBuildResult))]
public sealed class InvokeProjectBuildCommand : PSCmdlet
{
    /// <summary>Path to the JSON configuration file.</summary>
    [Parameter(Mandatory = true)]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Run version update step.</summary>
    [Parameter]
    public SwitchParameter UpdateVersions { get; set; }

    /// <summary>Run build/pack step.</summary>
    [Parameter]
    public SwitchParameter Build { get; set; }

    /// <summary>Publish packages to NuGet.</summary>
    [Parameter]
    public SwitchParameter PublishNuget { get; set; }

    /// <summary>Publish artifacts to GitHub.</summary>
    [Parameter]
    public SwitchParameter PublishGitHub { get; set; }

    /// <summary>Generate a plan only (no build/publish actions).</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Optional path to write the plan JSON file.</summary>
    [Parameter]
    public string? PlanPath { get; set; }

    /// <summary>Executes the configured build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var bound = MyInvocation?.BoundParameters;
        var isVerbose = bound?.ContainsKey("Verbose") == true;

        ConsoleEncoding.EnsureUtf8();
        try
        {
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                AnsiConsole.Profile.Capabilities.Unicode = true;
        }
        catch
        {
            // best effort only
        }

        var interactive = SpectrePipelineConsoleUi.ShouldUseInteractiveView(isVerbose);
        ILogger logger = interactive
            ? new SpectreConsoleLogger { IsVerbose = isVerbose }
            : new CmdletLogger(this, isVerbose);

        var configFullPath = ResolveConfigPath(ConfigPath);
        var configDir = Path.GetDirectoryName(configFullPath) ?? SessionState.Path.CurrentFileSystemLocation.Path;
        var config = LoadConfig(configFullPath);

        object? planValue = null;
        object? updateValue = null;
        object? buildValue = null;
        object? publishNugetValue = null;
        object? publishGitHubValue = null;

        var planProvided = bound?.TryGetValue(nameof(Plan), out planValue) == true;
        var updateProvided = bound?.TryGetValue(nameof(UpdateVersions), out updateValue) == true;
        var buildProvided = bound?.TryGetValue(nameof(Build), out buildValue) == true;
        var publishNugetProvided = bound?.TryGetValue(nameof(PublishNuget), out publishNugetValue) == true;
        var publishGitHubProvided = bound?.TryGetValue(nameof(PublishGitHub), out publishGitHubValue) == true;

        bool planOnly = planProvided ? IsTrue(planValue) : (config.PlanOnly ?? false);
        bool updateVersions = updateProvided ? IsTrue(updateValue) : (config.UpdateVersions ?? false);
        bool build = buildProvided ? IsTrue(buildValue) : (config.Build ?? false);
        bool publishNuget = publishNugetProvided ? IsTrue(publishNugetValue) : (config.PublishNuget ?? false);
        bool publishGitHub = publishGitHubProvided ? IsTrue(publishGitHubValue) : (config.PublishGitHub ?? false);

        var anyConfigSpecified = config.UpdateVersions is not null || config.Build is not null ||
                                 config.PublishNuget is not null || config.PublishGitHub is not null;
        var anyProvided = updateProvided || buildProvided || publishNugetProvided || publishGitHubProvided;

        if (!anyConfigSpecified && !anyProvided)
        {
            updateVersions = true;
            build = true;
            publishNuget = true;
            publishGitHub = true;
        }

        if (!updateVersions && !build && !publishNuget && !publishGitHub)
        {
            WriteObject(new ProjectBuildResult
            {
                Success = false,
                ErrorMessage = "Nothing to do. Enable UpdateVersions, Build, PublishNuget, or PublishGitHub."
            });
            return;
        }

        var rootPath = ResolveOptionalPath(config.RootPath, configDir) ?? configDir;
        var stagingPath = ResolveOptionalPath(config.StagingPath, rootPath);
        var outputPath = ResolveOptionalPath(config.OutputPath, rootPath);
        var releaseZipOutputPath = ResolveOptionalPath(config.ReleaseZipOutputPath, rootPath);
        var planOutputPath = ResolveOptionalPath(PlanPath ?? config.PlanOutputPath, configDir);

        if (string.IsNullOrWhiteSpace(outputPath) && !string.IsNullOrWhiteSpace(stagingPath))
            outputPath = Path.Combine(stagingPath, "packages");
        if (string.IsNullOrWhiteSpace(releaseZipOutputPath) && !string.IsNullOrWhiteSpace(stagingPath))
            releaseZipOutputPath = Path.Combine(stagingPath, "releases");

        var nugetCredentialSecret = ResolveSecret(config.NugetCredentialSecret, config.NugetCredentialSecretFilePath, config.NugetCredentialSecretEnvName, configDir);
        var nugetUser = string.IsNullOrWhiteSpace(config.NugetCredentialUserName) ? null : config.NugetCredentialUserName!.Trim();
        var nugetCredential = (!string.IsNullOrWhiteSpace(nugetUser) || !string.IsNullOrWhiteSpace(nugetCredentialSecret))
            ? new RepositoryCredential
            {
                UserName = nugetUser,
                Secret = nugetCredentialSecret
            }
            : null;

        var publishApiKey = ResolveSecret(config.PublishApiKey, config.PublishApiKeyFilePath, config.PublishApiKeyEnvName, configDir);

        var store = ParseCertificateStore(config.CertificateStore);
        var createReleaseZip = config.CreateReleaseZip ?? true;
        var spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = rootPath,
            ExpectedVersion = config.ExpectedVersion,
            ExpectedVersionsByProject = config.ExpectedVersionMap,
            ExpectedVersionMapAsInclude = config.ExpectedVersionMapAsInclude,
            ExpectedVersionMapUseWildcards = config.ExpectedVersionMapUseWildcards,
            IncludeProjects = config.IncludeProjects,
            ExcludeProjects = config.ExcludeProjects,
            ExcludeDirectories = config.ExcludeDirectories,
            VersionSources = config.NugetSource,
            VersionSourceCredential = nugetCredential,
            IncludePrerelease = config.IncludePrerelease,
            Configuration = string.IsNullOrWhiteSpace(config.Configuration) ? "Release" : config.Configuration!,
            OutputPath = outputPath,
            ReleaseZipOutputPath = releaseZipOutputPath,
            CertificateThumbprint = config.CertificateThumbprint,
            CertificateStore = store,
            TimeStampServer = config.TimeStampServer,
            Pack = build || publishNuget || publishGitHub,
            CreateReleaseZip = createReleaseZip,
            Publish = publishNuget,
            PublishSource = config.PublishSource,
            PublishApiKey = publishApiKey,
            SkipDuplicate = config.SkipDuplicate ?? true,
            PublishFailFast = config.PublishFailFast ?? true,
            UpdateVersions = updateVersions
        };

        var runner = new DotNetRepositoryReleaseService(logger);
        spec.WhatIf = true;
        var plan = runner.Execute(spec);

        if (planOnly || !ShouldProcess(rootPath, "Build project repository"))
        {
            TryWritePlan(plan, planOutputPath, logger);
            WriteObject(new ProjectBuildResult
            {
                Success = plan.Success,
                ErrorMessage = plan.ErrorMessage,
                Release = plan
            });
            return;
        }

        var preflightError = ValidatePreflight(publishNuget, publishGitHub, createReleaseZip, publishApiKey, config, configDir);
        if (!string.IsNullOrWhiteSpace(preflightError))
        {
            WriteObject(new ProjectBuildResult
            {
                Success = false,
                ErrorMessage = preflightError,
                Release = plan
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(stagingPath))
            PrepareStaging(stagingPath!, config.CleanStaging ?? false, logger);
        EnsureDirectory(outputPath);
        EnsureDirectory(releaseZipOutputPath);
        TryWritePlan(plan, planOutputPath, logger);

        spec.WhatIf = false;
        var release = runner.Execute(spec);
        var result = new ProjectBuildResult { Release = release };

        if (release is null || !release.Success)
        {
            result.Success = false;
            result.ErrorMessage = release?.ErrorMessage ?? "Release pipeline failed.";
            WriteObject(result);
            return;
        }

        if (!publishGitHub)
        {
            result.Success = true;
            WriteObject(result);
            return;
        }

        var gitHubToken = ResolveSecret(config.GitHubAccessToken, config.GitHubAccessTokenFilePath, config.GitHubAccessTokenEnvName, configDir);
        if (string.IsNullOrWhiteSpace(gitHubToken))
        {
            result.Success = false;
            result.ErrorMessage = "GitHub access token is required for GitHub publishing.";
            WriteObject(result);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.GitHubUsername) || string.IsNullOrWhiteSpace(config.GitHubRepositoryName))
        {
            result.Success = false;
            result.ErrorMessage = "GitHubUsername and GitHubRepositoryName are required for GitHub publishing.";
            WriteObject(result);
            return;
        }

        var sb = ScriptBlock.Create(@"
param($u,$r,$t,$tag,$name,$asset,$pre,$gen)
Send-GitHubRelease -GitHubUsername $u -GitHubRepositoryName $r -GitHubAccessToken $t -TagName $tag -ReleaseName $name -AssetFilePaths $asset -IsPreRelease:$pre -GenerateReleaseNotes:$gen
");

        var releaseMode = string.IsNullOrWhiteSpace(config.GitHubReleaseMode)
            ? "Single"
            : config.GitHubReleaseMode!.Trim();
        var perProject = string.Equals(releaseMode, "PerProject", StringComparison.OrdinalIgnoreCase);
        var nowLocal = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        var dateToken = nowLocal.ToString("yyyy.MM.dd");
        var utcDateToken = nowUtc.ToString("yyyy.MM.dd");
        var repoName = string.IsNullOrWhiteSpace(config.GitHubRepositoryName)
            ? "repository"
            : config.GitHubRepositoryName!.Trim();

        if (perProject)
        {
            foreach (var project in release.Projects)
            {
                if (!project.IsPackable) continue;
                var r = new ProjectBuildGitHubResult { ProjectName = project.ProjectName };

                if (string.IsNullOrWhiteSpace(project.NewVersion))
                {
                    r.Success = false;
                    r.ErrorMessage = "Missing project version for GitHub release.";
                    result.GitHub.Add(r);
                    if (spec.PublishFailFast)
                    {
                        result.Success = false;
                        result.ErrorMessage = r.ErrorMessage;
                        break;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(project.ReleaseZipPath))
                {
                    r.Success = false;
                    r.ErrorMessage = "No release zip available for GitHub release.";
                    result.GitHub.Add(r);
                    if (spec.PublishFailFast)
                    {
                        result.Success = false;
                        result.ErrorMessage = r.ErrorMessage;
                        break;
                    }
                    continue;
                }

                if (!File.Exists(project.ReleaseZipPath))
                {
                    r.Success = false;
                    r.ErrorMessage = $"Release zip not found: {project.ReleaseZipPath}";
                    result.GitHub.Add(r);
                    if (spec.PublishFailFast)
                    {
                        result.Success = false;
                        result.ErrorMessage = r.ErrorMessage;
                        break;
                    }
                    continue;
                }

                var tag = string.IsNullOrWhiteSpace(config.GitHubTagName)
                    ? (config.GitHubIncludeProjectNameInTag == false ? $"v{project.NewVersion}" : $"{project.ProjectName}-v{project.NewVersion}")
                    : config.GitHubTagName!;
                if (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate))
                {
                    tag = ApplyTemplate(
                        config.GitHubTagTemplate!,
                        project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        config.GitHubPrimaryProject ?? project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        repoName,
                        dateToken,
                        utcDateToken);
                }

                var releaseName = string.IsNullOrWhiteSpace(config.GitHubReleaseName)
                    ? tag
                    : ApplyTemplate(
                        config.GitHubReleaseName!,
                        project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        config.GitHubPrimaryProject ?? project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        repoName,
                        dateToken,
                        utcDateToken);

                var output = sb.Invoke(config.GitHubUsername, config.GitHubRepositoryName, gitHubToken, tag, releaseName, new[] { project.ReleaseZipPath }, config.GitHubIsPreRelease, config.GitHubGenerateReleaseNotes);
                var status = output.Count > 0 ? output[0]?.BaseObject : null;

                bool ok = false;
                string? releaseUrl = null;
                string? errorMessage = null;

                if (status is SendGitHubReleaseCommand.GitHubReleaseResult gr)
                {
                    ok = gr.Succeeded;
                    releaseUrl = gr.ReleaseUrl;
                    errorMessage = gr.Succeeded ? null : gr.ErrorMessage;
                }
                else if (status is PSObject pso)
                {
                    var succeeded = pso.Properties["Succeeded"]?.Value as bool?;
                    ok = succeeded ?? false;
                    releaseUrl = pso.Properties["ReleaseUrl"]?.Value?.ToString();
                    errorMessage = pso.Properties["ErrorMessage"]?.Value?.ToString();
                }
                else
                {
                    errorMessage = "Unexpected result from Send-GitHubRelease.";
                }

                r.Success = ok;
                r.TagName = tag;
                r.ReleaseUrl = releaseUrl;
                r.ErrorMessage = errorMessage;
                result.GitHub.Add(r);

                if (!ok)
                {
                    result.Success = false;
                    result.ErrorMessage = errorMessage ?? "GitHub publish failed.";
                    if (spec.PublishFailFast)
                        break;
                }
            }
        }
        else
        {
            var assets = new List<string>();
            foreach (var project in release.Projects)
            {
                if (!project.IsPackable) continue;
                var zip = project.ReleaseZipPath;
                if (string.IsNullOrWhiteSpace(zip))
                    continue;
                if (File.Exists(zip))
                    assets.Add(zip!);
            }

            var distinctAssets = assets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (distinctAssets.Length == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No release zips available for GitHub release.";
                WriteObject(result);
                return;
            }

            var baseVersion = ResolveGitHubBaseVersion(config, release);
            var tagVersionToken = string.IsNullOrWhiteSpace(baseVersion) ? dateToken : baseVersion!;

            var tag = !string.IsNullOrWhiteSpace(config.GitHubTagName)
                ? config.GitHubTagName!
                : (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate)
                    ? ApplyTemplate(
                        config.GitHubTagTemplate!,
                        repoName,
                        tagVersionToken,
                        config.GitHubPrimaryProject ?? repoName,
                        tagVersionToken,
                        repoName,
                        dateToken,
                        utcDateToken)
                    : $"v{tagVersionToken}");

            var releaseName = string.IsNullOrWhiteSpace(config.GitHubReleaseName)
                ? tag
                : ApplyTemplate(
                    config.GitHubReleaseName!,
                    repoName,
                    tagVersionToken,
                    config.GitHubPrimaryProject ?? repoName,
                    tagVersionToken,
                    repoName,
                    dateToken,
                    utcDateToken);

            var output = sb.Invoke(config.GitHubUsername, config.GitHubRepositoryName, gitHubToken, tag, releaseName, distinctAssets, config.GitHubIsPreRelease, config.GitHubGenerateReleaseNotes);
            var status = output.Count > 0 ? output[0]?.BaseObject : null;

            bool ok = false;
            string? releaseUrl = null;
            string? errorMessage = null;

            if (status is SendGitHubReleaseCommand.GitHubReleaseResult gr)
            {
                ok = gr.Succeeded;
                releaseUrl = gr.ReleaseUrl;
                errorMessage = gr.Succeeded ? null : gr.ErrorMessage;
            }
            else if (status is PSObject pso)
            {
                var succeeded = pso.Properties["Succeeded"]?.Value as bool?;
                ok = succeeded ?? false;
                releaseUrl = pso.Properties["ReleaseUrl"]?.Value?.ToString();
                errorMessage = pso.Properties["ErrorMessage"]?.Value?.ToString();
            }
            else
            {
                errorMessage = "Unexpected result from Send-GitHubRelease.";
            }

            foreach (var project in release.Projects.Where(p => p.IsPackable))
            {
                result.GitHub.Add(new ProjectBuildGitHubResult
                {
                    ProjectName = project.ProjectName,
                    Success = ok,
                    TagName = tag,
                    ReleaseUrl = releaseUrl,
                    ErrorMessage = ok ? null : errorMessage
                });
            }

            result.Success = ok;
            result.ErrorMessage = ok ? null : (errorMessage ?? "GitHub publish failed.");
            if (interactive)
                WriteGitHubSummary(perProject, tag, releaseUrl, distinctAssets.Length, result.GitHub);
        }

        if (result.ErrorMessage is null)
            result.Success = result.GitHub.Count == 0 || result.GitHub.TrueForAll(g => g.Success);

        WriteObject(result);
    }

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
        string utcDate)
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
            .Replace("{UtcDate}", utcDate ?? string.Empty);
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
    }
}

/// <summary>Aggregate result for project builds.</summary>
public sealed class ProjectBuildResult
{
    /// <summary>True when the pipeline completed successfully.</summary>
    public bool Success { get; set; }
    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Release pipeline result.</summary>
    public DotNetRepositoryReleaseResult? Release { get; set; }
    /// <summary>GitHub publishing results.</summary>
    public List<ProjectBuildGitHubResult> GitHub { get; } = new();
}

/// <summary>GitHub publish result per project.</summary>
public sealed class ProjectBuildGitHubResult
{
    /// <summary>Project name.</summary>
    public string ProjectName { get; set; } = string.Empty;
    /// <summary>True when publishing succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>Computed tag name.</summary>
    public string? TagName { get; set; }
    /// <summary>Release URL when publishing succeeded.</summary>
    public string? ReleaseUrl { get; set; }
    /// <summary>Error message when publishing failed.</summary>
    public string? ErrorMessage { get; set; }
}
