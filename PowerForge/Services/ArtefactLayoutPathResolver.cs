using System.IO;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Shared artefact layout path resolution helpers used by packaging and plan-time validation.
/// </summary>
internal static class ArtefactLayoutPathResolver
{
    internal static string ResolveScriptName(
        string? configuredName,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var replaced = ModulePathTokenFormatter.ReplacePathTokens(
            configuredName,
            moduleName,
            moduleVersion,
            preRelease).Trim();
        var scriptName = string.IsNullOrWhiteSpace(replaced) ? moduleName + ".ps1" : replaced;
        if (!scriptName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            scriptName += ".ps1";

        if (!IsPortableFileName(scriptName))
            throw new InvalidOperationException($"ScriptName must be a file name, but got '{scriptName}'.");

        return scriptName;
    }

    internal static string ResolveArtefactFileName(
        ArtefactConfiguration cfg,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        string fileName;
        if (!string.IsNullOrWhiteSpace(cfg.ArtefactName))
        {
            fileName = ModulePathTokenFormatter.ReplacePathTokens(
                cfg.ArtefactName!.Trim(),
                moduleName,
                moduleVersion,
                preRelease);
        }
        else
        {
            var tagWithPre = ModulePathTokenFormatter.ReplacePathTokens(
                "<TagModuleVersionWithPreRelease>",
                moduleName,
                moduleVersion,
                preRelease);
            fileName = cfg.IncludeTagName == true
                ? $"{moduleName}.{tagWithPre}.zip"
                : $"{moduleName}.zip";
        }

        fileName = fileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.IsPathRooted(fileName) ||
            fileName.IndexOf('/') >= 0 ||
            fileName.IndexOf('\\') >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !IsPortableFileName(fileName))
        {
            throw new InvalidOperationException(
                $"ArtefactName must be a file name that stays inside the artefact output root, but got '{fileName}'.");
        }

        return fileName;
    }

    private static bool IsPortableFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.IsPathRooted(fileName) ||
            fileName.IndexOf('/') >= 0 ||
            fileName.IndexOf('\\') >= 0 ||
            fileName.EndsWith(".", StringComparison.Ordinal) ||
            fileName.EndsWith(" ", StringComparison.Ordinal) ||
            fileName.Any(character =>
                character < 32 ||
                character is '<' or '>' or ':' or '"' or '|' or '?' or '*' ||
                Path.GetInvalidFileNameChars().Contains(character)))
        {
            return false;
        }

        string stem = fileName.Split('.')[0].TrimEnd(' ', '.');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(stem.Length == 4 &&
                 (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 IsWindowsDeviceNumber(stem[3]));
    }

    private static bool IsWindowsDeviceNumber(char character)
        => character is >= '1' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3';

    internal static string ResolveOutputRoot(
        string? configuredPath,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        ArtefactType type)
    {
        var raw = ModulePathTokenFormatter.ReplacePathTokens(configuredPath, moduleName, moduleVersion, preRelease);
        raw = PathValueResolver.Clean(raw);
        if (string.IsNullOrWhiteSpace(raw))
            return Path.GetFullPath(Path.Combine(projectRoot, "Artefacts", type.ToString()));

        return PathValueResolver.Resolve(projectRoot, raw);
    }

    internal static string ResolveRequiredModulesRootForUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var path = cfg.RequiredModules.Path;
        if (string.IsNullOrWhiteSpace(path))
            return outputRoot;

        var replaced = ModulePathTokenFormatter.ReplacePathTokens(path, moduleName, moduleVersion, preRelease);
        replaced = PathValueResolver.Clean(replaced);
        if (string.IsNullOrWhiteSpace(replaced))
            return outputRoot;

        return PathValueResolver.Resolve(outputRoot, replaced);
    }

    internal static string ResolveModulesRootForUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string requiredModulesRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var path = cfg.RequiredModules.ModulesPath;
        if (string.IsNullOrWhiteSpace(path))
            return requiredModulesRoot;

        var replaced = ModulePathTokenFormatter.ReplacePathTokens(path, moduleName, moduleVersion, preRelease);
        replaced = PathValueResolver.Clean(replaced);
        if (string.IsNullOrWhiteSpace(replaced))
            return requiredModulesRoot;

        return PathValueResolver.Resolve(outputRoot, replaced);
    }

    internal static string ResolveRequiredModulesRootForPacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string packedRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
        => ResolvePackedLayoutRoot(
            cfg.RequiredModules.Path,
            outputRoot,
            packedRoot,
            packedRoot,
            moduleName,
            moduleVersion,
            preRelease);

    internal static string ResolveModulesRootForPacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string packedRoot,
        string requiredModulesRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
        => ResolvePackedLayoutRoot(
            cfg.RequiredModules.ModulesPath,
            outputRoot,
            packedRoot,
            requiredModulesRoot,
            moduleName,
            moduleVersion,
            preRelease);

    internal static string ResolveScriptPackedEntryPointRelativePath(
        ArtefactConfiguration cfg,
        string outputRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        string packedRoot = Path.Combine(outputRoot, ".powerforge-packed-layout");
        string requiredRoot = ResolveRequiredModulesRootForPacked(
            cfg,
            outputRoot,
            packedRoot,
            moduleName,
            moduleVersion,
            preRelease);
        string modulesRoot = ResolveModulesRootForPacked(
            cfg,
            outputRoot,
            packedRoot,
            requiredRoot,
            moduleName,
            moduleVersion,
            preRelease);
        string scriptPath = Path.Combine(
            modulesRoot,
            ResolveScriptName(cfg.ScriptName, moduleName, moduleVersion, preRelease));
        return FrameworkCompatibility.GetRelativePath(packedRoot, scriptPath)
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static string ResolvePackedLayoutRoot(
        string? configuredPath,
        string outputRoot,
        string packedRoot,
        string defaultRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var raw = ModulePathTokenFormatter.ReplacePathTokens(configuredPath ?? string.Empty, moduleName, moduleVersion, preRelease);
        raw = PathValueResolver.Clean(raw);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultRoot;

        if (!Path.IsPathRooted(raw))
        {
            var resolved = Path.GetFullPath(Path.Combine(packedRoot, raw));
            if (!IsSameOrChildPath(packedRoot, resolved))
            {
                throw new InvalidOperationException(
                    $"Packed artefact module path '{raw}' resolves outside the temporary packed artefact root '{Path.GetFullPath(packedRoot)}'. Use a path that stays within the packed artefact payload.");
            }

            return resolved;
        }

        var rooted = Path.GetFullPath(raw);
        var resolvedOutputRoot = Path.GetFullPath(outputRoot);
        if (!IsSameOrChildPath(resolvedOutputRoot, rooted))
        {
            throw new InvalidOperationException(
                $"Packed artefact module paths must resolve under artefact output '{resolvedOutputRoot}', but got '{rooted}'.");
        }

        var relative = FrameworkCompatibility.GetRelativePath(resolvedOutputRoot, rooted);
        if (string.IsNullOrWhiteSpace(relative) || relative == ".")
            return packedRoot;

        var mapped = Path.GetFullPath(Path.Combine(packedRoot, relative));
        if (!IsSameOrChildPath(packedRoot, mapped))
        {
            throw new InvalidOperationException(
                $"Packed artefact module path '{raw}' resolves outside the temporary packed artefact root '{Path.GetFullPath(packedRoot)}'. Use a path that stays within the packed artefact payload.");
        }

        return mapped;
    }

    internal static string NormalizeDeliveryInternalsPath(string? configuredPath)
    {
        var normalized = PathValueResolver.Clean(configuredPath ?? string.Empty);
        return string.IsNullOrWhiteSpace(normalized) ? "Internals" : normalized;
    }

    internal static void ValidateFinalizedScriptLayout(
        ArtefactConfiguration cfg,
        string outputRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        string fullOutputRoot = Path.GetFullPath(outputRoot);
        string requiredModulesRoot = ResolveRequiredModulesRootForUnpacked(
            cfg,
            fullOutputRoot,
            moduleName,
            moduleVersion,
            preRelease);
        string modulesRoot = ResolveModulesRootForUnpacked(
            cfg,
            fullOutputRoot,
            requiredModulesRoot,
            moduleName,
            moduleVersion,
            preRelease);

        EnsureFinalizedScriptPathIsContained(fullOutputRoot, modulesRoot, "main script root");
        if (cfg.RequiredModules.Enabled == true)
            EnsureFinalizedScriptPathIsContained(fullOutputRoot, requiredModulesRoot, "required modules root");

        ValidateFinalizedScriptCopyMappings(
            cfg.DirectoryOutput,
            cfg.DestinationDirectoriesRelative == true,
            "directory copy destination",
            fullOutputRoot,
            moduleName,
            moduleVersion,
            preRelease);
        ValidateFinalizedScriptCopyMappings(
            cfg.FilesOutput,
            cfg.DestinationFilesRelative == true,
            "file copy destination",
            fullOutputRoot,
            moduleName,
            moduleVersion,
            preRelease);
    }

    private static void ValidateFinalizedScriptCopyMappings(
        IEnumerable<ArtefactCopyMapping>? mappings,
        bool relativeToRoot,
        string label,
        string outputRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        foreach (ArtefactCopyMapping mapping in mappings ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Destination))
                continue;

            string raw = ModulePathTokenFormatter.ReplacePathTokens(
                    mapping.Destination,
                    moduleName,
                    moduleVersion,
                    preRelease)
                .Trim()
                .Trim('"');
            string destination = relativeToRoot || !Path.IsPathRooted(raw)
                ? Path.GetFullPath(Path.Combine(outputRoot, raw))
                : Path.GetFullPath(raw);
            EnsureFinalizedScriptPathIsContained(outputRoot, destination, label);
        }
    }

    private static void EnsureFinalizedScriptPathIsContained(string outputRoot, string candidatePath, string label)
    {
        if (IsSameOrChildPath(outputRoot, candidatePath))
            return;

        throw new InvalidOperationException(
            $"A signed Script artefact must keep its complete finalized layout under output root '{Path.GetFullPath(outputRoot)}', " +
            $"but the {label} resolves outside it to '{Path.GetFullPath(candidatePath)}'.");
    }

    private static bool IsSameOrChildPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        StringComparison comparison =
            FrameworkCompatibility.GetPathStringComparisonForPath(root) == StringComparison.OrdinalIgnoreCase ||
            FrameworkCompatibility.GetPathStringComparisonForPath(candidate) == StringComparison.OrdinalIgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (string.Equals(root, candidate, comparison))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, comparison);
    }
}
