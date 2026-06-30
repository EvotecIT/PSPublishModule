namespace PowerForge;

internal static class ManagedModuleClobberDetector
{
    public static void ThrowIfConflicts(string moduleRoot, string moduleName, string stagedModulePath)
    {
        var incomingManifest = FindModuleManifest(stagedModulePath, moduleName);
        if (incomingManifest is null)
            return;

        var incoming = ReadCommandExports(incomingManifest);
        if (!incoming.HasAnyExport && !Directory.Exists(moduleRoot))
            return;
        if (!Directory.Exists(moduleRoot))
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
                if (HasWildcardConflict(incoming, existing))
                {
                    throw new InvalidOperationException(
                        $"Managed module install detected wildcard export clobber risk between '{moduleName}' and existing module '{existingName}'. Use AllowClobber to permit this.");
                }

                var conflict = incoming.Names.Intersect(existing.Names, StringComparer.OrdinalIgnoreCase)
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

    private static bool HasWildcardConflict(CommandExportSet incoming, CommandExportSet existing)
    {
        if (incoming.HasWildcard && existing.HasAnyExport)
            return true;

        return existing.HasWildcard && incoming.HasAnyExport;
    }

    private static CommandExportSet ReadCommandExports(string manifestPath)
    {
        var exports = ModuleManifestExportReader.ReadExports(manifestPath);
        var allNames = exports.Functions
            .Concat(exports.Cmdlets)
            .Concat(exports.Aliases)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .ToArray();
        var hasWildcard = allNames.Any(static name => name.Equals("*", StringComparison.Ordinal));
        var names = allNames.Where(static name => !name.Equals("*", StringComparison.Ordinal));

        return new CommandExportSet(new HashSet<string>(names, StringComparer.OrdinalIgnoreCase), hasWildcard);
    }

    private sealed class CommandExportSet
    {
        public CommandExportSet(HashSet<string> names, bool hasWildcard)
        {
            Names = names;
            HasWildcard = hasWildcard;
        }

        public HashSet<string> Names { get; }

        public bool HasWildcard { get; }

        public bool HasAnyExport => HasWildcard || Names.Count > 0;
    }
}
