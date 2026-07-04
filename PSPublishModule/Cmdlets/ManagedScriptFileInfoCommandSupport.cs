using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ManagedScriptFileInfoCommandSupport
{
    internal static ManagedScriptFileInfo CreateInfo(
        string path,
        string? version,
        string? author,
        string? description,
        Guid guid,
        string? companyName,
        string? copyright,
        Hashtable[]? requiredModules,
        string[]? externalModuleDependencies,
        string[]? requiredScripts,
        string[]? externalScriptDependencies,
        string[]? tags,
        string? projectUri,
        string? licenseUri,
        string? iconUri,
        string? releaseNotes,
        string? privateData)
        => new()
        {
            Path = path,
            Version = version ?? string.Empty,
            Author = author,
            Description = description,
            Guid = guid,
            CompanyName = companyName,
            Copyright = copyright,
            RequiredModules = ConvertRequiredModules(requiredModules),
            ExternalModuleDependencies = externalModuleDependencies ?? Array.Empty<string>(),
            RequiredScripts = requiredScripts ?? Array.Empty<string>(),
            ExternalScriptDependencies = externalScriptDependencies ?? Array.Empty<string>(),
            Tags = tags ?? Array.Empty<string>(),
            ProjectUri = projectUri,
            LicenseUri = licenseUri,
            IconUri = iconUri,
            ReleaseNotes = releaseNotes,
            PrivateData = privateData
        };

    internal static string ResolvePath(PSCmdlet cmdlet, string path)
        => cmdlet.SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

    private static IReadOnlyList<ManagedScriptRequiredModule> ConvertRequiredModules(Hashtable[]? requiredModules)
    {
        if (requiredModules is null || requiredModules.Length == 0)
            return Array.Empty<ManagedScriptRequiredModule>();

        var modules = new List<ManagedScriptRequiredModule>();
        foreach (var entry in requiredModules)
        {
            var moduleName = ConvertToString(entry, "ModuleName");
            if (string.IsNullOrWhiteSpace(moduleName))
                throw new ArgumentException("RequiredModules entries must include ModuleName.");

            modules.Add(new ManagedScriptRequiredModule
            {
                ModuleName = moduleName!,
                Guid = ConvertToString(entry, "Guid"),
                ModuleVersion = ConvertToString(entry, "ModuleVersion"),
                RequiredVersion = ConvertToString(entry, "RequiredVersion"),
                MaximumVersion = ConvertToString(entry, "MaximumVersion")
            });
        }

        return modules;
    }

    private static string? ConvertToString(Hashtable hashtable, string key)
    {
        foreach (DictionaryEntry entry in hashtable)
        {
            if (entry.Key is not null && string.Equals(entry.Key.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return entry.Value?.ToString();
        }

        return null;
    }
}
