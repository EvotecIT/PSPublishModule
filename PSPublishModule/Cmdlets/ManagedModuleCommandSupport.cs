using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using PowerForge;

namespace PSPublishModule;

internal static class ManagedModuleCommandSupport
{
    internal const string DefaultRepositorySource = "https://www.powershellgallery.com/api/v3/index.json";
    internal const string DefaultRepositoryName = "PSGallery";

    internal static ManagedModuleRepository CreateRepository(PSCmdlet cmdlet, string repositoryName, string repository)
    {
        var source = ResolveRepositorySource(cmdlet, repository);
        var name = ResolveRepositoryName(repositoryName, source);
        return new ManagedModuleRepository(name, source);
    }

    internal static ManagedModuleRepository CreateRepository(
        PSCmdlet cmdlet,
        string repositoryName,
        string repository,
        string? profileName,
        bool repositoryWasBound)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return CreateRepository(cmdlet, repositoryName, repository);

        if (repositoryWasBound)
            throw new InvalidOperationException("Specify either ProfileName or Repository, not both.");

        var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(profileName!);
        var source = ResolveProfileSource(profile, profileName!, publish: false);
        return new ManagedModuleRepository(
            ResolveProfileRepositoryName(profile, profileName!),
            ResolveRepositorySource(cmdlet, source),
            ManagedModuleRepositoryKind.Auto,
            profile.Trusted);
    }

    internal static ManagedModuleRepository CreatePublishRepository(
        PSCmdlet cmdlet,
        string repositoryName,
        string? repository,
        string? outputDirectory,
        string? profileName,
        bool repositoryWasBound,
        bool outputDirectoryWasBound)
    {
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            if (repositoryWasBound || outputDirectoryWasBound)
                throw new InvalidOperationException("Specify either ProfileName, Repository, or OutputDirectory.");

            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(profileName!);
            var source = ResolveProfileSource(profile, profileName!, publish: true);
            return new ManagedModuleRepository(
                ResolveProfileRepositoryName(profile, profileName!),
                ResolveRepositorySource(cmdlet, source),
                ManagedModuleRepositoryKind.Auto,
                profile.Trusted);
        }

        if (!string.IsNullOrWhiteSpace(repository))
            return CreateRepository(cmdlet, repositoryName, repository!);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
            return new ManagedModuleRepository("Local", ResolveProviderPath(cmdlet, outputDirectory)!);

        throw new ArgumentException("Specify Repository, OutputDirectory, or ProfileName.");
    }

    internal static string ResolveRepositoryName(string repositoryName, string repository)
    {
        if (!string.Equals(repositoryName, DefaultRepositoryName, StringComparison.OrdinalIgnoreCase))
            return repositoryName;

        return GetRepositoryKind(repository) == ManagedModuleRepositoryKind.LocalFolder
            ? "Local"
            : repositoryName;
    }

    internal static string ResolveRepositorySource(PSCmdlet cmdlet, string repository)
        => GetRepositoryKind(repository) == ManagedModuleRepositoryKind.LocalFolder
            ? ResolveProviderPath(cmdlet, repository) ?? repository
            : repository;

    internal static bool HasWildcard(string value)
        => value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;

    internal static string? ResolveProviderPath(PSCmdlet cmdlet, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return cmdlet.SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
    }

    internal static RepositoryCredential? ResolveCredential(
        PSCmdlet cmdlet,
        PSCredential? credential,
        string? credentialUserName,
        string? credentialSecret,
        string? credentialSecretFilePath)
    {
        var userName = credential?.UserName;
        var secret = credential?.GetNetworkCredential()?.Password;
        if (!string.IsNullOrWhiteSpace(credentialUserName))
            userName = credentialUserName;
        if (!string.IsNullOrWhiteSpace(credentialSecret))
            secret = credentialSecret;
        if (string.IsNullOrWhiteSpace(secret) && !string.IsNullOrWhiteSpace(credentialSecretFilePath))
        {
            var path = ResolveProviderPath(cmdlet, credentialSecretFilePath);
            secret = File.ReadAllText(path!).Trim();
        }

        return string.IsNullOrWhiteSpace(userName) && string.IsNullOrWhiteSpace(secret)
            ? null
            : new RepositoryCredential
            {
                UserName = userName,
                Secret = secret
            };
    }

    internal static ManagedModuleRepositoryClient CreateRepositoryClient(
        PSCmdlet cmdlet,
        ILogger logger,
        Uri? proxy,
        PSCredential? proxyCredential)
    {
        if (proxyCredential is not null && proxy is null)
            throw new InvalidOperationException("ProxyCredential requires Proxy.");

        return new ManagedModuleRepositoryClient(
            logger,
            options: new ManagedModuleRepositoryClientOptions
            {
                ProxyAddress = proxy,
                ProxyCredential = ResolveCredential(cmdlet, proxyCredential, null, null, null)
            });
    }

    internal static ManagedModuleTrustPolicy? CreateTrustPolicy(
        ManagedModuleTrustPolicy? trustPolicy,
        bool requireTrustedRepository,
        string[]? allowedAuthors)
    {
        var normalizedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(allowedAuthors);
        if (trustPolicy is null)
        {
            if (!requireTrustedRepository && normalizedAuthors.Count == 0)
                return null;

            return new ManagedModuleTrustPolicy
            {
                RequireTrustedRepository = requireTrustedRepository,
                AllowedAuthors = normalizedAuthors
            };
        }

        return new ManagedModuleTrustPolicy
        {
            RequireTrustedRepository = trustPolicy.RequireTrustedRepository || requireTrustedRepository,
            AllowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(
                (trustPolicy.AllowedAuthors ?? Array.Empty<string>()).Concat(normalizedAuthors).ToArray()),
            ApplyToDependencies = trustPolicy.ApplyToDependencies
        };
    }

    private static ManagedModuleRepositoryKind GetRepositoryKind(string repository)
        => new ManagedModuleRepository("Repository", repository).Kind;

    private static string ResolveProfileSource(ModuleRepositoryProfile profile, string profileName, bool publish)
    {
        var source = publish
            ? FirstNonEmpty(profile.RepositoryPublishUri, profile.RepositoryUri, profile.RepositorySourceUri, profile.Repository, profile.RepositoryName, profileName)
            : FirstNonEmpty(profile.RepositoryUri, profile.RepositorySourceUri, profile.Repository, profile.RepositoryName, profileName);

        return source
            ?? throw new InvalidOperationException($"Profile '{profileName}' does not define a repository source.");
    }

    private static string ResolveProfileRepositoryName(ModuleRepositoryProfile profile, string profileName)
        => FirstNonEmpty(profile.RepositoryName, profileName) ?? profileName;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
