using System.IO;

namespace PowerForge;

internal sealed class PublishConfigurationFactory
{
    private const string AzureArtifactsApiKeyPlaceholder = "AzureDevOps";
    private const string PowerShellGalleryRepositoryName = "PSGallery";

    public ConfigurationPublishSegment Create(PublishConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var isAzureArtifacts = string.Equals(request.ParameterSetName, "AzureArtifacts", StringComparison.Ordinal);
        var destination = isAzureArtifacts ? PublishDestination.PowerShellGallery : request.Type;
        if (string.Equals(request.ParameterSetName, "JFrog", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(request.ApiKey) &&
            !string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new ArgumentException("Specify either ApiKey or FilePath for JFrog publishing, not both.", nameof(request));
        }

        var apiKeyToUse = request.ParameterSetName switch
        {
            "ApiFromFile" => File.ReadAllText(request.FilePath).Trim(),
            "AzureArtifacts" => AzureArtifactsApiKeyPlaceholder,
            "JFrog" when !string.IsNullOrWhiteSpace(request.FilePath) => File.ReadAllText(request.FilePath).Trim(),
            _ => request.ApiKey
        };

        if (destination == PublishDestination.GitHub && string.IsNullOrWhiteSpace(request.UserName))
            throw new ArgumentException("UserName is required for GitHub. Please fix New-ConfigurationPublish and provide UserName", nameof(request));

        var repositoryName = request.RepositoryName;
        var repositoryUri = request.RepositoryUri;
        var repositorySourceUri = request.RepositorySourceUri;
        var repositoryPublishUri = request.RepositoryPublishUri;
        var repositoryApiVersion = request.RepositoryApiVersion;
        var hasJFrogShortcut =
            !string.IsNullOrWhiteSpace(request.JFrogBaseUri) ||
            !string.IsNullOrWhiteSpace(request.JFrogRepository);

        if (isAzureArtifacts && hasJFrogShortcut)
            throw new ArgumentException("JFrogBaseUri/JFrogRepository cannot be combined with the Azure Artifacts preset.", nameof(request));

        if (hasJFrogShortcut)
        {
            var endpoint = PrivateGalleryRepositoryEndpoints.Create(
                PrivateGalleryProvider.JFrog,
                repositoryName: repositoryName,
                repositoryUri: repositoryUri,
                repositorySourceUri: repositorySourceUri,
                repositoryPublishUri: repositoryPublishUri,
                jfrogBaseUri: request.JFrogBaseUri,
                jfrogRepository: request.JFrogRepository);

            repositoryName = endpoint.RepositoryName;
            repositoryUri = endpoint.PSResourceGetUri;
            repositorySourceUri = endpoint.PowerShellGetSourceUri;
            repositoryPublishUri = endpoint.PowerShellGetPublishUri;
            if (repositoryApiVersion == RepositoryApiVersion.Auto)
                repositoryApiVersion = RepositoryApiVersion.V3;
        }

        var repositorySecret = ResolveSecret(
            request.RepositoryCredentialSecretSpecified,
            request.RepositoryCredentialSecret,
            request.RepositoryCredentialSecretFilePathSpecified,
            request.RepositoryCredentialSecretFilePath,
            request.RepositoryCredentialSecretEnvironmentVariableSpecified,
            request.RepositoryCredentialSecretEnvironmentVariable);
        var hasJfrogOidcProvider = !string.IsNullOrWhiteSpace(request.JFrogOidcProvider);
        var hasJfrogOidcOptions =
            hasJfrogOidcProvider ||
            !string.IsNullOrWhiteSpace(request.JFrogOidcTokenId) ||
            !string.IsNullOrWhiteSpace(request.JFrogOidcTokenIdEnvironmentVariable) ||
            !string.IsNullOrWhiteSpace(request.JFrogPlatformUri) ||
            request.JFrogOidcProviderType != JFrogOidcProviderType.GitHub;

        if (hasJfrogOidcOptions && !hasJFrogShortcut)
            throw new ArgumentException("JFrog OIDC parameters require JFrogBaseUri/JFrogRepository or the JFrog parameter set.", nameof(request));

        if (hasJfrogOidcOptions && !hasJfrogOidcProvider)
            throw new ArgumentException("JFrogOidcProvider is required when using JFrog OIDC publishing.", nameof(request));

        var anyRepositoryUriProvided =
            !string.IsNullOrWhiteSpace(repositoryUri) ||
            !string.IsNullOrWhiteSpace(repositorySourceUri) ||
            !string.IsNullOrWhiteSpace(repositoryPublishUri);

        var resolvedAzureArtifactsRepositoryName = string.IsNullOrWhiteSpace(request.RepositoryName)
            ? request.AzureArtifactsFeed?.Trim()
            : request.RepositoryName?.Trim();

        if (isAzureArtifacts &&
            !string.IsNullOrWhiteSpace(resolvedAzureArtifactsRepositoryName) &&
            string.Equals(resolvedAzureArtifactsRepositoryName, PowerShellGalleryRepositoryName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("RepositoryName cannot be 'PSGallery' when using the Azure Artifacts preset.", nameof(request));
        }

        if (isAzureArtifacts && anyRepositoryUriProvided)
            throw new ArgumentException("RepositoryUri/RepositorySourceUri/RepositoryPublishUri cannot be combined with the Azure Artifacts preset.", nameof(request));

        if (isAzureArtifacts && request.RepositoryApiVersion == RepositoryApiVersion.ContainerRegistry)
            throw new ArgumentException("RepositoryApiVersion ContainerRegistry cannot be used with the Azure Artifacts preset.", nameof(request));

        if (!isAzureArtifacts &&
            destination == PublishDestination.PowerShellGallery &&
            request.Enabled &&
            IsMicrosoftArtifactRegistryPublishTarget(request))
        {
            throw new ArgumentException("Microsoft Artifact Registry is read-only. Do not enable publishing to MAR; use it only as a dependency/discovery source.", nameof(request));
        }

        if (!isAzureArtifacts && anyRepositoryUriProvided)
        {
            var resolvedRepositoryName = repositoryName?.Trim();
            if (string.IsNullOrWhiteSpace(resolvedRepositoryName))
                throw new ArgumentException("RepositoryName is required when RepositoryUri/RepositorySourceUri/RepositoryPublishUri is provided.", nameof(request));
            if (string.Equals(resolvedRepositoryName, PowerShellGalleryRepositoryName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("RepositoryName cannot be 'PSGallery' when RepositoryUri/RepositorySourceUri/RepositoryPublishUri is provided.", nameof(request));
        }

        var repositorySecretSourceSpecified =
            request.RepositoryCredentialSecretSpecified ||
            request.RepositoryCredentialSecretFilePathSpecified ||
            request.RepositoryCredentialSecretEnvironmentVariableSpecified;
        if (repositorySecretSourceSpecified && string.IsNullOrWhiteSpace(repositorySecret))
            throw new ArgumentException("Repository credential secret could not be resolved. Check RepositoryCredentialSecret, RepositoryCredentialSecretFilePath, or RepositoryCredentialSecretEnvironmentVariable.", nameof(request));

        var hasRepoCredentialSecret = !string.IsNullOrWhiteSpace(repositorySecret);
        if (hasRepoCredentialSecret && string.IsNullOrWhiteSpace(request.RepositoryCredentialUserName))
            throw new ArgumentException("RepositoryCredentialUserName is required when RepositoryCredentialSecret, RepositoryCredentialSecretFilePath, or RepositoryCredentialSecretEnvironmentVariable is provided.", nameof(request));

        if (hasRepoCredentialSecret && hasJfrogOidcOptions)
            throw new ArgumentException("Repository credential secrets cannot be combined with JFrog OIDC publishing. Use either static credentials or JFrogOidcProvider.", nameof(request));

        PublishRepositoryConfiguration? repository = null;
        var hasRepoCredential = !string.IsNullOrWhiteSpace(request.RepositoryCredentialUserName) && hasRepoCredentialSecret;
        var runtimeCredentialProvider = hasJfrogOidcProvider
            ? new RepositoryCredentialProviderConfiguration
            {
                Kind = RepositoryCredentialProviderKind.JFrogOidc,
                UserName = request.RepositoryCredentialUserName,
                JFrogPlatformUri = ResolveJfrogPlatformUri(request.JFrogPlatformUri, request.JFrogBaseUri),
                JFrogOidcProvider = request.JFrogOidcProvider?.Trim(),
                JFrogOidcTokenId = string.IsNullOrWhiteSpace(request.JFrogOidcTokenId) ? null : request.JFrogOidcTokenId!.Trim(),
                JFrogOidcTokenIdEnvironmentVariable = string.IsNullOrWhiteSpace(request.JFrogOidcTokenIdEnvironmentVariable) ? null : request.JFrogOidcTokenIdEnvironmentVariable!.Trim(),
                JFrogOidcProviderType = request.JFrogOidcProviderType
            }
            : null;
        var hasRepoOptions = isAzureArtifacts ||
                             anyRepositoryUriProvided ||
                             hasRepoCredential ||
                             runtimeCredentialProvider is not null ||
                             request.RepositoryPriority is not null ||
                             request.RepositoryApiVersion != RepositoryApiVersion.Auto ||
                             !request.EnsureRepositoryRegistered ||
                             request.UnregisterRepositoryAfterPublish;

        if (isAzureArtifacts)
        {
            repository = AzureArtifactsRepositoryEndpoints.CreatePublishRepositoryConfiguration(
                request.AzureDevOpsOrganization,
                request.AzureDevOpsProject,
                request.AzureArtifactsFeed!,
                repositoryName: request.RepositoryName,
                trusted: request.RepositoryTrusted,
                priority: request.RepositoryPriority,
                apiVersion: request.RepositoryApiVersion == RepositoryApiVersion.Auto ? RepositoryApiVersion.V3 : request.RepositoryApiVersion,
                ensureRegistered: request.EnsureRepositoryRegistered,
                unregisterAfterUse: request.UnregisterRepositoryAfterPublish,
                credential: hasRepoCredential
                    ? new RepositoryCredential
                    {
                        UserName = request.RepositoryCredentialUserName!.Trim(),
                        Secret = repositorySecret
                    }
                    : null);

            repositoryName = repository.Name;
        }
        else if (hasRepoOptions)
        {
            repository = new PublishRepositoryConfiguration
            {
                Name = repositoryName,
                Uri = repositoryUri,
                SourceUri = repositorySourceUri,
                PublishUri = repositoryPublishUri,
                Trusted = request.RepositoryTrusted,
                Priority = request.RepositoryPriority,
                ApiVersion = repositoryApiVersion,
                EnsureRegistered = request.EnsureRepositoryRegistered,
                UnregisterAfterUse = request.UnregisterRepositoryAfterPublish,
                Credential = hasRepoCredential
                    ? new RepositoryCredential
                    {
                        UserName = request.RepositoryCredentialUserName,
                        Secret = repositorySecret
                    }
                    : null,
                CredentialProvider = runtimeCredentialProvider
            };
        }

        return new ConfigurationPublishSegment
        {
            Configuration = new PublishConfiguration
            {
                Destination = destination,
                Tool = request.Tool,
                ApiKey = apiKeyToUse,
                ID = request.ID,
                Enabled = request.Enabled,
                UserName = request.UserName,
                RepositoryName = repositoryName,
                Repository = repository,
                Force = request.Force,
                OverwriteTagName = request.OverwriteTagName,
                DoNotMarkAsPreRelease = request.DoNotMarkAsPreRelease,
                GenerateReleaseNotes = request.GenerateReleaseNotes,
                UseAsDependencyVersionSource = request.UseAsDependencyVersionSource,
                Verbose = request.Verbose
            }
        };
    }

    private static string ResolveSecret(
        bool secretSpecified,
        string? secret,
        bool secretFileSpecified,
        string? secretFilePath,
        bool secretEnvironmentVariableSpecified,
        string? secretEnvironmentVariable)
    {
        if (secretFileSpecified && !string.IsNullOrWhiteSpace(secretFilePath))
            return File.ReadAllText(secretFilePath!.Trim()).Trim();

        if (secretEnvironmentVariableSpecified && !string.IsNullOrWhiteSpace(secretEnvironmentVariable))
            return Environment.GetEnvironmentVariable(secretEnvironmentVariable!.Trim())?.Trim() ?? string.Empty;

        if (secretSpecified && !string.IsNullOrWhiteSpace(secret))
            return secret!.Trim();

        return string.Empty;
    }

    private static string? ResolveJfrogPlatformUri(string? explicitPlatformUri, string? jfrogBaseUri)
    {
        if (!string.IsNullOrWhiteSpace(explicitPlatformUri))
            return explicitPlatformUri!.Trim().TrimEnd('/') + "/";

        if (string.IsNullOrWhiteSpace(jfrogBaseUri))
            return null;

        var candidate = jfrogBaseUri!.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return candidate.TrimEnd('/') + "/";

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Equals("/artifactory", StringComparison.OrdinalIgnoreCase))
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/";

        return candidate.TrimEnd('/') + "/";
    }

    private static bool IsMicrosoftArtifactRegistryPublishTarget(PublishConfigurationRequest request)
        => MicrosoftArtifactRegistryRepository.IsDefaultUri(request.RepositoryUri) ||
           MicrosoftArtifactRegistryRepository.IsDefaultUri(request.RepositorySourceUri) ||
           MicrosoftArtifactRegistryRepository.IsDefaultUri(request.RepositoryPublishUri);
}
