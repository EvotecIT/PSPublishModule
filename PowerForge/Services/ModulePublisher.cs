using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace PowerForge;

/// <summary>
/// Publishes built modules to repositories or GitHub releases based on publish configuration.
/// </summary>
public sealed partial class ModulePublisher
{
    private readonly ILogger _logger;
    private readonly RepositoryPublisher _repositoryPublisher;
    private readonly PSResourceGetClient _psResourceGet;
    private readonly PowerShellGetClient _powerShellGet;
    private readonly GitHubReleasePublisher _gitHub;
    private readonly PowerShellGalleryVersionFeedClient _powerShellGalleryFeed;
    private readonly RequiredModuleRepositoryPublisher _requiredModuleRepositoryPublisher;
    private readonly RequiredModuleRepositoryValidator _requiredModuleRepositoryValidator;
    private readonly ManagedRequiredModuleRepositoryValidator _managedRequiredModuleRepositoryValidator;

    /// <summary>
    /// Creates a new publisher using the provided logger and the default out-of-process PowerShell runner.
    /// </summary>
    public ModulePublisher(ILogger logger)
        : this(logger, new PowerShellRunner(), client: null)
    {
    }

    /// <summary>
    /// Creates a new publisher using the provided logger and runner.
    /// </summary>
    public ModulePublisher(ILogger logger, IPowerShellRunner runner)
        : this(logger, runner, client: null)
    {
    }

    internal ModulePublisher(ILogger logger, IPowerShellRunner runner, HttpClient? client, IProcessRunner? processRunner = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        _repositoryPublisher = processRunner is null
            ? new RepositoryPublisher(_logger, runner)
            : new RepositoryPublisher(_logger, runner, processRunner);
        _psResourceGet = new PSResourceGetClient(runner, _logger);
        _requiredModuleRepositoryPublisher = new RequiredModuleRepositoryPublisher(_logger, _psResourceGet, _repositoryPublisher);
        _requiredModuleRepositoryValidator = new RequiredModuleRepositoryValidator(_logger, _psResourceGet, _requiredModuleRepositoryPublisher);
        _managedRequiredModuleRepositoryValidator = new ManagedRequiredModuleRepositoryValidator(_logger);
        _powerShellGet = new PowerShellGetClient(runner, _logger);
        _gitHub = new GitHubReleasePublisher(_logger);
        _powerShellGalleryFeed = new PowerShellGalleryVersionFeedClient(_logger, client);
    }

    /// <summary>
    /// Publishes based on <paramref name="publish"/> configuration.
    /// </summary>
    public ModulePublishResult Publish(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        IReadOnlyList<ArtefactBuildResult> artefactResults,
        bool includeScriptFolders = true)
    {
        if (publish is null) throw new ArgumentNullException(nameof(publish));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (artefactResults is null) throw new ArgumentNullException(nameof(artefactResults));

        if (!publish.Enabled)
        {
            return new ModulePublishResult(
                destination: publish.Destination,
                repositoryName: publish.RepositoryName,
                userName: publish.UserName,
                tagName: null,
                versionText: string.Empty,
                isPreRelease: false,
                assetPaths: Array.Empty<string>(),
                releaseUrl: null,
                succeeded: true,
                errorMessage: null);
        }

        publish = ResolveRuntimePublishSecrets(publish, plan.ProjectRoot);

        return publish.Destination switch
        {
            PublishDestination.PowerShellGallery => PublishToRepository(publish, plan, buildResult, includeScriptFolders),
            PublishDestination.GitHub => PublishToGitHub(publish, plan, artefactResults),
            _ => throw new NotSupportedException($"Unsupported publish destination: {publish.Destination}")
        };
    }

    internal void ValidateVersionForPublish(
        PublishConfiguration publish,
        ModulePipelinePlan plan)
    {
        if (publish is null)
        {
            throw new ArgumentNullException(nameof(publish));
        }
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }
        if (!publish.Enabled || publish.Force || publish.Destination != PublishDestination.PowerShellGallery)
        {
            return;
        }

        var (repositoryName, repoConfig) = ResolveRepository(publish);
        var useManagedModule = publish.Tool == PublishTool.ManagedModule ||
                               publish.Tool == PublishTool.Auto && ShouldUseManagedModuleForAuto(publish);
        if (useManagedModule)
        {
            EnsureManagedVersionIsGreaterThanRepository(
                CreateManagedReadRepository(repositoryName, repoConfig),
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease,
                ResolveManagedReadCredential(repoConfig));
            return;
        }

        if (publish.Tool == PublishTool.Auto)
        {
            try
            {
                ValidateVersionForPublishWithTool(
                    PublishTool.PSResourceGet,
                    plan,
                    repositoryName,
                    repoConfig);
            }
            catch (PowerShellToolNotAvailableException)
            {
                ValidateVersionForPublishWithTool(
                    PublishTool.PowerShellGet,
                    plan,
                    repositoryName,
                    repoConfig);
            }
            return;
        }

        ValidateVersionForPublishWithTool(
            publish.Tool,
            plan,
            repositoryName,
            repoConfig);
    }

    private void ValidateVersionForPublishWithTool(
        PublishTool tool,
        ModulePipelinePlan plan,
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig)
    {
        var repositoryCreated = false;
        var readCredential = _repositoryPublisher.ResolveCredentialForRepository(repoConfig);
        try
        {
            if (repoConfig is not null && repoConfig.EnsureRegistered && HasRepositoryUris(repoConfig))
            {
                repositoryCreated = EnsureRepositoryRegistered(tool, repositoryName, repoConfig);
            }

            EnsureVersionIsGreaterThanRepository(
                tool,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease,
                repositoryName,
                readCredential);
        }
        finally
        {
            if (repositoryCreated && repoConfig is { UnregisterAfterUse: true })
            {
                try
                {
                    UnregisterRepository(tool, repositoryName);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to unregister repository '{repositoryName}' after version preflight: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Resolves an inline or file-backed publish API key immediately before a publish operation.
    /// </summary>
    /// <param name="publish">Publish configuration containing the key or deferred key path.</param>
    /// <param name="basePath">Optional project root used to resolve relative key paths.</param>
    /// <returns>The resolved single-line key, or an empty string when no key source is configured.</returns>
    public static string ResolvePublishApiKey(PublishConfiguration publish, string? basePath = null)
    {
        if (publish is null) throw new ArgumentNullException(nameof(publish));

        if (!string.IsNullOrWhiteSpace(publish.ApiKey))
            return ValidateSingleLineSecret(publish.ApiKey, nameof(PublishConfiguration.ApiKey));

        if (!string.IsNullOrWhiteSpace(publish.ApiKeyFilePath))
            return ReadSingleLineSecretFile(
                ResolvePublishApiKeyFilePath(publish.ApiKeyFilePath!, basePath),
                nameof(PublishConfiguration.ApiKeyFilePath));

        return string.Empty;
    }

    private static PublishConfiguration ResolveRuntimePublishSecrets(PublishConfiguration publish, string? basePath)
    {
        var apiKey = ResolvePublishApiKey(publish, basePath);
        if (string.Equals(apiKey, publish.ApiKey, StringComparison.Ordinal))
            return publish;

        return new PublishConfiguration
        {
            Destination = publish.Destination,
            Tool = publish.Tool,
            ApiKey = apiKey,
            ApiKeyFilePath = publish.ApiKeyFilePath,
            ID = publish.ID,
            Enabled = publish.Enabled,
            UserName = publish.UserName,
            RepositoryName = publish.RepositoryName,
            Repository = publish.Repository,
            Force = publish.Force,
            OverwriteTagName = publish.OverwriteTagName,
            DoNotMarkAsPreRelease = publish.DoNotMarkAsPreRelease,
            GenerateReleaseNotes = publish.GenerateReleaseNotes,
            UseAsDependencyVersionSource = publish.UseAsDependencyVersionSource,
            PublishRequiredModules = publish.PublishRequiredModules,
            RequiredModuleSourceRepository = publish.RequiredModuleSourceRepository,
            RequiredModuleSourceRepositoryUri = publish.RequiredModuleSourceRepositoryUri,
            Verbose = publish.Verbose
        };
    }

    private static string ResolvePublishApiKeyFilePath(string filePath, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return Path.GetFullPath(PathValueResolver.Clean(filePath));

        return PathValueResolver.Resolve(basePath!, filePath);
    }

    private static string ReadSingleLineSecretFile(string filePath, string parameterName)
    {
        var path = filePath.Trim();
        var value = File.ReadAllText(path).Trim();
        return ValidateSingleLineSecret(value, parameterName, path);
    }

    private static string ValidateSingleLineSecret(string value, string parameterName, string? path = null)
    {
        if (value.Contains('\r') || value.Contains('\n'))
        {
            var location = string.IsNullOrWhiteSpace(path)
                ? parameterName
                : $"{parameterName} '{path}'";
            throw new ArgumentException(
                $"{location} resolved to a multi-line secret. Publish API keys and repository credential secrets must be a single line; check that the path points to a secret file, not a script or configuration file.",
                parameterName);
        }

        return value.Trim();
    }

    private ModulePublishResult PublishToRepository(PublishConfiguration publish, ModulePipelinePlan plan, ModuleBuildResult buildResult, bool includeScriptFolders)
    {
        var (repositoryName, repoConfig) = ResolveRepository(publish);
        var isPsGallery = string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase);

        var credential = repoConfig?.Credential;
        var hasCredential = credential is not null &&
                            !string.IsNullOrWhiteSpace(credential.UserName) &&
                            !string.IsNullOrWhiteSpace(credential.Secret);
        var hasRuntimeCredentialProvider = repoConfig?.CredentialProvider is { Kind: not RepositoryCredentialProviderKind.None };

        if (isPsGallery && string.IsNullOrWhiteSpace(publish.ApiKey))
            throw new InvalidOperationException("Publish API key is required for repository publishing to PSGallery.");

        var useManagedModule = publish.Tool == PublishTool.ManagedModule ||
                               publish.Tool == PublishTool.Auto && ShouldUseManagedModuleForAuto(publish);
        var managedRepository = useManagedModule
            ? CreateManagedPublishRepository(repositoryName, repoConfig)
            : null;
        var managedLocalFolder = managedRepository?.Kind == ManagedModuleRepositoryKind.LocalFolder;

        if (!isPsGallery && !managedLocalFolder && string.IsNullOrWhiteSpace(publish.ApiKey) && !hasCredential && !hasRuntimeCredentialProvider)
            throw new InvalidOperationException("Publish API key or credential is required for repository publishing.");

        var tool = publish.Tool;
        if (tool == PublishTool.Auto)
        {
            if (useManagedModule)
            {
                return PublishToRepositoryWithTool(
                    PublishTool.ManagedModule,
                    publish,
                    plan,
                    buildResult,
                    repositoryName,
                    repoConfig,
                    includeScriptFolders);
            }

            try
            {
                return PublishToRepositoryWithTool(PublishTool.PSResourceGet, publish, plan, buildResult, repositoryName, repoConfig, includeScriptFolders);
            }
            catch (PowerShellToolNotAvailableException)
            {
                return PublishToRepositoryWithTool(PublishTool.PowerShellGet, publish, plan, buildResult, repositoryName, repoConfig, includeScriptFolders);
            }
        }

        return PublishToRepositoryWithTool(tool, publish, plan, buildResult, repositoryName, repoConfig, includeScriptFolders);
    }

    private ModulePublishResult PublishToRepositoryWithTool(
        PublishTool tool,
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig,
        bool includeScriptFolders)
    {
        if (publish.PublishRequiredModules && tool == PublishTool.PowerShellGet)
        {
            throw new InvalidOperationException(
                "PublishRequiredModules requires PSResourceGet because dependency mirroring saves and republishes dependency graphs before publishing the main module. Use Tool = PSResourceGet or disable PublishRequiredModules.");
        }

        var readCredential = tool == PublishTool.ManagedModule
            ? ResolveManagedReadCredential(repoConfig)
            : _repositoryPublisher.ResolveCredentialForRepository(repoConfig);
        var publishCredential = tool == PublishTool.ManagedModule
            ? ResolveManagedPublishCredential(publish, repoConfig)
            : readCredential;
        string? temporaryPublishPath = null;
        string? temporaryPackagePath = null;
        var repositoryCreated = false;
        PublishRepositoryConfiguration? repositoryForPublish = repoConfig is null
            ? null
            : CloneRepositoryForPublish(repoConfig, publishCredential);
        var versionText = ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);

        try
        {
            temporaryPublishPath = PrepareModulePackageForRepositoryPublish(
                stagingPath: buildResult.StagingPath,
                moduleName: plan.ModuleName,
                information: plan.Information,
                delivery: plan.Delivery,
                includeScriptFolders: includeScriptFolders);

            if (tool != PublishTool.ManagedModule && repoConfig is not null && repoConfig.EnsureRegistered && HasRepositoryUris(repoConfig))
            {
                repositoryCreated = EnsureRepositoryRegistered(tool, repositoryName, repoConfig);
                repositoryForPublish = CloneRegisteredRepository(repoConfig, publishCredential);
            }

            if (!publish.Force)
            {
                if (tool == PublishTool.ManagedModule)
                {
                    EnsureManagedVersionIsGreaterThanRepository(
                        CreateManagedReadRepository(repositoryName, repoConfig),
                        plan.ModuleName,
                        plan.ResolvedVersion,
                        plan.PreRelease,
                        readCredential);
                }
                else
                {
                    EnsureVersionIsGreaterThanRepository(tool, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease, repositoryName, readCredential);
                }
            }

            _logger.Info($"Publishing {plan.ModuleName} {versionText} to repository '{repositoryName}' using {tool}");

            var modulePath = Path.GetFullPath(temporaryPublishPath);

            if (tool == PublishTool.ManagedModule)
            {
                _managedRequiredModuleRepositoryValidator.Validate(
                    publish,
                    CreateManagedReadRepository(repositoryName, repoConfig),
                    readCredential,
                    publishCredential,
                    plan,
                    buildResult);

                temporaryPackagePath = Path.Combine(Path.GetTempPath(), "PowerForge", "managed-publish", Guid.NewGuid().ToString("N"));
                PublishToRepositoryWithManagedModule(
                    publish,
                    plan,
                    modulePath,
                    repositoryName,
                    repoConfig,
                    readCredential,
                    publishCredential,
                    versionText,
                    temporaryPackagePath,
                    skipDependenciesCheck: true);
                CleanupTemporaryPublishPath(temporaryPublishPath);
                temporaryPublishPath = null;
                return CreateRepositoryPublishResult(repositoryName, versionText, tool);
            }

            if (tool != PublishTool.PowerShellGet)
            {
                _requiredModuleRepositoryValidator.Validate(
                    publish,
                    repositoryName,
                    readCredential,
                    repositoryForPublish,
                    plan,
                    buildResult);
            }

            _repositoryPublisher.Publish(
                new RepositoryPublishRequest
                {
                    Path = modulePath,
                    IsNupkg = false,
                    RepositoryName = repositoryName,
                    Tool = tool,
                    ApiKey = string.IsNullOrWhiteSpace(publish.ApiKey) ? null : publish.ApiKey,
                    Repository = repositoryForPublish,
                    DestinationPath = null,
                    SkipDependenciesCheck = tool != PublishTool.PowerShellGet,
                    SkipModuleManifestValidate = false
                });

            _logger.Info($"Published {plan.ModuleName} {versionText} to repository '{repositoryName}' using {tool}.");

            CleanupTemporaryPublishPath(temporaryPublishPath);
            temporaryPublishPath = null;
        }
        finally
        {
            if (repositoryCreated && repoConfig is { UnregisterAfterUse: true })
            {
                try
                {
                    UnregisterRepository(tool, repositoryName);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to unregister repository '{repositoryName}': {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(temporaryPublishPath))
                CleanupTemporaryPublishPath(temporaryPublishPath);
            if (!string.IsNullOrWhiteSpace(temporaryPackagePath))
                CleanupTemporaryPublishPath(temporaryPackagePath);
        }

        return CreateRepositoryPublishResult(repositoryName, versionText, tool);
    }

    private static ModulePublishResult CreateRepositoryPublishResult(string repositoryName, string versionText, PublishTool tool)
    {
        return new ModulePublishResult(
            destination: PublishDestination.PowerShellGallery,
            repositoryName: repositoryName,
            userName: null,
            tagName: null,
            versionText: versionText,
            isPreRelease: false,
            assetPaths: Array.Empty<string>(),
            releaseUrl: null,
            succeeded: true,
            errorMessage: null,
            tool: tool);
    }

    internal static string PrepareModulePackageForRepositoryPublish(
        string stagingPath,
        string moduleName,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders)
    {
        if (string.IsNullOrWhiteSpace(stagingPath))
            throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("ModuleName is required.", nameof(moduleName));

        var source = Path.GetFullPath(stagingPath);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Staging directory not found: {source}");

        var publishPath = Path.Combine(Path.GetTempPath(), "PowerForge", "publish", $"{moduleName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(publishPath);

        ArtefactBuilder.CopyModulePackageForInstall(
            stagingRoot: source,
            destinationModuleRoot: publishPath,
            information: information,
            delivery: delivery,
            includeScriptFolders: includeScriptFolders);

        return publishPath;
    }

    internal static void CleanupTemporaryPublishPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    internal static RequiredModuleReference[] GetRequiredModulesForPublish(
        ModuleBuildResult buildResult,
        ModulePipelinePlan plan)
    {
        if (!string.IsNullOrWhiteSpace(buildResult.ManifestPath) &&
            File.Exists(buildResult.ManifestPath))
        {
            var manifestModules = ModuleManifestValueReader.ReadRequiredModules(buildResult.ManifestPath);
            return manifestModules
                .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.ModuleName))
                .ToArray()!;
        }

        var planned = plan.RequiredModules ?? Array.Empty<RequiredModuleReference>();
        if (planned.Length == 0)
            return Array.Empty<RequiredModuleReference>();

        return planned
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.ModuleName))
            .ToArray()!;
    }

    internal static bool HasMatchingRequiredModuleVersion(
        RequiredModuleReference requiredModule,
        IReadOnlyList<string> repositoryVersions)
    {
        if (requiredModule is null || string.IsNullOrWhiteSpace(requiredModule.ModuleName))
            return false;

        if (repositoryVersions is null || repositoryVersions.Count == 0)
            return false;

        foreach (var candidateVersion in repositoryVersions)
        {
            if (DoesVersionMatchRequiredModule(requiredModule, candidateVersion))
                return true;
        }

        return false;
    }

    internal static bool ShouldSkipRepositoryDependencyValidation(
        RequiredModuleReference requiredModule,
        ISet<string> externalModuleDependencies)
    {
        if (requiredModule is null || string.IsNullOrWhiteSpace(requiredModule.ModuleName))
            return false;

        if (externalModuleDependencies is null || externalModuleDependencies.Count == 0)
            return false;

        return externalModuleDependencies.Contains(requiredModule.ModuleName.Trim());
    }

    internal static bool DoesVersionMatchRequiredModule(RequiredModuleReference requiredModule, string candidateVersion)
    {
        if (requiredModule is null || string.IsNullOrWhiteSpace(requiredModule.ModuleName))
            return false;
        if (string.IsNullOrWhiteSpace(candidateVersion))
            return false;

        var hasConstraints =
            !string.IsNullOrWhiteSpace(requiredModule.ModuleVersion) ||
            !string.IsNullOrWhiteSpace(requiredModule.RequiredVersion) ||
            !string.IsNullOrWhiteSpace(requiredModule.MaximumVersion);

        if (!TryParseSemVer(candidateVersion, out var candidate))
        {
            if (!hasConstraints)
                return true;

            if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
                return string.Equals(candidateVersion.Trim(), requiredModule.RequiredVersion!.Trim(), StringComparison.OrdinalIgnoreCase);

            return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
        {
            var requiredVersion = requiredModule.RequiredVersion!.Trim();
            if (!TryParseSemVer(requiredVersion, out var required))
                return string.Equals(candidateVersion.Trim(), requiredVersion, StringComparison.OrdinalIgnoreCase);

            return candidate.CompareTo(required) == 0;
        }

        if (!string.IsNullOrWhiteSpace(requiredModule.ModuleVersion))
        {
            if (!TryParseSemVer(requiredModule.ModuleVersion!, out var minimum))
                return false;

            if (candidate.CompareTo(minimum) < 0)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredModule.MaximumVersion))
        {
            if (!TryParseSemVer(requiredModule.MaximumVersion!, out var maximum))
                return false;

            if (candidate.CompareTo(maximum) > 0)
                return false;
        }

        return true;
    }

    internal static string FormatRequiredModuleConstraint(RequiredModuleReference requiredModule)
    {
        if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
            return $"RequiredVersion = {requiredModule.RequiredVersion}";
        if (!string.IsNullOrWhiteSpace(requiredModule.ModuleVersion) && !string.IsNullOrWhiteSpace(requiredModule.MaximumVersion))
            return $"ModuleVersion >= {requiredModule.ModuleVersion}, MaximumVersion <= {requiredModule.MaximumVersion}";
        if (!string.IsNullOrWhiteSpace(requiredModule.ModuleVersion))
            return $"ModuleVersion >= {requiredModule.ModuleVersion}";
        if (!string.IsNullOrWhiteSpace(requiredModule.MaximumVersion))
            return $"MaximumVersion <= {requiredModule.MaximumVersion}";
        return "Any version";
    }

    internal void EnsureVersionIsGreaterThanRepository(
        PublishTool tool,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        string repositoryName,
        RepositoryCredential? credential)
    {
        var publishVersionText = FormatSemVer(moduleVersion, preRelease);
        if (!TryParseSemVer(publishVersionText, out var publishVersion))
            throw new InvalidOperationException($"Could not parse module version for publish: '{publishVersionText}'.");

        SemVer? current = null;
        try
        {
            current = TryGetLatestRepositoryVersion(tool, moduleName, repositoryName, credential);
        }
        catch (Exception ex) when (IsRepositoryPackageNotFound(moduleName, ex))
        {
            _logger.Verbose($"No existing repository version was found for {moduleName} on '{repositoryName}'. Treating this as a first publish.");
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to query repository version for {moduleName} on '{repositoryName}'. Use -Force to publish without version check. {ex.Message}");
        }

        if (current is null) return;

        if (publishVersion.CompareTo(current.Value) <= 0)
            throw new InvalidOperationException($"Module version '{publishVersionText}' is not greater than repository version '{FormatSemVer(current.Value.Version.ToString(), current.Value.PreRelease)}' for '{moduleName}'. Use -Force to publish anyway.");
    }

    internal static bool IsRepositoryPackageNotFound(string moduleName, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || exception is null)
            return false;

        if (exception is ManagedModuleRepositoryException managedException &&
            managedException.Operation.Equals("VersionQuery", StringComparison.OrdinalIgnoreCase) &&
            managedException.StatusCode == 404)
        {
            return true;
        }

        if (exception is ManagedModuleRepositoryException localRepositoryException &&
            localRepositoryException.Operation.Equals("VersionQuery", StringComparison.OrdinalIgnoreCase) &&
            localRepositoryException.StatusCode is null &&
            localRepositoryException.Message.IndexOf("Local repository folder was not found", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        var message = exception.ToString();
        return message.IndexOf(moduleName, StringComparison.OrdinalIgnoreCase) >= 0 &&
               (message.IndexOf("could not be found in repository", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("no match was found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("no packages found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("no results", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private SemVer? TryGetLatestRepositoryVersion(PublishTool tool, string moduleName, string repositoryName, RepositoryCredential? credential)
    {
        SemVer? latest = null;

        if (string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var version in _powerShellGalleryFeed.GetVersions(moduleName, includePrerelease: true, timeout: TimeSpan.FromMinutes(2)))
                {
                    if (!TryParseSemVer(version.VersionText, out var parsed))
                        continue;

                    if (latest is null || parsed.CompareTo(latest.Value) > 0)
                        latest = parsed;
                }

                if (latest is not null)
                    return latest;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to query the raw PowerShell Gallery feed for '{moduleName}'. Falling back to {tool}. {ex.Message}");
            }
        }

        var versions = tool == PublishTool.PowerShellGet
            ? _powerShellGet.Find(
                    new PowerShellGetFindOptions(
                        names: new[] { moduleName },
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(GetRepositoryVersionText)
            : _psResourceGet.Find(
                    new PSResourceFindOptions(
                        names: new[] { moduleName },
                        version: null,
                        prerelease: true,
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .Select(GetRepositoryVersionText);

        foreach (var v in versions)
        {
            if (!TryParseSemVer(v, out var parsed)) continue;
            if (latest is null || parsed.CompareTo(latest.Value) > 0) latest = parsed;
        }
        return latest;
    }

    internal static string GetRepositoryVersionText(PSResourceInfo resource)
    {
        if (resource is null)
            throw new ArgumentNullException(nameof(resource));

        return FormatSemVer(resource.Version, resource.PreRelease);
    }

    private static bool HasRepositoryUris(PublishRepositoryConfiguration repo)
        => repo is not null &&
           (!string.IsNullOrWhiteSpace(repo.Uri) ||
            !string.IsNullOrWhiteSpace(repo.SourceUri) ||
            !string.IsNullOrWhiteSpace(repo.PublishUri));

    private bool EnsureRepositoryRegistered(PublishTool tool, string repositoryName, PublishRepositoryConfiguration repo)
    {
        if (tool == PublishTool.PowerShellGet)
        {
            var sourceUri = string.IsNullOrWhiteSpace(repo.SourceUri)
                ? (string.IsNullOrWhiteSpace(repo.Uri) ? repo.PublishUri : repo.Uri)
                : repo.SourceUri;
            var publishUri = string.IsNullOrWhiteSpace(repo.PublishUri)
                ? (string.IsNullOrWhiteSpace(repo.Uri) ? repo.SourceUri : repo.Uri)
                : repo.PublishUri;

            if (string.IsNullOrWhiteSpace(sourceUri) || string.IsNullOrWhiteSpace(publishUri))
            {
                throw new InvalidOperationException(
                    $"Repository '{repositoryName}' is missing SourceUri/PublishUri/Uri for PowerShellGet registration.");
            }

            return _powerShellGet.EnsureRepositoryRegistered(
                repositoryName,
                sourceUri!,
                publishUri!,
                trusted: repo.Trusted,
                timeout: TimeSpan.FromMinutes(2));
        }

        var uri = string.IsNullOrWhiteSpace(repo.Uri)
            ? (string.IsNullOrWhiteSpace(repo.PublishUri) ? repo.SourceUri : repo.PublishUri)
            : repo.Uri;

        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException($"Repository '{repositoryName}' is missing Uri/PublishUri/SourceUri for PSResourceGet registration.");

        return _psResourceGet.EnsureRepositoryRegistered(
            name: repositoryName,
            uri: uri!,
            trusted: repo.Trusted,
            priority: repo.Priority,
            apiVersion: repo.ApiVersion,
            timeout: TimeSpan.FromMinutes(2));
    }

    private void UnregisterRepository(PublishTool tool, string repositoryName)
    {
        if (tool == PublishTool.PowerShellGet)
        {
            _powerShellGet.UnregisterRepository(repositoryName, timeout: TimeSpan.FromMinutes(2));
            return;
        }

        _psResourceGet.UnregisterRepository(repositoryName, timeout: TimeSpan.FromMinutes(2));
    }

    private static PublishRepositoryConfiguration CloneRegisteredRepository(PublishRepositoryConfiguration repo, RepositoryCredential? credential)
        => CloneRepositoryForPublish(repo, credential, ensureRegistered: false, unregisterAfterUse: false);

    private static PublishRepositoryConfiguration CloneRepositoryForPublish(
        PublishRepositoryConfiguration repo,
        RepositoryCredential? credential,
        bool ensureRegistered = false,
        bool unregisterAfterUse = false)
        => new()
        {
            Name = repo.Name,
            Uri = repo.Uri,
            SourceUri = repo.SourceUri,
            PublishUri = repo.PublishUri,
            Trusted = repo.Trusted,
            Priority = repo.Priority,
            ApiVersion = repo.ApiVersion,
            EnsureRegistered = ensureRegistered,
            UnregisterAfterUse = unregisterAfterUse,
            Credential = credential,
            CredentialProvider = credential is null ? CloneCredentialProvider(repo.CredentialProvider) : null
        };

    private static RepositoryCredentialProviderConfiguration? CloneCredentialProvider(RepositoryCredentialProviderConfiguration? provider)
        => provider is null
            ? null
            : new RepositoryCredentialProviderConfiguration
            {
                Kind = provider.Kind,
                UserName = provider.UserName,
                JFrogPlatformUri = provider.JFrogPlatformUri,
                JFrogOidcProvider = provider.JFrogOidcProvider,
                JFrogOidcTokenId = provider.JFrogOidcTokenId,
                JFrogOidcTokenIdEnvironmentVariable = provider.JFrogOidcTokenIdEnvironmentVariable,
                JFrogOidcProviderType = provider.JFrogOidcProviderType
            };

    private static (string RepositoryName, PublishRepositoryConfiguration? Repository) ResolveRepository(PublishConfiguration publish)
    {
        var repoConfig = publish.Repository;

        var name = repoConfig is not null && !string.IsNullOrWhiteSpace(repoConfig.Name)
            ? repoConfig.Name!.Trim()
            : (string.IsNullOrWhiteSpace(publish.RepositoryName) ? "PSGallery" : publish.RepositoryName!.Trim());

        return (name, repoConfig);
    }

    private ModulePublishResult PublishToGitHub(PublishConfiguration publish, ModulePipelinePlan plan, IReadOnlyList<ArtefactBuildResult> artefactResults)
    {
        if (string.IsNullOrWhiteSpace(publish.UserName))
            throw new InvalidOperationException("UserName is required for GitHub publishing.");
        if (string.IsNullOrWhiteSpace(publish.ApiKey))
            throw new InvalidOperationException("API key (token) is required for GitHub publishing.");

        var owner = publish.UserName!.Trim();
        var repo = string.IsNullOrWhiteSpace(publish.RepositoryName) ? plan.ModuleName : publish.RepositoryName!.Trim();
        var versionText = ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);
        var tag = GetGitHubTag(publish, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease);

        var isPreRelease = !string.IsNullOrWhiteSpace(plan.PreRelease) && !publish.DoNotMarkAsPreRelease;

        var selected = SelectPackedArtefacts(artefactResults, publish.ID);
        var assets = selected.Select(a => a.OutputPath).ToArray();

        _logger.Info($"Publishing GitHub release {owner}/{repo} tag '{tag}' with {assets.Length} asset(s)");
        var created = _gitHub.PublishRelease(
            owner: owner,
            repo: repo,
            token: publish.ApiKey,
            tagName: tag,
            releaseName: tag,
            releaseNotes: null,
            commitish: null,
            generateReleaseNotes: publish.GenerateReleaseNotes,
            isDraft: false,
            isPreRelease: isPreRelease,
            assetFilePaths: assets);

        var releaseUrl = string.IsNullOrWhiteSpace(created.HtmlUrl)
            ? $"https://github.com/{owner}/{repo}/releases/tag/{tag}"
            : created.HtmlUrl;

        _logger.Info($"Published GitHub release {owner}/{repo} tag '{tag}' ({versionText}) => {releaseUrl}");

        return new ModulePublishResult(
            destination: PublishDestination.GitHub,
            repositoryName: repo,
            userName: owner,
            tagName: tag,
            versionText: versionText,
            isPreRelease: isPreRelease,
            assetPaths: assets,
            releaseUrl: releaseUrl,
            succeeded: true,
            errorMessage: null);
    }

    internal static string GetGitHubTag(PublishConfiguration publish, string moduleName, string resolvedVersion, string? preRelease)
    {
        if (publish is null)
            throw new ArgumentNullException(nameof(publish));

        if (string.IsNullOrWhiteSpace(publish.OverwriteTagName))
            return "v" + ModulePathTokenFormatter.FormatVersionWithPreRelease(resolvedVersion, preRelease);

        return ModulePathTokenFormatter.ReplacePathTokens(publish.OverwriteTagName!, moduleName, resolvedVersion, preRelease);
    }

    private static ArtefactBuildResult[] SelectPackedArtefacts(IReadOnlyList<ArtefactBuildResult> artefactResults, string? id)
    {
        var packed = (artefactResults ?? Array.Empty<ArtefactBuildResult>())
            .Where(a => a is not null && (a.Type == ArtefactType.Packed || a.Type == ArtefactType.ScriptPacked))
            .ToArray();

        if (packed.Length == 0)
            throw new InvalidOperationException("No packed artefacts were produced; cannot publish.");

        if (string.IsNullOrWhiteSpace(id))
            return new[] { packed[0] };

        var idValue = id!.Trim();
        var selected = packed.Where(a => string.Equals(a.Id, idValue, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0)
        {
            var available = packed.Select(a => a.Id).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var availText = available.Length == 0 ? "(none)" : string.Join(", ", available);
            throw new InvalidOperationException($"No packed artefacts matched ID '{id}'. Available IDs: {availText}");
        }
        return selected;
    }

    private readonly struct SemVer : IComparable<SemVer>
    {
        public Version Version { get; }
        public string? PreRelease { get; }

        public SemVer(Version version, string? preRelease)
        {
            Version = version;
            PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
        }

        public int CompareTo(SemVer other)
        {
            var c = Version.CompareTo(other.Version);
            if (c != 0) return c;

            var a = PreRelease;
            var b = other.PreRelease;
            if (a is null && b is null) return 0;
            if (a is null) return 1; // stable > prerelease
            if (b is null) return -1;

            return ComparePreRelease(a, b);
        }

        private static int ComparePreRelease(string a, string b)
        {
            var aa = a.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var bb = b.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var len = Math.Max(aa.Length, bb.Length);
            for (var i = 0; i < len; i++)
            {
                if (i >= aa.Length) return -1;
                if (i >= bb.Length) return 1;

                var pa = aa[i];
                var pb = bb[i];

                var na = int.TryParse(pa, out var ia);
                var nb = int.TryParse(pb, out var ib);
                if (na && nb)
                {
                    var c = ia.CompareTo(ib);
                    if (c != 0) return c;
                }
                else
                {
                    var c = StringComparer.OrdinalIgnoreCase.Compare(pa, pb);
                    if (c != 0) return c;
                }
            }
            return 0;
        }
    }

    private static bool TryParseSemVer(string text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim();
        var dash = t.IndexOf('-');
        var main = dash >= 0 ? t.Substring(0, dash) : t;
        var pre = dash >= 0 ? t.Substring(dash + 1) : null;
        if (!Version.TryParse(main, out var v)) return false;
        version = new SemVer(v, pre);
        return true;
    }

    private static string FormatSemVer(string moduleVersion, string? preRelease)
        => string.IsNullOrWhiteSpace(preRelease) ? moduleVersion : moduleVersion + "-" + preRelease;
}
