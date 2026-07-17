using System;
using System.IO;

namespace PowerForge;

internal static class ModuleStatePathIdentity
{
    internal static StringComparer Comparer { get; } = FrameworkCompatibility.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static StringComparison Comparison { get; } = FrameworkCompatibility.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        var pathRoot = Path.GetPathRoot(fullPath);
        var normalized = !string.IsNullOrWhiteSpace(pathRoot) &&
                         string.Equals(fullPath, pathRoot, Comparison)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized.Replace('\\', '/');
    }

    internal static bool Equals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);

        return string.Equals(Normalize(left!), Normalize(right!), Comparison);
    }

    internal static bool IsSameOrChild(string? path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        var normalizedPath = Normalize(path!);
        var normalizedRoot = Normalize(root!);
        var rootPrefix = normalizedRoot.EndsWith("/", StringComparison.Ordinal)
            ? normalizedRoot
            : normalizedRoot + "/";
        return string.Equals(normalizedPath, normalizedRoot, Comparison) ||
               normalizedPath.StartsWith(rootPrefix, Comparison);
    }

    internal static string CreatePlacementKey(
        string moduleName,
        string? powerShellEdition,
        string? scope,
        string? moduleRoot)
        => string.Join(
            "|",
            moduleName.Trim().ToUpperInvariant(),
            (powerShellEdition ?? string.Empty).Trim().ToUpperInvariant(),
            (scope ?? string.Empty).Trim().ToUpperInvariant(),
            NormalizeForKey(moduleRoot));

    internal static string CreateEstateKey(
        string? powerShellEdition,
        string? scope,
        string? moduleRoot,
        string? profileName = null)
        => string.Join(
            "|",
            (powerShellEdition ?? string.Empty).Trim().ToUpperInvariant(),
            (scope ?? string.Empty).Trim().ToUpperInvariant(),
            NormalizeForKey(moduleRoot),
            (profileName ?? string.Empty).Trim().ToUpperInvariant());

    internal static string? ResolveModuleRoot(ModuleStateInstalledModule module)
    {
        if (!string.IsNullOrWhiteSpace(module.ModuleRoot))
            return Normalize(module.ModuleRoot!);
        if (string.IsNullOrWhiteSpace(module.Path))
            return null;

        var moduleDirectory = new DirectoryInfo(module.Path!);
        if (string.Equals(moduleDirectory.Name, module.Name, StringComparison.OrdinalIgnoreCase))
            return moduleDirectory.Parent?.FullName;

        var parent = moduleDirectory.Parent;
        return parent is not null &&
               string.Equals(parent.Name, module.Name, StringComparison.OrdinalIgnoreCase)
            ? parent.Parent?.FullName
            : null;
    }

    private static string NormalizeForKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = Normalize(path!);
        return FrameworkCompatibility.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }
}
