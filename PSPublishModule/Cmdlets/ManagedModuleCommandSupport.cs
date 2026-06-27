using System;
using System.IO;
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

    private static ManagedModuleRepositoryKind GetRepositoryKind(string repository)
        => new ManagedModuleRepository("Repository", repository).Kind;
}
