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
        string? privateData,
        bool requiredModulesSpecified = false,
        bool externalModuleDependenciesSpecified = false,
        bool requiredScriptsSpecified = false,
        bool externalScriptDependenciesSpecified = false,
        bool tagsSpecified = false,
        bool defaultAuthorWhenOmitted = false)
        => new()
        {
            Path = path,
            Version = version ?? string.Empty,
            Author = ResolveAuthor(author, defaultAuthorWhenOmitted),
            Description = description,
            Guid = guid,
            CompanyName = companyName,
            Copyright = copyright,
            RequiredModules = ConvertRequiredModules(requiredModules),
            RequiredModulesSpecified = requiredModulesSpecified,
            ExternalModuleDependencies = externalModuleDependencies ?? Array.Empty<string>(),
            ExternalModuleDependenciesSpecified = externalModuleDependenciesSpecified,
            RequiredScripts = requiredScripts ?? Array.Empty<string>(),
            RequiredScriptsSpecified = requiredScriptsSpecified,
            ExternalScriptDependencies = externalScriptDependencies ?? Array.Empty<string>(),
            ExternalScriptDependenciesSpecified = externalScriptDependenciesSpecified,
            Tags = tags ?? Array.Empty<string>(),
            TagsSpecified = tagsSpecified,
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
            var moduleVersion = ConvertToString(entry, "ModuleVersion");
            var requiredVersion = ConvertToString(entry, "RequiredVersion");
            var maximumVersion = ConvertToString(entry, "MaximumVersion");
            if (!string.IsNullOrWhiteSpace(requiredVersion) &&
                (!string.IsNullOrWhiteSpace(moduleVersion) || !string.IsNullOrWhiteSpace(maximumVersion)))
                throw new ArgumentException("RequiredModules entries cannot combine RequiredVersion with ModuleVersion or MaximumVersion.");

            modules.Add(new ManagedScriptRequiredModule
            {
                ModuleName = moduleName!,
                Guid = ConvertToString(entry, "Guid"),
                ModuleVersion = moduleVersion,
                RequiredVersion = requiredVersion,
                MaximumVersion = maximumVersion
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

    private static string? ResolveAuthor(string? author, bool defaultWhenOmitted)
        => string.IsNullOrWhiteSpace(author)
            ? defaultWhenOmitted ? Environment.UserName : null
            : author!.Trim();
}
