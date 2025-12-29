using System;
using System.IO;

namespace PowerForge;

/// <summary>
/// Publishes module folders or nupkg packages to PowerShell repositories using PSResourceGet or PowerShellGet.
/// </summary>
public sealed class RepositoryPublisher
{
    private readonly ILogger _logger;
    private readonly PSResourceGetClient _psResourceGet;
    private readonly PowerShellGetClient _powerShellGet;

    /// <summary>
    /// Creates a new publisher using the provided logger and the default out-of-process PowerShell runner.
    /// </summary>
    public RepositoryPublisher(ILogger logger)
        : this(logger, new PowerShellRunner())
    {
    }

    /// <summary>
    /// Creates a new publisher using the provided logger and runner.
    /// </summary>
    public RepositoryPublisher(ILogger logger, IPowerShellRunner runner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        _psResourceGet = new PSResourceGetClient(runner, _logger);
        _powerShellGet = new PowerShellGetClient(runner, _logger);
    }

    /// <summary>
    /// Publishes a module folder or nupkg based on <paramref name="request"/>.
    /// </summary>
    public RepositoryPublishResult Publish(RepositoryPublishRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Path))
            throw new ArgumentException("Path is required.", nameof(request));

        var fullPath = Path.GetFullPath(request.Path.Trim().Trim('"'));

        var configuredRepository = request.Repository;
        var configuredRepositoryName = configuredRepository?.Name;

        var legacyRepositoryName = request.RepositoryName;
        var repositoryName =
            configuredRepositoryName is not null && !string.IsNullOrWhiteSpace(configuredRepositoryName)
                ? configuredRepositoryName.Trim()
                : (legacyRepositoryName is not null && !string.IsNullOrWhiteSpace(legacyRepositoryName)
                    ? legacyRepositoryName.Trim()
                    : "PSGallery");

        var repositorySpecified = !string.IsNullOrWhiteSpace(request.Repository?.Name) ||
                                  !string.IsNullOrWhiteSpace(request.RepositoryName);

        var repoConfig = request.Repository;
        var credential = repoConfig?.Credential;

        var tool = request.Tool;
        if (tool == PublishTool.Auto)
        {
            try
            {
                return PublishWithTool(PublishTool.PSResourceGet, request, fullPath, repositoryName, repositorySpecified, repoConfig, credential);
            }
            catch (PowerShellToolNotAvailableException) when (!request.IsNupkg)
            {
                return PublishWithTool(PublishTool.PowerShellGet, request, fullPath, repositoryName, repositorySpecified, repoConfig, credential);
            }
            catch (PowerShellToolNotAvailableException ex) when (request.IsNupkg)
            {
                throw new InvalidOperationException(
                    "Publishing a nupkg requires Microsoft.PowerShell.PSResourceGet; it is not available on this system. " +
                    "Install PSResourceGet or publish a module folder instead.",
                    ex);
            }
        }

        if (tool == PublishTool.PowerShellGet && request.IsNupkg)
            throw new InvalidOperationException("PowerShellGet cannot publish nupkg packages. Use PSResourceGet or publish a module folder.");

        return PublishWithTool(tool, request, fullPath, repositoryName, repositorySpecified, repoConfig, credential);
    }

    private RepositoryPublishResult PublishWithTool(
        PublishTool tool,
        RepositoryPublishRequest request,
        string fullPath,
        string repositoryName,
        bool repositorySpecified,
        PublishRepositoryConfiguration? repoConfig,
        RepositoryCredential? credential)
    {
        bool createdRepository = false;
        bool unregisteredRepository = false;
        var repositoryParameter = repositorySpecified ? repositoryName : null;

        try
        {
            if (repoConfig is not null && repoConfig.EnsureRegistered && HasRepositoryUris(repoConfig))
            {
                createdRepository = EnsureRepositoryRegistered(tool, repositoryName, repoConfig);
            }

            if (!string.IsNullOrWhiteSpace(request.DestinationPath))
                _logger.Info($"Publishing {fullPath} to destination '{request.DestinationPath}' using {tool}");
            else if (!string.IsNullOrWhiteSpace(repositoryParameter))
                _logger.Info($"Publishing {fullPath} to repository '{repositoryName}' using {tool}");
            else
                _logger.Info($"Publishing {fullPath} using {tool}");

            if (tool == PublishTool.PowerShellGet)
            {
                _powerShellGet.Publish(
                    new PowerShellGetPublishOptions(
                        path: fullPath,
                        repository: repositoryParameter,
                        apiKey: request.ApiKey,
                        credential: credential));
            }
            else
            {
                _psResourceGet.Publish(
                    new PSResourcePublishOptions(
                        path: fullPath,
                        isNupkg: request.IsNupkg,
                        repository: repositoryParameter,
                        apiKey: request.ApiKey,
                        destinationPath: request.DestinationPath,
                        skipDependenciesCheck: request.SkipDependenciesCheck,
                        skipModuleManifestValidate: request.SkipModuleManifestValidate,
                        credential: credential));
            }
        }
        finally
        {
            if (createdRepository && repoConfig is not null && repoConfig.UnregisterAfterUse)
            {
                try
                {
                    UnregisterRepository(tool, repositoryName);
                    unregisteredRepository = true;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to unregister repository '{repositoryName}': {ex.Message}");
                }
            }
        }

        return new RepositoryPublishResult(
            path: fullPath,
            isNupkg: request.IsNupkg,
            repositoryName: repositoryName,
            tool: tool,
            repositoryCreated: createdRepository,
            repositoryUnregistered: unregisteredRepository);
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
}
