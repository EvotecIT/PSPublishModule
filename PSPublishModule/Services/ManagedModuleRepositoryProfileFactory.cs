using System;
using System.Collections;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ManagedModuleRepositoryProfileFactory
{
    private const int DefaultPSResourceGetPriority = 50;

    internal static ModuleRepositoryProfile CreateNuGetProfile(
        string name,
        string uri,
        bool trusted,
        int? priority,
        RepositoryApiVersion apiVersion = RepositoryApiVersion.Auto)
    {
        if (apiVersion == RepositoryApiVersion.ContainerRegistry)
            throw new NotSupportedException("ContainerRegistry repository API version requires Microsoft Artifact Registry onboarding through Initialize-ManagedModuleRepository -MicrosoftArtifactRegistry.");

        var normalizedPriority = ResolvePSResourceGetPriority(priority);
        var resolvedApiVersion = ResolveApiVersion(uri, apiVersion);
        if (TryCreateAzureArtifactsProfile(name, uri, trusted, normalizedPriority, resolvedApiVersion, out var azureProfile))
            return azureProfile;

        return ModuleRepositoryProfileStore.Normalize(new ModuleRepositoryProfile
        {
            Name = name,
            Provider = PrivateGalleryProvider.NuGet,
            Repository = name,
            RepositoryName = name,
            RepositoryUri = uri,
            RepositorySourceUri = uri,
            RepositoryPublishUri = uri,
            Tool = RepositoryRegistrationTool.PSResourceGet,
            BootstrapMode = PrivateGalleryBootstrapMode.CredentialPrompt,
            Trusted = trusted,
            Priority = normalizedPriority,
            ApiVersion = resolvedApiVersion,
            AuthenticationMode = "CredentialPrompt"
        });
    }

    internal static ModuleRepositoryProfile CreatePowerShellGalleryProfile(bool trusted, int? priority)
        => CreateNuGetProfile(
            ManagedModuleCommandSupport.DefaultRepositoryName,
            ManagedModuleCommandSupport.DefaultRepositorySource,
            trusted,
            priority);

    internal static ModuleRepositoryProfile[] CreateFromRepositoryHashtable(Hashtable repository)
    {
        if (repository is null) throw new ArgumentNullException(nameof(repository));

        if (TryGetSwitch(repository, "PSGallery"))
            return new[] { CreatePowerShellGalleryProfile(TryGetSwitch(repository, "Trusted"), TryGetNullableInt(repository, "Priority")) };

        var name = GetRequiredString(repository, "Name");
        var uri = GetRequiredString(repository, "Uri", "RepositoryUri");
        return new[]
        {
            CreateNuGetProfile(
                name,
                uri,
                TryGetSwitch(repository, "Trusted"),
                TryGetNullableInt(repository, "Priority"),
                TryGetApiVersion(repository))
        };
    }

    private static string GetRequiredString(Hashtable table, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetValue(table, key, out var value) && value is not null)
            {
                var text = LanguagePrimitives.ConvertTo<string>(value);
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }

        throw new ArgumentException($"Repository hashtable requires '{string.Join("' or '", keys)}'.");
    }

    private static bool TryGetSwitch(Hashtable table, string key)
    {
        if (!TryGetValue(table, key, out var value) || value is null)
            return false;

        return LanguagePrimitives.ConvertTo<bool>(value);
    }

    private static int? TryGetNullableInt(Hashtable table, string key)
    {
        if (!TryGetValue(table, key, out var value) || value is null)
            return null;

        return LanguagePrimitives.ConvertTo<int>(value);
    }

    private static RepositoryApiVersion TryGetApiVersion(Hashtable table)
    {
        if (!TryGetValue(table, "ApiVersion", out var value) || value is null)
            return RepositoryApiVersion.Auto;

        return LanguagePrimitives.ConvertTo<RepositoryApiVersion>(value);
    }

    private static int ResolvePSResourceGetPriority(int? priority)
    {
        var value = priority ?? DefaultPSResourceGetPriority;
        if (value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(priority), value, "Repository priority must be between 0 and 100.");

        return value;
    }

    private static RepositoryApiVersion ResolveApiVersion(string uri, RepositoryApiVersion apiVersion)
        => apiVersion == RepositoryApiVersion.Auto && IsV2RepositoryUri(uri)
            ? RepositoryApiVersion.V2
            : apiVersion;

    private static bool TryCreateAzureArtifactsProfile(
        string name,
        string uri,
        bool trusted,
        int priority,
        RepositoryApiVersion apiVersion,
        out ModuleRepositoryProfile profile)
    {
        if (TryParseAzureArtifactsUri(uri, out var organization, out var project, out var feed))
        {
            profile = ModuleRepositoryProfileStore.Normalize(new ModuleRepositoryProfile
            {
                Name = name,
                Provider = PrivateGalleryProvider.AzureArtifacts,
                AzureDevOpsOrganization = organization,
                AzureDevOpsProject = project,
                AzureArtifactsFeed = feed,
                Repository = feed,
                RepositoryName = name,
                Tool = RepositoryRegistrationTool.PSResourceGet,
                BootstrapMode = PrivateGalleryBootstrapMode.ExistingSession,
                Trusted = trusted,
                Priority = priority,
                ApiVersion = apiVersion,
                AuthenticationMode = "AzureArtifactsCredentialProvider"
            });
            return true;
        }

        profile = null!;
        return false;
    }

    private static bool TryParseAzureArtifactsUri(
        string uri,
        out string organization,
        out string? project,
        out string feed)
    {
        organization = string.Empty;
        project = null;
        feed = string.Empty;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;

        var host = parsed.Host;
        var segments = parsed.AbsolutePath
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var packagingIndex = Array.FindIndex(segments, segment => string.Equals(segment, "_packaging", StringComparison.OrdinalIgnoreCase));
        if (packagingIndex < 0 || packagingIndex + 1 >= segments.Length)
            return false;

        if (string.Equals(host, "pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            if (packagingIndex is not (1 or 2))
                return false;

            organization = Uri.UnescapeDataString(segments[0]);
            project = packagingIndex == 2 ? Uri.UnescapeDataString(segments[1]) : null;
            feed = Uri.UnescapeDataString(segments[packagingIndex + 1]);
            return !string.IsNullOrWhiteSpace(organization) && !string.IsNullOrWhiteSpace(feed);
        }

        if (!host.EndsWith(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase))
            return false;

        organization = host.Substring(0, host.Length - ".pkgs.visualstudio.com".Length);
        project = packagingIndex == 1 ? Uri.UnescapeDataString(segments[0]) : null;
        feed = Uri.UnescapeDataString(segments[packagingIndex + 1]);
        return !string.IsNullOrWhiteSpace(organization) && !string.IsNullOrWhiteSpace(feed);
    }

    private static bool IsV2RepositoryUri(string uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
           parsed.AbsolutePath.TrimEnd('/').EndsWith("/v2", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetValue(Hashtable table, string key, out object? value)
    {
        foreach (DictionaryEntry entry in table)
        {
            if (entry.Key is not null &&
                string.Equals(entry.Key.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
