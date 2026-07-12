namespace PowerForge;

/// <content>
/// Managed C# repository publish integration.
/// </content>
public sealed partial class ModulePublisher
{
    internal static bool ShouldUseManagedModuleForAuto(PublishConfiguration publish)
    {
        if (publish is null)
            throw new ArgumentNullException(nameof(publish));
        if (publish.Tool == PublishTool.ManagedModule)
            return true;
        if (publish.Tool != PublishTool.Auto)
            return false;
        if (publish.Repository?.CredentialProvider is { Kind: not RepositoryCredentialProviderKind.None })
            return false;

        var (repositoryName, repository) = ResolveRepository(publish);
        if (!ManagedRequiredModuleRepositoryValidator.CanResolveSourceRepository(publish, repositoryName))
            return false;
        try
        {
            var support = ManagedModuleProviderSupportEvaluator.Evaluate(
                CreateManagedPublishRepository(repositoryName, repository));
            return support.Level == ManagedModuleProviderSupportLevel.Supported;
        }
        catch (InvalidOperationException)
        {
            // Named repositories without an explicit URI still require the compatibility registration path.
            return false;
        }
    }

    private void PublishToRepositoryWithManagedModule(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        string modulePath,
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig,
        RepositoryCredential? readCredential,
        RepositoryCredential? publishCredential,
        string versionText,
        string temporaryPackagePath,
        bool skipDependenciesCheck)
    {
        var managedResult = new ManagedModulePublishService(_logger).PublishAsync(
                new ManagedModulePublishRequest
                {
                    ModulePath = modulePath,
                    Repository = CreateManagedReadRepository(repositoryName, repoConfig),
                    PublishRepository = CreateManagedPublishRepository(repositoryName, repoConfig),
                    OutputDirectory = temporaryPackagePath,
                    Credential = readCredential,
                    PublishCredential = publishCredential,
                    SkipDependenciesCheck = skipDependenciesCheck,
                    SkipModuleManifestValidate = false,
                    Force = publish.Force
                })
            .GetAwaiter()
            .GetResult();

        if (!managedResult.Published)
            throw new InvalidOperationException(managedResult.Message ?? $"Managed module publish did not publish {plan.ModuleName} {versionText}.");

        _logger.Info($"Published {plan.ModuleName} {versionText} to repository '{repositoryName}' using {PublishTool.ManagedModule}.");
    }

    private static ManagedModuleRepository CreateManagedPublishRepository(
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig)
    {
        var source = FirstNonEmpty(repoConfig?.PublishUri, repoConfig?.Uri, repoConfig?.SourceUri);
        if (string.IsNullOrWhiteSpace(source))
            source = ResolveDefaultManagedRepositorySource(repositoryName);
        else
            source = NormalizeManagedRepositorySource(source!);

        return new ManagedModuleRepository(
            repositoryName,
            source!,
            ManagedModuleRepositoryKind.Auto,
            repoConfig?.Trusted ?? true);
    }

    private static ManagedModuleRepository CreateManagedReadRepository(
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig)
    {
        var source = FirstNonEmpty(repoConfig?.Uri, repoConfig?.SourceUri, repoConfig?.PublishUri);
        if (string.IsNullOrWhiteSpace(source))
            source = ResolveDefaultManagedRepositorySource(repositoryName);
        else
            source = NormalizeManagedRepositorySource(source!);

        return new ManagedModuleRepository(
            repositoryName,
            source!,
            ManagedModuleRepositoryKind.Auto,
            repoConfig?.Trusted ?? true);
    }

    internal static string ResolveDefaultManagedRepositorySource(string repositoryName)
    {
        if (string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
            return ManagedModuleCatalogDefaults.PowerShellGalleryV2;

        throw new InvalidOperationException(
            $"Managed module publishing requires a repository Uri, SourceUri, or PublishUri for repository '{repositoryName}'.");
    }

    internal static string NormalizeManagedRepositorySource(string source)
    {
        var normalized = source.Trim().TrimEnd('/');
        return normalized.Equals(ManagedModuleCatalogDefaults.PowerShellGalleryV3, StringComparison.OrdinalIgnoreCase)
            ? ManagedModuleCatalogDefaults.PowerShellGalleryV2
            : normalized;
    }

    private static RepositoryCredential? ResolveManagedReadCredential(PublishRepositoryConfiguration? repoConfig)
    {
        if (repoConfig?.CredentialProvider is { Kind: not RepositoryCredentialProviderKind.None })
        {
            throw new InvalidOperationException(
                "Managed module publishing does not use external runtime credential providers. Provide ApiKey or static repository credentials, or use a compatibility publish tool.");
        }

        return repoConfig?.Credential;
    }

    private static RepositoryCredential? ResolveManagedPublishCredential(
        PublishConfiguration publish,
        PublishRepositoryConfiguration? repoConfig)
    {
        _ = ResolveManagedReadCredential(repoConfig);

        if (!string.IsNullOrWhiteSpace(publish.ApiKey))
            return new RepositoryCredential { Secret = publish.ApiKey };

        return repoConfig?.Credential;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private void EnsureManagedVersionIsGreaterThanRepository(
        ManagedModuleRepository repository,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        RepositoryCredential? credential)
    {
        var publishVersionText = FormatSemVer(moduleVersion, preRelease);
        if (!TryParseSemVer(publishVersionText, out var publishVersion))
            throw new InvalidOperationException($"Could not parse module version for publish: '{publishVersionText}'.");

        SemVer? current = null;
        try
        {
            var versions = new ManagedModuleRepositoryClient(_logger)
                .GetVersionsAsync(repository, moduleName, includePrerelease: true, credential: credential)
                .GetAwaiter()
                .GetResult();

            foreach (var version in versions)
            {
                if (!TryParseSemVer(version.Version, out var parsed))
                    continue;

                if (current is null || parsed.CompareTo(current.Value) > 0)
                    current = parsed;
            }
        }
        catch (Exception ex) when (IsRepositoryPackageNotFound(moduleName, ex))
        {
            _logger.Verbose($"No existing repository version was found for {moduleName} on '{repository.Name}'. Treating this as a first publish.");
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to query repository version for {moduleName} on '{repository.Name}'. Use -Force to publish without version check. {ex.Message}");
        }

        if (current is null)
            return;

        if (publishVersion.CompareTo(current.Value) <= 0)
            throw new InvalidOperationException($"Module version '{publishVersionText}' is not greater than repository version '{FormatSemVer(current.Value.Version.ToString(), current.Value.PreRelease)}' for '{moduleName}'. Use -Force to publish anyway.");
    }
}
