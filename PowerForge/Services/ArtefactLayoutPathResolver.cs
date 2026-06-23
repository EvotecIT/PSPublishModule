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

    internal static string NormalizeDeliveryInternalsPath(string? configuredPath)
    {
        var normalized = PathValueResolver.Clean(configuredPath ?? string.Empty);
        return string.IsNullOrWhiteSpace(normalized) ? "Internals" : normalized;
    }
}
