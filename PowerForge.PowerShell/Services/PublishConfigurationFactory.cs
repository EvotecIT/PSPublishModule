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
        var apiKeyToUse = request.ParameterSetName switch
        {
            "ApiFromFile" => File.ReadAllText(request.FilePath).Trim(),
            "AzureArtifacts" => AzureArtifactsApiKeyPlaceholder,
            _ => request.ApiKey
        };

        if (destination == PublishDestination.GitHub && string.IsNullOrWhiteSpace(request.UserName))
            throw new ArgumentException("UserName is required for GitHub. Please fix New-ConfigurationPublish and provide UserName", nameof(request));

        var repositorySecret = ResolveSecret(
            request.RepositoryCredentialSecretSpecified,
            request.RepositoryCredentialSecret,
            request.RepositoryCredentialSecretFilePathSpecified,
            request.RepositoryCredentialSecretFilePath);

        var anyRepositoryUriProvided =
            !string.IsNullOrWhiteSpace(request.RepositoryUri) ||
            !string.IsNullOrWhiteSpace(request.RepositorySourceUri) ||
            !string.IsNullOrWhiteSpace(request.RepositoryPublishUri);

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

        if (!isAzureArtifacts && anyRepositoryUriProvided)
        {
            var resolvedRepositoryName = request.RepositoryName?.Trim();
            if (string.IsNullOrWhiteSpace(resolvedRepositoryName))
                throw new ArgumentException("RepositoryName is required when RepositoryUri/RepositorySourceUri/RepositoryPublishUri is provided.", nameof(request));
            if (string.Equals(resolvedRepositoryName, PowerShellGalleryRepositoryName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("RepositoryName cannot be 'PSGallery' when RepositoryUri/RepositorySourceUri/RepositoryPublishUri is provided.", nameof(request));
        }

        var hasRepoCredentialSecret = !string.IsNullOrWhiteSpace(repositorySecret);
        if (hasRepoCredentialSecret && string.IsNullOrWhiteSpace(request.RepositoryCredentialUserName))
            throw new ArgumentException("RepositoryCredentialUserName is required when RepositoryCredentialSecret/RepositoryCredentialSecretFilePath is provided.", nameof(request));

        PublishRepositoryConfiguration? repository = null;
        var hasRepoCredential = !string.IsNullOrWhiteSpace(request.RepositoryCredentialUserName) && hasRepoCredentialSecret;
        var hasRepoOptions = isAzureArtifacts ||
                             anyRepositoryUriProvided ||
                             hasRepoCredential ||
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

            request.RepositoryName = repository.Name;
        }
        else if (hasRepoOptions)
        {
            repository = new PublishRepositoryConfiguration
            {
                Name = request.RepositoryName,
                Uri = request.RepositoryUri,
                SourceUri = request.RepositorySourceUri,
                PublishUri = request.RepositoryPublishUri,
                Trusted = request.RepositoryTrusted,
                Priority = request.RepositoryPriority,
                ApiVersion = request.RepositoryApiVersion,
                EnsureRegistered = request.EnsureRepositoryRegistered,
                UnregisterAfterUse = request.UnregisterRepositoryAfterPublish,
                Credential = hasRepoCredential
                    ? new RepositoryCredential
                    {
                        UserName = request.RepositoryCredentialUserName,
                        Secret = repositorySecret
                    }
                    : null
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
                RepositoryName = request.RepositoryName,
                Repository = repository,
                Force = request.Force,
                OverwriteTagName = request.OverwriteTagName,
                DoNotMarkAsPreRelease = request.DoNotMarkAsPreRelease,
                GenerateReleaseNotes = request.GenerateReleaseNotes,
                Verbose = request.Verbose
            }
        };
    }

    private static string ResolveSecret(bool secretSpecified, string? secret, bool secretFileSpecified, string? secretFilePath)
    {
        if (secretFileSpecified && !string.IsNullOrWhiteSpace(secretFilePath))
            return File.ReadAllText(secretFilePath!.Trim()).Trim();

        if (secretSpecified && !string.IsNullOrWhiteSpace(secret))
            return secret!.Trim();

        return string.Empty;
    }
}
