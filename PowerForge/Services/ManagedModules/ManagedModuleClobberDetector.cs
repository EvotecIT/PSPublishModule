namespace PowerForge;

internal static class ManagedModuleClobberDetector
{
    public static void ThrowIfConflicts(string moduleRoot, string moduleName, string stagedModulePath)
    {
        var incomingManifest = FindModuleManifest(stagedModulePath, moduleName);
        if (incomingManifest is null)
            return;

        var incoming = ReadCommandExports(incomingManifest);
        if (incoming.Count == 0 || !Directory.Exists(moduleRoot))
            return;

        foreach (var moduleDirectory in Directory.EnumerateDirectories(moduleRoot))
        {
            var existingName = Path.GetFileName(moduleDirectory);
            if (existingName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var versionDirectory in Directory.EnumerateDirectories(moduleDirectory))
            {
                var existingManifest = FindModuleManifest(versionDirectory, existingName);
                if (existingManifest is null)
                    continue;

                var existing = ReadCommandExports(existingManifest);
                var conflict = incoming.Intersect(existing, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(conflict))
                {
                    throw new InvalidOperationException(
                        $"Managed module install detected export conflict '{conflict}' between '{moduleName}' and existing module '{existingName}'. Use AllowClobber to permit this.");
                }
            }
        }
    }

    private static string? FindModuleManifest(string modulePath, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(modulePath) || !Directory.Exists(modulePath))
            return null;

        var expected = Path.Combine(modulePath, moduleName + ".psd1");
        if (File.Exists(expected))
            return expected;

        return Directory.EnumerateFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static HashSet<string> ReadCommandExports(string manifestPath)
    {
        var exports = ModuleManifestExportReader.ReadExports(manifestPath);
        var names = exports.Functions
            .Concat(exports.Cmdlets)
            .Concat(exports.Aliases)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Where(static name => !name.Equals("*", StringComparison.Ordinal));

        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
}
