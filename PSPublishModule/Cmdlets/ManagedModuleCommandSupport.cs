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
        var source = ResolveRepositorySource(cmdlet, repository, out var resolvedRegisteredRepositoryName, out var resolvedRegisteredRepositoryTrusted);
        var name = !string.Equals(repositoryName, DefaultRepositoryName, StringComparison.OrdinalIgnoreCase)
            ? repositoryName
            : !string.IsNullOrWhiteSpace(resolvedRegisteredRepositoryName)
                ? resolvedRegisteredRepositoryName!
                : ResolveRepositoryName(repositoryName, source);
        var trusted = resolvedRegisteredRepositoryName is not null
            ? resolvedRegisteredRepositoryTrusted
            : IsBuiltInDefaultRepository(repositoryName, source);
        return new ManagedModuleRepository(name, source, ManagedModuleRepositoryKind.Auto, trusted);
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

    internal static ManagedModuleRepository CreateScriptRepository(
        PSCmdlet cmdlet,
        string repositoryName,
        string repository,
        string? profileName,
        bool repositoryWasBound)
    {
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            if (repositoryWasBound)
                throw new InvalidOperationException("Specify either ProfileName or Repository, not both.");

            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(profileName!);
            var source = FirstNonEmpty(profile.RepositoryUri, profile.RepositorySourceUri, profile.Repository, profile.RepositoryName, profileName)
                ?? throw new InvalidOperationException($"Profile '{profileName}' does not define a repository source.");
            return CreateScriptRepositoryFromSource(
                cmdlet,
                ResolveProfileRepositoryName(profile, profileName!),
                source,
                profile.Trusted);
        }

        var resolvedSource = ResolveScriptRepositorySource(cmdlet, repository, out var resolvedRegisteredRepositoryName, out var resolvedRegisteredRepositoryTrusted);
        var name = !string.Equals(repositoryName, DefaultRepositoryName, StringComparison.OrdinalIgnoreCase)
            ? repositoryName
            : !string.IsNullOrWhiteSpace(resolvedRegisteredRepositoryName)
                ? resolvedRegisteredRepositoryName!
                : ResolveRepositoryName(repositoryName, resolvedSource);
        var trusted = resolvedRegisteredRepositoryName is not null
            ? resolvedRegisteredRepositoryTrusted
            : IsBuiltInDefaultRepository(repositoryName, resolvedSource);
        return new ManagedModuleRepository(name, resolvedSource, ManagedModuleRepositoryKind.Auto, trusted);
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
                ResolveRepositoryPublishSource(cmdlet, source, out _, out _),
                ManagedModuleRepositoryKind.Auto,
                profile.Trusted);
        }

        if (!string.IsNullOrWhiteSpace(repository))
        {
            var source = ResolveRepositoryPublishSource(cmdlet, repository!, out var resolvedRegisteredRepositoryName, out var resolvedRegisteredRepositoryTrusted);
            var name = !string.Equals(repositoryName, DefaultRepositoryName, StringComparison.OrdinalIgnoreCase)
                ? repositoryName
                : !string.IsNullOrWhiteSpace(resolvedRegisteredRepositoryName)
                    ? resolvedRegisteredRepositoryName!
                    : ResolveRepositoryName(repositoryName, source);
            var trusted = resolvedRegisteredRepositoryName is not null
                ? resolvedRegisteredRepositoryTrusted
                : IsBuiltInDefaultRepository(repositoryName, source);
            return new ManagedModuleRepository(name, source, ManagedModuleRepositoryKind.Auto, trusted);
        }

        if (!string.IsNullOrWhiteSpace(outputDirectory))
            return new ManagedModuleRepository("Local", ResolveProviderPath(cmdlet, outputDirectory)!);

        throw new ArgumentException("Specify Repository, OutputDirectory, or ProfileName.");
    }

    internal static ManagedModuleRepository CreatePublishReadRepository(
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
            var source = ResolveProfileReadSourceForPublish(profile, profileName!);
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

        if (GetRepositoryKind(repository) == ManagedModuleRepositoryKind.LocalFolder)
            return "Local";

        if (IsBuiltInDefaultRepository(repositoryName, repository))
            return repositoryName;

        return Uri.TryCreate(repository, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : "Repository";
    }

    internal static void ValidateSinglePackageHashTarget(string? expectedPackageSha256, string[] moduleNames)
    {
        if (string.IsNullOrWhiteSpace(expectedPackageSha256) || moduleNames.Length == 1)
            return;

        throw new InvalidOperationException("ExpectedPackageSha256 can only be used when exactly one module is targeted.");
    }

    internal static bool ResolveForce(bool force, bool reinstall)
        => force || reinstall;

    internal static void ValidateClobberSwitches(bool allowClobber, bool noClobber)
    {
        if (allowClobber && noClobber)
            throw new InvalidOperationException("Specify either AllowClobber or NoClobber, not both.");
    }

    internal static bool ShouldWriteSummary(bool showSummary, bool quiet)
        => showSummary && !quiet;

    internal static string ResolveRepositorySource(PSCmdlet cmdlet, string repository)
        => ResolveRepositorySource(cmdlet, repository, out _);

    internal static string ResolveRepositorySource(PSCmdlet cmdlet, string repository, out string? resolvedRegisteredRepositoryName)
        => ResolveRepositorySource(cmdlet, repository, out resolvedRegisteredRepositoryName, out _);

    internal static string ResolveRepositoryPublishSource(PSCmdlet cmdlet, string repository, out string? resolvedRegisteredRepositoryName, out bool resolvedRegisteredRepositoryTrusted)
        => ResolveRepositorySource(cmdlet, repository, publish: true, out resolvedRegisteredRepositoryName, out resolvedRegisteredRepositoryTrusted);

    internal static string ResolveScriptRepositorySource(
        PSCmdlet cmdlet,
        string repository,
        out string? resolvedRegisteredRepositoryName,
        out bool resolvedRegisteredRepositoryTrusted)
        => ResolveRepositorySource(cmdlet, repository, publish: false, script: true, out resolvedRegisteredRepositoryName, out resolvedRegisteredRepositoryTrusted);

    internal static string ResolveRepositorySource(
        PSCmdlet cmdlet,
        string repository,
        out string? resolvedRegisteredRepositoryName,
        out bool resolvedRegisteredRepositoryTrusted)
        => ResolveRepositorySource(cmdlet, repository, publish: false, out resolvedRegisteredRepositoryName, out resolvedRegisteredRepositoryTrusted);

    private static ManagedModuleRepository CreateScriptRepositoryFromSource(
        PSCmdlet cmdlet,
        string repositoryName,
        string source,
        bool trusted)
    {
        var resolvedSource = ResolveScriptRepositorySource(cmdlet, source, out var resolvedRegisteredRepositoryName, out var resolvedRegisteredRepositoryTrusted);
        return new ManagedModuleRepository(
            !string.IsNullOrWhiteSpace(resolvedRegisteredRepositoryName) ? resolvedRegisteredRepositoryName! : repositoryName,
            resolvedSource,
            ManagedModuleRepositoryKind.Auto,
            resolvedRegisteredRepositoryName is not null ? resolvedRegisteredRepositoryTrusted : trusted);
    }

    private static string ResolveRepositorySource(
        PSCmdlet cmdlet,
        string repository,
        bool publish,
        out string? resolvedRegisteredRepositoryName,
        out bool resolvedRegisteredRepositoryTrusted)
        => ResolveRepositorySource(cmdlet, repository, publish, script: false, out resolvedRegisteredRepositoryName, out resolvedRegisteredRepositoryTrusted);

    private static string ResolveRepositorySource(
        PSCmdlet cmdlet,
        string repository,
        bool publish,
        bool script,
        out string? resolvedRegisteredRepositoryName,
        out bool resolvedRegisteredRepositoryTrusted)
    {
        resolvedRegisteredRepositoryName = null;
        resolvedRegisteredRepositoryTrusted = false;
        if (string.IsNullOrWhiteSpace(repository))
            return repository;

        var trimmed = repository.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
                return uri.LocalPath;

            return trimmed;
        }

        if (string.Equals(trimmed, DefaultRepositoryName, StringComparison.OrdinalIgnoreCase))
            return DefaultRepositorySource;

        var providerPath = ResolveProviderPath(cmdlet, trimmed);
        if (!string.IsNullOrWhiteSpace(providerPath) && Directory.Exists(providerPath))
            return providerPath!;

        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith(".", StringComparison.Ordinal) || LooksLikeLocalPath(trimmed))
            return providerPath ?? trimmed;

        var resolver = new PowerShellRepositorySourceResolver();
        string? registeredSource;
        bool registeredTrusted;
        var resolved = script
            ? resolver.TryResolveScriptSource(cmdlet, trimmed, out registeredSource, out registeredTrusted)
            : publish
            ? resolver.TryResolvePublishSource(cmdlet, trimmed, out registeredSource, out registeredTrusted)
            : resolver.TryResolveSource(cmdlet, trimmed, out registeredSource, out registeredTrusted);
        if (resolved &&
            !string.IsNullOrWhiteSpace(registeredSource))
        {
            resolvedRegisteredRepositoryName = trimmed;
            resolvedRegisteredRepositoryTrusted = registeredTrusted;
            return registeredSource!;
        }

        if (LooksLikeRepositoryName(trimmed))
            throw new InvalidOperationException(
                $"Repository '{trimmed}' looks like a registered PowerShell repository name, but no matching repository was found in the current session. Use Set-ManagedModuleRepository/Initialize-ManagedModuleRepository with -ProfileName, or pass a repository URL/local feed path.");

        return trimmed;
    }

    internal static bool HasWildcard(string value)
        => value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;

    private static bool LooksLikeRepositoryName(string repository)
        => repository.IndexOfAny(new[] { '/', '\\', ':', '?' }) < 0;

    private static bool LooksLikeLocalPath(string repository)
        => repository.IndexOfAny(new[] { '/', '\\' }) >= 0;

    private static bool IsBuiltInDefaultRepository(string repositoryName, string source)
        => string.Equals(repositoryName, DefaultRepositoryName, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(source, DefaultRepositorySource, StringComparison.OrdinalIgnoreCase);

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

    private static string ResolveProfileReadSourceForPublish(ModuleRepositoryProfile profile, string profileName)
    {
        var source = FirstNonEmpty(profile.RepositorySourceUri, profile.RepositoryUri, profile.Repository, profile.RepositoryName, profileName);

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
