using System.IO;

namespace PowerForge;

/// <summary>
/// Shared artefact layout path resolution helpers used by packaging and plan-time validation.
/// </summary>
internal static class ArtefactLayoutPathResolver
{
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

    private static bool IsSameOrChildPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
