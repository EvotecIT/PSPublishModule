namespace PowerForge;

/// <summary>
/// Orchestrates package and tool release workflows from one unified configuration.
/// </summary>
internal sealed class PowerForgeReleaseService
{
    private readonly ILogger _logger;
    private readonly Func<ProjectBuildHostRequest, ProjectBuildConfiguration, string, ProjectBuildHostExecutionResult> _executePackages;
    private readonly Func<PowerForgeToolReleaseSpec, string, PowerForgeReleaseRequest, PowerForgeToolReleasePlan> _planTools;
    private readonly Func<PowerForgeToolReleasePlan, PowerForgeToolReleaseResult> _runTools;
    private readonly Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> _publishGitHubRelease;

    /// <summary>
    /// Creates a new unified release service.
    /// </summary>
    public PowerForgeReleaseService(ILogger logger)
        : this(
            logger,
            (request, config, configPath) => new ProjectBuildHostService(logger).Execute(request, config, configPath),
            (spec, configPath, request) => new PowerForgeToolReleaseService(logger).Plan(spec, configPath, request),
            plan => new PowerForgeToolReleaseService(logger).Run(plan),
            publishRequest => new GitHubReleasePublisher(logger).PublishRelease(publishRequest))
    {
    }

    internal PowerForgeReleaseService(
        ILogger logger,
        Func<ProjectBuildHostRequest, ProjectBuildConfiguration, string, ProjectBuildHostExecutionResult> executePackages,
        Func<PowerForgeToolReleaseSpec, string, PowerForgeReleaseRequest, PowerForgeToolReleasePlan> planTools,
        Func<PowerForgeToolReleasePlan, PowerForgeToolReleaseResult> runTools,
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> publishGitHubRelease)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executePackages = executePackages ?? throw new ArgumentNullException(nameof(executePackages));
        _planTools = planTools ?? throw new ArgumentNullException(nameof(planTools));
        _runTools = runTools ?? throw new ArgumentNullException(nameof(runTools));
        _publishGitHubRelease = publishGitHubRelease ?? throw new ArgumentNullException(nameof(publishGitHubRelease));
    }

    /// <summary>
    /// Executes the unified release workflow.
    /// </summary>
    public PowerForgeReleaseResult Execute(PowerForgeReleaseSpec spec, PowerForgeReleaseRequest request)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            throw new ArgumentException("ConfigPath is required.", nameof(request));

        var configPath = Path.GetFullPath(request.ConfigPath.Trim().Trim('"'));
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        var runPackages = !request.ToolsOnly && spec.Packages is not null;
        var runTools = !request.PackagesOnly && spec.Tools is not null;

        if (!runPackages && !runTools)
        {
            return new PowerForgeReleaseResult
            {
                Success = false,
                ConfigPath = configPath,
                ErrorMessage = "Release config does not enable any selected Packages or Tools sections."
            };
        }

        var result = new PowerForgeReleaseResult
        {
            Success = true,
            ConfigPath = configPath
        };

        if (runPackages)
        {
            var packageRequest = new ProjectBuildHostRequest
            {
                ConfigPath = configPath,
                ExecuteBuild = !request.PlanOnly && !request.ValidateOnly,
                PlanOnly = request.PlanOnly || request.ValidateOnly ? true : null,
                PublishNuget = request.PublishNuget,
                PublishGitHub = request.PublishProjectGitHub
            };

            var packages = _executePackages(packageRequest, spec.Packages!, configPath);
            result.Packages = packages;
            if (!packages.Success)
            {
                result.Success = false;
                result.ErrorMessage = packages.ErrorMessage ?? "Package release workflow failed.";
                return result;
            }
        }

        if (runTools)
        {
            var toolPlan = _planTools(spec.Tools!, configPath, request);
            result.ToolPlan = toolPlan;

            if (!request.PlanOnly && !request.ValidateOnly)
            {
                var tools = _runTools(toolPlan);
                result.Tools = tools;
                if (!tools.Success)
                {
                    result.Success = false;
                    result.ErrorMessage = tools.ErrorMessage ?? "Tool release workflow failed.";
                    return result;
                }

                var publishToolGitHub = request.PublishToolGitHub ?? spec.Tools!.GitHub.Publish;
                if (publishToolGitHub)
                {
                    var releases = PublishToolGitHubReleases(spec, configDirectory, toolPlan, tools);
                    result.ToolGitHubReleases = releases;
                    var failures = releases.Where(entry => !entry.Success).ToArray();
                    if (failures.Length > 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = failures[0].ErrorMessage ?? "Tool GitHub release publishing failed.";
                        return result;
                    }
                }
            }
        }

        return result;
    }

    private PowerForgeToolGitHubReleaseResult[] PublishToolGitHubReleases(
        PowerForgeReleaseSpec spec,
        string configDirectory,
        PowerForgeToolReleasePlan plan,
        PowerForgeToolReleaseResult result)
    {
        var gitHub = spec.Tools?.GitHub ?? new PowerForgeToolReleaseGitHubOptions();
        var owner = string.IsNullOrWhiteSpace(gitHub.Owner)
            ? spec.Packages?.GitHubUsername
            : gitHub.Owner!.Trim();
        var repository = string.IsNullOrWhiteSpace(gitHub.Repository)
            ? spec.Packages?.GitHubRepositoryName
            : gitHub.Repository!.Trim();
        var token = ProjectBuildSupportService.ResolveSecret(
            gitHub.Token,
            gitHub.TokenFilePath,
            gitHub.TokenEnvName,
            configDirectory);

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            return new[]
            {
                new PowerForgeToolGitHubReleaseResult
                {
                    Success = false,
                    ErrorMessage = "Tool GitHub publishing requires Owner and Repository."
                }
            };
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new[]
            {
                new PowerForgeToolGitHubReleaseResult
                {
                    Success = false,
                    ErrorMessage = "Tool GitHub publishing requires a token."
                }
            };
        }

        var tagTemplate = string.IsNullOrWhiteSpace(gitHub.TagTemplate)
            ? "{Target}-v{Version}"
            : gitHub.TagTemplate!;
        var releaseNameTemplate = string.IsNullOrWhiteSpace(gitHub.ReleaseNameTemplate)
            ? "{Target} {Version}"
            : gitHub.ReleaseNameTemplate!;

        var artefactGroups = result.Artefacts
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ZipPath))
            .GroupBy(entry => (Target: entry.Target, Version: entry.Version))
            .ToArray();

        if (artefactGroups.Length == 0)
        {
            return new[]
            {
                new PowerForgeToolGitHubReleaseResult
                {
                    Success = false,
                    ErrorMessage = "Tool GitHub publishing requires zip assets, but no ZipPath values were produced."
                }
            };
        }

        var results = new List<PowerForgeToolGitHubReleaseResult>();
        foreach (var group in artefactGroups)
        {
            var assets = group
                .Select(entry => entry.ZipPath!)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (assets.Length == 0)
            {
                results.Add(new PowerForgeToolGitHubReleaseResult
                {
                    Target = group.Key.Target,
                    Version = group.Key.Version,
                    Success = false,
                    ErrorMessage = $"No zip assets found on disk for tool target '{group.Key.Target}'."
                });
                continue;
            }

            var tagName = ApplyGitHubTemplate(tagTemplate, group.Key.Target, group.Key.Version, repository!);
            var releaseName = ApplyGitHubTemplate(releaseNameTemplate, group.Key.Target, group.Key.Version, repository!);

            try
            {
                var publishResult = _publishGitHubRelease(new GitHubReleasePublishRequest
                {
                    Owner = owner!,
                    Repository = repository!,
                    Token = token!,
                    TagName = tagName,
                    ReleaseName = releaseName,
                    GenerateReleaseNotes = gitHub.GenerateReleaseNotes,
                    IsPreRelease = gitHub.IsPreRelease,
                    ReuseExistingReleaseOnConflict = true,
                    AssetFilePaths = assets
                });

                results.Add(new PowerForgeToolGitHubReleaseResult
                {
                    Target = group.Key.Target,
                    Version = group.Key.Version,
                    TagName = tagName,
                    ReleaseName = releaseName,
                    AssetPaths = assets,
                    Success = publishResult.Succeeded,
                    ReleaseUrl = publishResult.HtmlUrl,
                    ReusedExistingRelease = publishResult.ReusedExistingRelease,
                    ErrorMessage = publishResult.Succeeded ? null : "GitHub release publish failed.",
                    SkippedExistingAssets = publishResult.SkippedExistingAssets?.ToArray() ?? Array.Empty<string>()
                });
            }
            catch (Exception ex)
            {
                results.Add(new PowerForgeToolGitHubReleaseResult
                {
                    Target = group.Key.Target,
                    Version = group.Key.Version,
                    TagName = tagName,
                    ReleaseName = releaseName,
                    AssetPaths = assets,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return results.ToArray();
    }

    private static string ApplyGitHubTemplate(string template, string target, string version, string repository)
    {
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        return template
            .Replace("{Target}", target)
            .Replace("{Project}", target)
            .Replace("{Version}", version)
            .Replace("{Repo}", repository)
            .Replace("{Repository}", repository)
            .Replace("{Date}", now.ToString("yyyy.MM.dd"))
            .Replace("{UtcDate}", utcNow.ToString("yyyy.MM.dd"))
            .Replace("{DateTime}", now.ToString("yyyyMMddHHmmss"))
            .Replace("{UtcDateTime}", utcNow.ToString("yyyyMMddHHmmss"))
            .Replace("{Timestamp}", now.ToString("yyyyMMddHHmmss"))
            .Replace("{UtcTimestamp}", utcNow.ToString("yyyyMMddHHmmss"));
    }
}
