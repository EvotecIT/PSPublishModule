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

        foreach (var moduleDirectory in EnumerateDirectories(moduleRoot))
        {
            var existingName = Path.GetFileName(moduleDirectory);
            if (ManagedModuleInstallContext.IsManagedStageDirectory(existingName))
                continue;

            if (existingName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var existingModulePath in EnumerateCandidateModulePaths(moduleDirectory))
            {
                var existingManifest = FindModuleManifest(existingModulePath, existingName);
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

    private static IEnumerable<string> EnumerateCandidateModulePaths(string moduleDirectory)
    {
        yield return moduleDirectory;

        foreach (var versionDirectory in EnumerateDirectories(moduleDirectory))
        {
            var directoryName = Path.GetFileName(versionDirectory);
            if (ManagedModuleInstallContext.IsManagedStageDirectory(directoryName))
                continue;

            yield return versionDirectory;
        }
    }

    private static string? FindModuleManifest(string modulePath, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(modulePath) || !Directory.Exists(modulePath))
            return null;

        var expected = Path.Combine(modulePath, moduleName + ".psd1");
        if (File.Exists(expected))
            return expected;

        try
        {
            return Directory.GetFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly)
                .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.GetDirectories(path)
                : Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
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
