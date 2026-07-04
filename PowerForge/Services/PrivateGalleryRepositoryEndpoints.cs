using System;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Resolves provider-specific private gallery inputs into generic PowerShellGet and PSResourceGet endpoints.
/// </summary>
public static class PrivateGalleryRepositoryEndpoints
{
    private const string PowerShellGalleryRepositoryName = "PSGallery";
    private const string PowerShellGalleryRepositoryUri = "https://www.powershellgallery.com/api/v3/index.json";

    /// <summary>
    /// Resolves Azure Artifacts, JFrog, GitHub Packages, or generic NuGet private-gallery inputs into concrete repository endpoints.
    /// </summary>
    public static PrivateGalleryRepositoryEndpoint Create(
        PrivateGalleryProvider provider,
        string? azureDevOpsOrganization = null,
        string? azureDevOpsProject = null,
        string? azureArtifactsFeed = null,
        string? repositoryName = null,
        string? repository = null,
        string? repositoryUri = null,
        string? repositorySourceUri = null,
        string? repositoryPublishUri = null,
        string? jfrogBaseUri = null,
        string? jfrogRepository = null,
        string? gitHubOwner = null)
    {
        if (provider == PrivateGalleryProvider.AzureArtifacts)
        {
            var feed = string.IsNullOrWhiteSpace(azureArtifactsFeed) ? repository : azureArtifactsFeed;
            var localRepositoryName = string.IsNullOrWhiteSpace(azureArtifactsFeed) ? repositoryName : repositoryName ?? repository;
            var endpoint = AzureArtifactsRepositoryEndpoints.Create(
                azureDevOpsOrganization ?? string.Empty,
                azureDevOpsProject,
                feed ?? string.Empty,
                localRepositoryName);

            return new PrivateGalleryRepositoryEndpoint(
                PrivateGalleryProvider.AzureArtifacts,
                endpoint.RepositoryName,
                endpoint.Organization,
                endpoint.Project,
                endpoint.Feed,
                endpoint.PowerShellGetSourceUri,
                endpoint.PowerShellGetPublishUri,
                endpoint.PSResourceGetUri,
                null,
                null);
        }

        if (provider == PrivateGalleryProvider.JFrog)
        {
            var remoteRepository = NormalizeOptional(jfrogRepository) ?? NormalizeOptional(repository);
            var resolvedRepositoryName = ResolveRepositoryName(repositoryName, remoteRepository);
            if (string.IsNullOrWhiteSpace(resolvedRepositoryName))
                throw new ArgumentException("RepositoryName is required for JFrog when no repository id is provided.", nameof(repositoryName));

            var sourceUri = NormalizeOptional(repositorySourceUri);
            var publishUri = NormalizeOptional(repositoryPublishUri);
            var psResourceUri = NormalizeOptional(repositoryUri);
            var baseUri = NormalizeOptional(jfrogBaseUri);

            if (!string.IsNullOrWhiteSpace(remoteRepository) && !string.IsNullOrWhiteSpace(baseUri))
            {
                var normalizedBase = baseUri!.TrimEnd('/');
                sourceUri ??= $"{normalizedBase}/api/nuget/{Uri.EscapeDataString(remoteRepository!)}";
                publishUri ??= sourceUri;
                psResourceUri ??= $"{normalizedBase}/api/nuget/v3/{Uri.EscapeDataString(remoteRepository!)}/index.json";
            }
            else if (!string.IsNullOrWhiteSpace(psResourceUri))
            {
                sourceUri ??= psResourceUri;
                publishUri ??= sourceUri;
            }

            if (string.IsNullOrWhiteSpace(psResourceUri))
                throw new ArgumentException("RepositoryUri or JFrogBaseUri plus JFrogRepository is required for JFrog.", nameof(repositoryUri));

            return new PrivateGalleryRepositoryEndpoint(
                PrivateGalleryProvider.JFrog,
                resolvedRepositoryName!,
                null,
                null,
                remoteRepository ?? resolvedRepositoryName!,
                sourceUri ?? psResourceUri!,
                publishUri ?? sourceUri ?? psResourceUri!,
                psResourceUri!,
                baseUri,
                remoteRepository);
        }

        if (provider == PrivateGalleryProvider.GitHubPackages)
        {
            var owner = NormalizeGitHubOwner(gitHubOwner) ?? NormalizeGitHubOwner(repository);
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("GitHubOwner or Repository is required for GitHub Packages.", nameof(gitHubOwner));

            var githubName = ResolveRepositoryName(repositoryName, owner);
            if (string.IsNullOrWhiteSpace(githubName))
                throw new ArgumentException("RepositoryName is required for GitHub Packages when no owner is provided.", nameof(repositoryName));

            var serviceIndex = NormalizeOptional(repositoryUri) ?? $"https://nuget.pkg.github.com/{Uri.EscapeDataString(owner!)}/index.json";
            var sourceUri = NormalizeOptional(repositorySourceUri) ?? serviceIndex;

            return new PrivateGalleryRepositoryEndpoint(
                PrivateGalleryProvider.GitHubPackages,
                githubName!,
                null,
                null,
                owner!,
                sourceUri,
                NormalizeOptional(repositoryPublishUri) ?? sourceUri,
                serviceIndex,
                null,
                null);
        }

        if (provider != PrivateGalleryProvider.NuGet)
            throw new ArgumentException($"Provider '{provider}' is not supported. Supported values: AzureArtifacts, JFrog, GitHubPackages, NuGet.", nameof(provider));

        var normalizedRepositoryUri = NormalizeOptional(repositoryUri);
        var candidateRepositoryName = NormalizeOptional(repositoryName) ?? NormalizeOptional(repository);
        if (IsPowerShellGallery(candidateRepositoryName, normalizedRepositoryUri))
        {
            return new PrivateGalleryRepositoryEndpoint(
                PrivateGalleryProvider.NuGet,
                PowerShellGalleryRepositoryName,
                null,
                null,
                PowerShellGalleryRepositoryName,
                PowerShellGalleryRepositoryUri,
                PowerShellGalleryRepositoryUri,
                PowerShellGalleryRepositoryUri,
                null,
                null);
        }

        var genericName = ResolveRepositoryName(repositoryName, repository);
        if (string.IsNullOrWhiteSpace(genericName))
            throw new ArgumentException("RepositoryName is required for generic NuGet private galleries.", nameof(repositoryName));
        if (string.IsNullOrWhiteSpace(normalizedRepositoryUri))
            throw new ArgumentException("RepositoryUri is required for generic NuGet private galleries.", nameof(repositoryUri));

        var genericSourceUri = NormalizeOptional(repositorySourceUri) ?? normalizedRepositoryUri!;
        return new PrivateGalleryRepositoryEndpoint(
            PrivateGalleryProvider.NuGet,
            genericName!,
            null,
            null,
            NormalizeOptional(repository) ?? genericName!,
            genericSourceUri,
            NormalizeOptional(repositoryPublishUri) ?? genericSourceUri,
            normalizedRepositoryUri!,
            null,
            NormalizeOptional(repository));
    }

    private static bool IsPowerShellGallery(string? repositoryName, string? repositoryUri)
        => string.Equals(repositoryName, PowerShellGalleryRepositoryName, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(repositoryUri, PowerShellGalleryRepositoryUri, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveRepositoryName(string? repositoryName, string? repository)
    {
        var resolved = NormalizeOptional(repositoryName) ?? NormalizeOptional(repository);
        if (string.Equals(resolved, "PSGallery", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private gallery repository name cannot resolve to 'PSGallery'.", nameof(repositoryName));
        return resolved;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value!.Trim().Trim('/');
    }

    private static string? NormalizeGitHubOwner(string? value)
    {
        var owner = NormalizeOptional(value);
        if (owner is null)
            return null;

        if (owner.Contains('/') || owner.Contains('\\') || owner.Any(char.IsWhiteSpace))
            throw new ArgumentException("GitHub owner must be a single GitHub user or organization name.", nameof(value));

        return owner;
    }
}

/// <summary>
/// Provider-neutral private-gallery endpoint details for PowerShellGet and PSResourceGet.
/// </summary>
public sealed class PrivateGalleryRepositoryEndpoint
{
    /// <summary>
    /// Creates a private-gallery endpoint result.
    /// </summary>
    public PrivateGalleryRepositoryEndpoint(
        PrivateGalleryProvider provider,
        string repositoryName,
        string? azureDevOpsOrganization,
        string? azureDevOpsProject,
        string repository,
        string powerShellGetSourceUri,
        string powerShellGetPublishUri,
        string psResourceGetUri,
        string? jfrogBaseUri,
        string? jfrogRepository)
    {
        Provider = provider;
        RepositoryName = repositoryName;
        AzureDevOpsOrganization = azureDevOpsOrganization;
        AzureDevOpsProject = azureDevOpsProject;
        Repository = repository;
        PowerShellGetSourceUri = powerShellGetSourceUri;
        PowerShellGetPublishUri = powerShellGetPublishUri;
        PSResourceGetUri = psResourceGetUri;
        JFrogBaseUri = jfrogBaseUri;
        JFrogRepository = jfrogRepository;
    }

    /// <summary>Normalized private gallery provider.</summary>
    public PrivateGalleryProvider Provider { get; }

    /// <summary>Local PowerShell repository name.</summary>
    public string RepositoryName { get; }

    /// <summary>Azure DevOps organization for Azure Artifacts endpoints.</summary>
    public string? AzureDevOpsOrganization { get; }

    /// <summary>Azure DevOps project for project-scoped Azure Artifacts endpoints.</summary>
    public string? AzureDevOpsProject { get; }

    /// <summary>Provider repository/feed id.</summary>
    public string Repository { get; }

    /// <summary>PowerShellGet source URI.</summary>
    public string PowerShellGetSourceUri { get; }

    /// <summary>PowerShellGet publish URI.</summary>
    public string PowerShellGetPublishUri { get; }

    /// <summary>PSResourceGet v3 index URI.</summary>
    public string PSResourceGetUri { get; }

    /// <summary>JFrog Artifactory base URI when the provider is JFrog.</summary>
    public string? JFrogBaseUri { get; }

    /// <summary>JFrog NuGet repository key when the provider is JFrog.</summary>
    public string? JFrogRepository { get; }

    /// <summary>GitHub user or organization namespace when the provider is GitHub Packages.</summary>
    public string? GitHubOwner => Provider == PrivateGalleryProvider.GitHubPackages ? Repository : null;
}
