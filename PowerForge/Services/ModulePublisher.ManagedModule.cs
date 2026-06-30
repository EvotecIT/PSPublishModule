namespace PowerForge;

/// <content>
/// Managed C# repository publish integration.
/// </content>
public sealed partial class ModulePublisher
{
    private void PublishToRepositoryWithManagedModule(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        string modulePath,
        string repositoryName,
        PublishRepositoryConfiguration? repoConfig,
        RepositoryCredential? credential,
        string versionText,
        string temporaryPackagePath,
        bool skipDependenciesCheck)
    {
        var managedResult = new ManagedModulePublishService(_logger).PublishAsync(
                new ManagedModulePublishRequest
                {
                    ModulePath = modulePath,
                    Repository = CreateManagedPublishRepository(repositoryName, repoConfig),
                    OutputDirectory = temporaryPackagePath,
                    Credential = credential,
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
        {
            if (string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
                source = "https://www.powershellgallery.com/api/v3/index.json";
            else
                throw new InvalidOperationException(
                    $"Managed module publishing requires a repository Uri, SourceUri, or PublishUri for repository '{repositoryName}'.");
        }

        return new ManagedModuleRepository(
            repositoryName,
            source!,
            ManagedModuleRepositoryKind.Auto,
            repoConfig?.Trusted ?? true);
    }

    private static RepositoryCredential? ResolveManagedPublishCredential(
        PublishConfiguration publish,
        PublishRepositoryConfiguration? repoConfig)
    {
        if (repoConfig?.CredentialProvider is { Kind: not RepositoryCredentialProviderKind.None })
        {
            throw new InvalidOperationException(
                "Managed module publishing does not use external runtime credential providers. Provide ApiKey or static repository credentials, or use a compatibility publish tool.");
        }

        if (repoConfig?.Credential is not null)
            return repoConfig.Credential;

        return string.IsNullOrWhiteSpace(publish.ApiKey)
            ? null
            : new RepositoryCredential { Secret = publish.ApiKey };
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
