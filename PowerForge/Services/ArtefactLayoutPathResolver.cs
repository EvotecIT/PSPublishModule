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
        raw = raw.Trim();
        raw = raw.Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
            return Path.GetFullPath(Path.Combine(projectRoot, "Artefacts", type.ToString()));

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(projectRoot, raw));
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
        replaced = replaced.Trim();
        replaced = replaced.Trim('"');
        if (string.IsNullOrWhiteSpace(replaced))
            return outputRoot;

        var full = Path.IsPathRooted(replaced) ? replaced : Path.Combine(outputRoot, replaced);
        return Path.GetFullPath(full);
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
        replaced = replaced.Trim();
        replaced = replaced.Trim('"');
        if (string.IsNullOrWhiteSpace(replaced))
            return requiredModulesRoot;

        var full = Path.IsPathRooted(replaced) ? replaced : Path.Combine(outputRoot, replaced);
        return Path.GetFullPath(full);
    }

    internal static string NormalizeDeliveryInternalsPath(string? configuredPath)
    {
        var normalized = (configuredPath ?? string.Empty).Trim().Trim('"');
        return string.IsNullOrWhiteSpace(normalized) ? "Internals" : normalized;
    }
}
