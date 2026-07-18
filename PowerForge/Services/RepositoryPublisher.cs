using System;
using System.IO;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Publishes module folders or nupkg packages to PowerShell repositories using PSResourceGet or PowerShellGet.
/// </summary>
public sealed class RepositoryPublisher
{
    private readonly ILogger _logger;
    private readonly PSResourceGetClient _psResourceGet;
    private readonly PowerShellGetClient _powerShellGet;
    private readonly IProcessRunner _processRunner;

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
        : this(logger, runner, new ProcessRunner())
    {
    }

    /// <summary>
    /// Creates a new publisher using the provided logger, PowerShell runner, and external process runner.
    /// </summary>
    public RepositoryPublisher(ILogger logger, IPowerShellRunner runner, IProcessRunner processRunner)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
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
        var credential = ResolveRepositoryCredential(repoConfig);

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
                if (request.RemotePublishAttempted is not null &&
                    !_powerShellGet.IsAvailable(out var availabilityMessage))
                    throw new PowerShellToolNotAvailableException("PowerShellGet", availabilityMessage ?? "PowerShellGet is not available.");

                request.RemotePublishAttempted?.Invoke();
                _powerShellGet.Publish(
                    new PowerShellGetPublishOptions(
                        path: fullPath,
                        repository: repositoryParameter,
                        apiKey: request.ApiKey,
                        credential: credential));
            }
            else
            {
                if (request.RemotePublishAttempted is not null &&
                    !_psResourceGet.IsAvailable(out var availabilityMessage))
                    throw new PowerShellToolNotAvailableException("PSResourceGet", availabilityMessage ?? "PSResourceGet is not available.");

                request.RemotePublishAttempted?.Invoke();
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

    internal RepositoryCredential? ResolveCredentialForRepository(PublishRepositoryConfiguration? repoConfig)
        => ResolveRepositoryCredential(repoConfig);

    private RepositoryCredential? ResolveRepositoryCredential(PublishRepositoryConfiguration? repoConfig)
    {
        if (repoConfig?.CredentialProvider is null ||
            repoConfig.CredentialProvider.Kind == RepositoryCredentialProviderKind.None)
        {
            return repoConfig?.Credential;
        }

        if (repoConfig.Credential is not null)
            throw new InvalidOperationException("Repository configuration cannot use both static credentials and a runtime credential provider.");

        return repoConfig.CredentialProvider.Kind switch
        {
            RepositoryCredentialProviderKind.JFrogOidc => ExchangeJFrogOidcToken(repoConfig.CredentialProvider),
            _ => throw new InvalidOperationException($"Repository credential provider '{repoConfig.CredentialProvider.Kind}' is not supported.")
        };
    }

    private RepositoryCredential ExchangeJFrogOidcToken(RepositoryCredentialProviderConfiguration provider)
    {
        if (string.IsNullOrWhiteSpace(provider.JFrogOidcProvider))
            throw new InvalidOperationException("JFrogOidcProvider is required for JFrog OIDC credential exchange.");

        var executable = ResolveOnPath(Path.DirectorySeparatorChar == '\\' ? "jf.exe" : "jf") ??
                         ResolveOnPath("jf");
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("JFrog CLI executable 'jf' was not found on PATH. Install JFrog CLI before using JFrog OIDC publishing.");

        var tokenId = ResolveJfrogOidcTokenId(provider);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(tokenId))
            environment["JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID"] = tokenId;

        var arguments = new List<string>
        {
            "eot",
            provider.JFrogOidcProvider!.Trim()
        };

        if (!string.IsNullOrWhiteSpace(provider.JFrogPlatformUri))
            arguments.Add("--url=" + provider.JFrogPlatformUri!.Trim());

        arguments.Add("--oidc-provider-type=" + provider.JFrogOidcProviderType);

        _logger.Info("Exchanging JFrog OIDC token for a short-lived repository credential.");
        var result = _processRunner.RunAsync(
            new ProcessRunRequest(
                executable!,
                Environment.CurrentDirectory,
                arguments,
                TimeSpan.FromMinutes(2),
                environmentVariables: environment.Count == 0 ? null : environment,
                captureOutput: true,
                captureError: true)).GetAwaiter().GetResult();

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException($"JFrog OIDC token exchange failed with exit code {result.ExitCode}. {detail}".Trim());
        }

        var credential = ParseJfrogOidcCredential(result.StdOut, provider.UserName);
        if (string.IsNullOrWhiteSpace(credential.Secret))
            throw new InvalidOperationException("JFrog OIDC token exchange succeeded but did not return an access token.");

        if (string.IsNullOrWhiteSpace(credential.UserName))
            throw new InvalidOperationException("JFrog OIDC token exchange succeeded but did not return a username. Provide RepositoryCredentialUserName as a fallback.");

        return credential;
    }

    private static string? ResolveJfrogOidcTokenId(RepositoryCredentialProviderConfiguration provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.JFrogOidcTokenIdEnvironmentVariable))
        {
            var value = Environment.GetEnvironmentVariable(provider.JFrogOidcTokenIdEnvironmentVariable!.Trim());
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.IsNullOrWhiteSpace(provider.JFrogOidcTokenId)
            ? null
            : provider.JFrogOidcTokenId!.Trim();
    }

    private static RepositoryCredential ParseJfrogOidcCredential(string stdout, string? fallbackUserName)
    {
        var trimmed = stdout?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            return new RepositoryCredential
            {
                UserName = TryGetJsonString(root, "username") ?? fallbackUserName,
                Secret = TryGetJsonString(root, "access_token")
            };
        }

        string? accessToken = null;
        string? userName = null;
        foreach (var line in SplitLines(trimmed))
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            if (parts[0].Equals("access_token", StringComparison.OrdinalIgnoreCase))
                accessToken = parts[1];
            else if (parts[0].Equals("username", StringComparison.OrdinalIgnoreCase))
                userName = parts[1];
        }

        return new RepositoryCredential
        {
            UserName = userName ?? fallbackUserName,
            Secret = accessToken
        };
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IEnumerable<string> SplitLines(string value)
        => value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

    private static string? ResolveOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var entry in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            try
            {
                var candidate = Path.Combine(entry.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}
