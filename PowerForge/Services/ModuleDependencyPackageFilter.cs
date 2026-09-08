using System;
using System.Linq;

namespace PowerForge;

internal static class ModuleDependencyPackageFilter
{
    internal static bool IsBuildHostMetadataPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var parts = relativePath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(static part => part.Equals(".powerforge", StringComparison.OrdinalIgnoreCase)) ||
               (parts.Length > 0 && parts[parts.Length - 1].Equals("PSGetModuleInfo.xml", StringComparison.OrdinalIgnoreCase));
    }
}
