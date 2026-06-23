using System.IO;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Resolves user-authored filesystem paths while accepting either slash style on every supported host.
/// </summary>
internal static class PathValueResolver
{
    internal static string NormalizeSeparators(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? value.Replace('/', '\\')
            : value.Replace('\\', '/');
    }

    internal static string Clean(string value)
        => NormalizeSeparators(value.Trim().Trim('"'));

    internal static string Resolve(string basePath, string path)
    {
        var cleaned = Clean(path);
        return Path.GetFullPath(Path.IsPathRooted(cleaned)
            ? cleaned
            : Path.Combine(basePath, cleaned));
    }

    internal static string? ResolveNullable(string basePath, string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : Resolve(basePath, path!);
}
