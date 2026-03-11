using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void RefreshManifestFromPlan(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        RequiredModuleReference[] manifestRequiredModules,
        string[] manifestExternalModuleDependencies)
    {
        RefreshManifestPathFromPlan(
            plan,
            buildResult.ManifestPath,
            manifestRequiredModules,
            manifestExternalModuleDependencies);
    }

    private void RefreshProjectManifestFromPlan(
        ModulePipelinePlan plan,
        string manifestPath)
    {
        RefreshManifestPathFromPlan(
            plan,
            manifestPath,
            plan.RequiredModules ?? Array.Empty<RequiredModuleReference>(),
            plan.ExternalModuleDependencies ?? Array.Empty<string>());
    }

    private void RefreshManifestPathFromPlan(
        ModulePipelinePlan plan,
        string manifestPath,
        RequiredModuleReference[] manifestRequiredModules,
        string[] manifestExternalModuleDependencies)
    {
        var manifest = plan.Manifest;
        var hasManifestSegment = manifest is not null;

        ManifestEditor.TrySetTopLevelModuleVersion(manifestPath, plan.BuildSpec.Version);
        ManifestEditor.TrySetTopLevelString(manifestPath, "RootModule", $"{plan.ModuleName}.psm1");

        if (hasManifestSegment)
        {
            SetOrRemoveTopLevelString(manifestPath, "GUID", manifest!.Guid, removeWhenEmpty: true);
            SetOrRemoveTopLevelString(manifestPath, "Author", manifest.Author, removeWhenEmpty: true);
            SetOrRemoveTopLevelString(manifestPath, "CompanyName", manifest.CompanyName, removeWhenEmpty: true);
            SetOrRemoveTopLevelString(manifestPath, "Copyright", manifest.Copyright, removeWhenEmpty: true);
            SetOrRemoveTopLevelString(manifestPath, "Description", manifest.Description, removeWhenEmpty: true);
            SetOrRemoveTopLevelString(manifestPath, "PowerShellVersion", manifest.PowerShellVersion, removeWhenEmpty: true);
            SetOrRemoveTopLevelString(manifestPath, "DotNetFrameworkVersion", manifest.DotNetFrameworkVersion, removeWhenEmpty: true);
            SetOrRemovePsDataString(manifestPath, "Prerelease", manifest.Prerelease, removeWhenEmpty: true);

            if (manifest.CompatiblePSEditions is not null)
                ManifestEditor.TrySetTopLevelStringArray(manifestPath, "CompatiblePSEditions", NormalizeStringArray(manifest.CompatiblePSEditions));
            else
                ManifestEditor.TryRemoveTopLevelKey(manifestPath, "CompatiblePSEditions");

            if (manifest.FormatsToProcess is not null)
                ManifestEditor.TrySetTopLevelStringArray(manifestPath, "FormatsToProcess", NormalizeStringArray(manifest.FormatsToProcess));
            else
                ManifestEditor.TryRemoveTopLevelKey(manifestPath, "FormatsToProcess");

            if (manifest.FunctionsToExport is not null ||
                manifest.CmdletsToExport is not null ||
                manifest.AliasesToExport is not null)
            {
                BuildServices.SetManifestExports(
                    manifestPath,
                    functions: manifest.FunctionsToExport is null ? null : NormalizeStringArray(manifest.FunctionsToExport),
                    cmdlets: manifest.CmdletsToExport is null ? null : NormalizeStringArray(manifest.CmdletsToExport),
                    aliases: manifest.AliasesToExport is null ? null : NormalizeStringArray(manifest.AliasesToExport));
            }

            if (manifest.Tags is null)
                ManifestEditor.TryRemovePsDataKey(manifestPath, "Tags");
            else
                ManifestEditor.TrySetPsDataStringArray(manifestPath, "Tags", NormalizeStringArray(manifest.Tags));

            SetOrRemovePsDataString(manifestPath, "IconUri", manifest.IconUri, removeWhenEmpty: true);
            SetOrRemovePsDataString(manifestPath, "ProjectUri", manifest.ProjectUri, removeWhenEmpty: true);
            SetOrRemovePsDataString(manifestPath, "LicenseUri", manifest.LicenseUri, removeWhenEmpty: true);
            ManifestEditor.TrySetPsDataBool(manifestPath, "RequireLicenseAcceptance", manifest.RequireLicenseAcceptance);
        }
        else
        {
            if (plan.CompatiblePSEditions is { Length: > 0 })
                ManifestEditor.TrySetTopLevelStringArray(manifestPath, "CompatiblePSEditions", NormalizeStringArray(plan.CompatiblePSEditions));

            SetOrRemoveTopLevelString(manifestPath, "Author", plan.BuildSpec.Author, removeWhenEmpty: false);
            SetOrRemoveTopLevelString(manifestPath, "CompanyName", plan.BuildSpec.CompanyName, removeWhenEmpty: false);
            SetOrRemoveTopLevelString(manifestPath, "Description", plan.BuildSpec.Description, removeWhenEmpty: false);

            if (plan.BuildSpec.Tags is { Length: > 0 })
                ManifestEditor.TrySetPsDataStringArray(manifestPath, "Tags", NormalizeStringArray(plan.BuildSpec.Tags));
            SetOrRemovePsDataString(manifestPath, "IconUri", plan.BuildSpec.IconUri, removeWhenEmpty: false);
            SetOrRemovePsDataString(manifestPath, "ProjectUri", plan.BuildSpec.ProjectUri, removeWhenEmpty: false);

            SetOrRemovePsDataString(manifestPath, "Prerelease", plan.PreRelease, removeWhenEmpty: true);
        }

        // Prerelease belongs under PrivateData.PSData. Remove any stale top-level key left by older builds.
        ManifestEditor.TryRemoveTopLevelKey(manifestPath, "Prerelease");

        // Keep dependency fields deterministic and avoid stale values from source PSD1.
        var normalizedExternalModules = NormalizeExternalModuleDependencies(manifestExternalModuleDependencies);
        var normalizedRequiredModules = NormalizeRequiredModulesForManifest(
            manifestRequiredModules,
            normalizedExternalModules);
        ManifestEditor.TrySetRequiredModules(manifestPath, normalizedRequiredModules);
        ManifestEditor.TrySetPsDataStringArray(manifestPath, "ExternalModuleDependencies", normalizedExternalModules);

        var scriptsToProcess = Array.Empty<string>();
        if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "ScriptsToProcess", out var existingScripts) &&
            existingScripts is not null)
        {
            scriptsToProcess = existingScripts;
        }
        else if (ManifestEditor.TryGetTopLevelString(manifestPath, "ScriptsToProcess", out var singleScript) &&
                 !string.IsNullOrWhiteSpace(singleScript))
        {
            var normalizedScript = singleScript;
            if (normalizedScript is not null)
                scriptsToProcess = new[] { normalizedScript.Trim() };
        }

        // Normalize ScriptsToProcess layout by rewriting the key once using the current value.
        // This cleans up historical insertion formatting artifacts (extra blank line before the key).
        ManifestEditor.TryRemoveTopLevelKey(manifestPath, "ScriptsToProcess");
        ManifestEditor.TrySetTopLevelStringArray(manifestPath, "ScriptsToProcess", scriptsToProcess);

        // Keep command/module hints in the in-memory plan only.
        // Persisting them into the PSD1 breaks downstream Import-Module consumers such as the documentation engine.
        ManifestEditor.TryRemoveTopLevelKey(manifestPath, "CommandModuleDependencies");
    }

    private static void SetOrRemoveTopLevelString(string manifestPath, string key, string? value, bool removeWhenEmpty)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var normalized = value!.Trim();
            ManifestEditor.TrySetTopLevelString(manifestPath, key, normalized);
            return;
        }

        if (removeWhenEmpty)
            ManifestEditor.TryRemoveTopLevelKey(manifestPath, key);
    }

    private static void SetOrRemovePsDataString(string manifestPath, string key, string? value, bool removeWhenEmpty)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var normalized = value!.Trim();
            ManifestEditor.TrySetPsDataString(manifestPath, key, normalized);
            return;
        }

        if (removeWhenEmpty)
            ManifestEditor.TryRemovePsDataKey(manifestPath, key);
    }

    private static string[] NormalizeStringArray(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeExternalModuleDependencies(IEnumerable<string>? values)
    {
        return NormalizeStringArray(values)
            .Where(name => !ShouldSkipManifestDependencyModule(name))
            .ToArray();
    }

    private static RequiredModuleReference[] NormalizeRequiredModulesForManifest(
        IEnumerable<RequiredModuleReference>? modules,
        IReadOnlyCollection<string> externalModuleDependencies)
    {
        return (modules ?? Array.Empty<RequiredModuleReference>())
            .Where(module =>
                module is not null &&
                !string.IsNullOrWhiteSpace(module.ModuleName) &&
                !ShouldSkipManifestDependencyModule(module.ModuleName))
            .ToArray();
    }
}
