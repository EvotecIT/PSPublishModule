using System;
using System.Collections;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ManagedModuleRepositoryProfileFactory
{
    internal static ModuleRepositoryProfile CreateNuGetProfile(
        string name,
        string uri,
        bool trusted,
        int? priority,
        RepositoryApiVersion apiVersion = RepositoryApiVersion.Auto)
    {
        if (apiVersion == RepositoryApiVersion.ContainerRegistry)
            throw new NotSupportedException("ContainerRegistry repository API version requires Microsoft Artifact Registry onboarding through Initialize-ManagedModuleRepository -MicrosoftArtifactRegistry.");

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
            Priority = priority,
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
