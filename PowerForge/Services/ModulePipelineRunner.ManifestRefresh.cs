using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void RefreshManifestFromPlan(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        ManifestEditor.RequiredModule[] manifestRequiredModules,
        string[] manifestExternalModuleDependencies)
    {
        var manifestPath = buildResult.ManifestPath;
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
            SetOrRemoveTopLevelString(manifestPath, "Prerelease", manifest.Prerelease, removeWhenEmpty: true);

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

            if (!string.IsNullOrWhiteSpace(plan.PreRelease))
                ManifestEditor.TrySetTopLevelString(manifestPath, "Prerelease", plan.PreRelease!);
        }

        // Keep dependency fields deterministic and avoid stale values from source PSD1.
        ManifestEditor.TrySetRequiredModules(manifestPath, manifestRequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>());
        ManifestEditor.TrySetPsDataStringArray(manifestPath, "ExternalModuleDependencies", NormalizeStringArray(manifestExternalModuleDependencies));

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

        if (plan.CommandModuleDependencies is { Count: > 0 })
            ManifestEditor.TrySetTopLevelHashtableStringArray(manifestPath, "CommandModuleDependencies", plan.CommandModuleDependencies);
        else
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
}
