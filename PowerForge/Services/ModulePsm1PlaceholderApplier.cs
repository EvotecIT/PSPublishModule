using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PowerForge;

internal static class ModulePsm1PlaceholderApplier
{
    internal static void Apply(
        ILogger logger,
        string psm1Path,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<PlaceHolderReplacement>? replacements,
        PlaceHolderOptionConfiguration? options)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));
        if (string.IsNullOrWhiteSpace(psm1Path) || !File.Exists(psm1Path))
            return;

        var resolvedReplacements = BuildReplacements(
            moduleName,
            moduleVersion,
            preRelease,
            replacements,
            skipBuiltinReplacements: options?.SkipBuiltinReplacements == true);
        if (resolvedReplacements.Count == 0)
            return;

        string content;
        try
        {
            content = File.ReadAllText(psm1Path);
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to read PSM1 for placeholder replacement: {ex.Message}");
            return;
        }

        var updated = content;
        foreach (var replacement in resolvedReplacements)
        {
            if (string.IsNullOrEmpty(replacement.Find))
                continue;

            updated = updated.Replace(replacement.Find, replacement.Replace ?? string.Empty);
        }

        if (string.Equals(content, updated, StringComparison.Ordinal))
            return;

        try
        {
            File.WriteAllText(psm1Path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to write PSM1 after placeholder replacement: {ex.Message}");
        }
    }

    internal static IReadOnlyList<(string Find, string Replace)> BuildReplacements(
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<PlaceHolderReplacement>? replacements,
        bool skipBuiltinReplacements)
    {
        moduleName ??= string.Empty;
        moduleVersion ??= string.Empty;

        var resolved = new List<(string Find, string Replace)>();
        if (!skipBuiltinReplacements)
        {
            var moduleVersionWithPreRelease = ModulePathTokenFormatter.FormatVersionWithPreRelease(moduleVersion, preRelease);
            var tagName = "v" + moduleVersion;
            var tagModuleVersionWithPreRelease = "v" + moduleVersionWithPreRelease;

            resolved.Add(("{ModuleName}", moduleName));
            resolved.Add(("<ModuleName>", moduleName));
            resolved.Add(("{ModuleVersion}", moduleVersion));
            resolved.Add(("<ModuleVersion>", moduleVersion));
            resolved.Add(("{ModuleVersionWithPreRelease}", moduleVersionWithPreRelease));
            resolved.Add(("<ModuleVersionWithPreRelease>", moduleVersionWithPreRelease));
            resolved.Add(("{TagModuleVersionWithPreRelease}", tagModuleVersionWithPreRelease));
            resolved.Add(("<TagModuleVersionWithPreRelease>", tagModuleVersionWithPreRelease));
            resolved.Add(("{TagName}", tagName));
            resolved.Add(("<TagName>", tagName));
        }

        if (replacements is { Count: > 0 })
        {
            foreach (var entry in replacements)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Find))
                    continue;

                resolved.Add((entry.Find, entry.Replace ?? string.Empty));
            }
        }

        return resolved;
    }
}
