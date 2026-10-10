namespace PowerForge;

/// <content>Repository publishing workflow and dependency validation.</content>
public sealed partial class ModulePublisher
{
    private ModulePublishResult PublishToRepository(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        bool includeScriptFolders,
        Action? remotePublishAttempted,
        Action? remoteSideEffectObserved,
        CancellationToken cancellationToken,
        Action<string, string>? finalizeRepositoryModule)
    {
        ModulePublishDependencyPolicy.Validate(publish, publish.Tool);
        var (repositoryName, repoConfig) = ResolveRepository(publish);
        repoConfig = NormalizeRepositoryPaths(repoConfig, plan.ProjectRoot);
        var isPsGallery = string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase);

        var credential = repoConfig?.Credential;
        var hasCredential = credential is not null &&
                            !string.IsNullOrWhiteSpace(credential.UserName) &&
                            !string.IsNullOrWhiteSpace(credential.Secret);
        var hasRuntimeCredentialProvider = repoConfig?.CredentialProvider is { Kind: not RepositoryCredentialProviderKind.None };

        if (isPsGallery && string.IsNullOrWhiteSpace(publish.ApiKey))
            throw new InvalidOperationException("Publish API key is required for repository publishing to PSGallery.");

        var useManagedModule = publish.Tool == PublishTool.ManagedModule ||
                               publish.Tool == PublishTool.Auto && ShouldUseManagedModuleForAuto(publish, plan.ProjectRoot);
        var managedRepository = useManagedModule
            ? CreateManagedPublishRepository(repositoryName, repoConfig, plan.ProjectRoot)
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
                    includeScriptFolders,
                    remotePublishAttempted,
                    remoteSideEffectObserved,
                    cancellationToken,
                    finalizeRepositoryModule);
            }

            try
            {
                return PublishToRepositoryWithTool(PublishTool.PSResourceGet, publish, plan, buildResult, repositoryName, repoConfig, includeScriptFolders, remotePublishAttempted, remoteSideEffectObserved, cancellationToken, finalizeRepositoryModule);
            }
            catch (PowerShellToolNotAvailableException)
            {
                return PublishToRepositoryWithTool(PublishTool.PowerShellGet, publish, plan, buildResult, repositoryName, repoConfig, includeScriptFolders, remotePublishAttempted, remoteSideEffectObserved, cancellationToken, finalizeRepositoryModule);
            }
        }

        return PublishToRepositoryWithTool(tool, publish, plan, buildResult, repositoryName, repoConfig, includeScriptFolders, remotePublishAttempted, remoteSideEffectObserved, cancellationToken, finalizeRepositoryModule);
    }

    private ModulePublishResult PublishToRepositoryWithTool(
        PublishTool tool,
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig,
        bool includeScriptFolders,
        Action? remotePublishAttempted,
        Action? remoteSideEffectObserved,
        CancellationToken cancellationToken,
        Action<string, string>? finalizeRepositoryModule)
    {
        ModulePublishDependencyPolicy.Validate(publish, tool);
        var readCredential = tool == PublishTool.ManagedModule
            ? ResolveManagedReadCredential(repoConfig)
            : _repositoryPublisher.ResolveCredentialForRepository(repoConfig);
        var publishCredential = tool == PublishTool.ManagedModule
            ? ResolveManagedPublishCredential(publish, repoConfig)
            : readCredential;
        string? temporaryPublishPath = null;
        string? temporaryPackagePath = null;
        var repositoryCreated = false;
        var temporaryRepository = false;
        var uploadRepositoryName = repositoryName;
        PublishRepositoryConfiguration? repositoryForPublish = repoConfig is null
            ? null
            : CloneRepositoryForPublish(repoConfig, publishCredential);
        var versionText = ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            temporaryPublishPath = PrepareModulePackageForRepositoryPublish(
                stagingPath: buildResult.StagingPath,
                moduleName: plan.ModuleName,
                information: plan.Information,
                delivery: plan.Delivery,
                includeScriptFolders: includeScriptFolders,
                finalizedPayloadFiles: buildResult.FinalizedPayloadFiles);
            finalizeRepositoryModule?.Invoke(temporaryPublishPath, plan.ModuleName);

            if (tool != PublishTool.ManagedModule && repoConfig is not null && repoConfig.EnsureRegistered && HasRepositoryUris(repoConfig))
            {
                if (tool == PublishTool.PSResourceGet && ModulePublishDependencyPolicy.HasSeparateEndpoints(repoConfig))
                {
                    (uploadRepositoryName, repositoryCreated) = _psResourceGet.AcquirePublishRepository(
                        ModulePublishDependencyPolicy.PublishUri(repoConfig)!, repoConfig.Trusted, repoConfig.Priority, repoConfig.ApiVersion);
                    temporaryRepository = repositoryCreated;
                }
                else
                {
                    repositoryCreated = EnsureRepositoryRegistered(tool, repositoryName, repoConfig);
                }
                repositoryForPublish = CloneRegisteredRepository(repoConfig, publishCredential);
                repositoryForPublish.Name = uploadRepositoryName;
            }

            if (!publish.Force)
            {
                if (tool == PublishTool.ManagedModule || ModulePublishDependencyPolicy.HasSeparateEndpoints(repoConfig))
                {
                    EnsureManagedVersionIsGreaterThanRepository(
                        CreateManagedReadRepository(repositoryName, repoConfig, plan.ProjectRoot),
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
                    CreateManagedReadRepository(repositoryName, repoConfig, plan.ProjectRoot),
                    readCredential,
                    publishCredential,
                    plan,
                    buildResult,
                    remoteSideEffectObserved,
                    publishRepository: CreateManagedPublishRepository(repositoryName, repoConfig, plan.ProjectRoot),
                    cancellationToken: cancellationToken);

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
                    skipDependenciesCheck: true,
                    remotePublishAttempted: remotePublishAttempted);
                CleanupTemporaryPublishPath(temporaryPublishPath);
                temporaryPublishPath = null;
                return CreateRepositoryPublishResult(repositoryName, versionText, tool);
            }

            if (tool != PublishTool.PowerShellGet)
            {
                if (ModulePublishDependencyPolicy.HasSeparateEndpoints(repoConfig))
                {
                    _managedRequiredModuleRepositoryValidator.Validate(
                        publish,
                        CreateManagedReadRepository(repositoryName, repoConfig, plan.ProjectRoot),
                        readCredential,
                        publishCredential,
                        plan,
                        buildResult,
                        remoteSideEffectObserved,
                        publishRepository: CreateManagedPublishRepository(repositoryName, repoConfig, plan.ProjectRoot),
                        cancellationToken: cancellationToken,
                        mirrorRequiredModule: ManagedRequiredModuleRepositoryValidator.CanResolveSourceRepository(publish, repositoryName)
                            ? null
                            : _requiredModuleRepositoryValidator.CreateMirroringCallback(
                                publish, uploadRepositoryName, readCredential, repositoryForPublish, remoteSideEffectObserved));
                }
                else
                {
                    _requiredModuleRepositoryValidator.Validate(
                        publish,
                        repositoryName,
                        readCredential,
                        repositoryForPublish,
                        plan,
                        buildResult,
                        remoteSideEffectObserved);
                }
            }

            _repositoryPublisher.Publish(
                new RepositoryPublishRequest
                {
                    Path = modulePath,
                    IsNupkg = false,
                    RepositoryName = uploadRepositoryName,
                    Tool = tool,
                    ApiKey = string.IsNullOrWhiteSpace(publish.ApiKey) ? null : publish.ApiKey,
                    Repository = repositoryForPublish,
                    DestinationPath = null,
                    SkipDependenciesCheck = tool != PublishTool.PowerShellGet,
                    SkipModuleManifestValidate = false,
                    RemotePublishAttempted = remotePublishAttempted,
                    CancellationToken = cancellationToken
                });

            _logger.Info($"Published {plan.ModuleName} {versionText} to repository '{repositoryName}' using {tool}.");

            CleanupTemporaryPublishPath(temporaryPublishPath);
            temporaryPublishPath = null;
        }
        finally
        {
            if (repositoryCreated && (temporaryRepository || repoConfig is { UnregisterAfterUse: true }))
            {
                try
                {
                    UnregisterRepository(tool, uploadRepositoryName);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to unregister repository '{uploadRepositoryName}': {ex.Message}");
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

}
