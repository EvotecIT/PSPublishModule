namespace PowerForge;

public sealed partial class ManagedModuleUninstallService
{
    private static RequiredModuleReference[] ReadRequiredModules(InstalledModuleCandidate candidate)
        => string.IsNullOrWhiteSpace(candidate.ManifestPath)
            ? Array.Empty<RequiredModuleReference>()
            : ModuleManifestValueReader.ReadRequiredModules(candidate.ManifestPath!);

    private static RequiredModuleReference[] ReadRequiredModules(ManagedModuleUninstallTarget target)
    {
        var manifestPath = FindModuleManifest(target.ModulePath, target.Name);
        return string.IsNullOrWhiteSpace(manifestPath)
            ? Array.Empty<RequiredModuleReference>()
            : ModuleManifestValueReader.ReadRequiredModules(manifestPath!);
    }

    private static IReadOnlyList<InstalledModuleCandidate> EnumerateInstalledModules(string moduleRoot)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot) || !Directory.Exists(moduleRoot))
            return Array.Empty<InstalledModuleCandidate>();

        var modules = new List<InstalledModuleCandidate>();
        foreach (var moduleDirectory in EnumerateDirectories(moduleRoot))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            if (ManagedModuleInstallContext.IsManagedStageDirectory(moduleName))
                continue;

            var flatManifest = FindModuleManifest(moduleDirectory, moduleName);
            if (flatManifest is not null && TryReadManifestVersion(flatManifest, out var flatVersion))
                modules.Add(new InstalledModuleCandidate(moduleName, flatVersion, moduleDirectory, flatManifest, ReadManifestGuid(flatManifest), isFlatLayout: true));

            foreach (var versionDirectory in EnumerateDirectories(moduleDirectory))
            {
                var versionName = Path.GetFileName(versionDirectory);
                if (ManagedModuleInstallContext.IsManagedStageDirectory(versionName))
                    continue;

                var manifest = FindModuleManifest(versionDirectory, moduleName);
                if (manifest is not null && TryReadManifestVersion(manifest, out var manifestVersion))
                    modules.Add(new InstalledModuleCandidate(moduleName, manifestVersion, versionDirectory, manifest, ReadManifestGuid(manifest), isFlatLayout: false));
                else if (ModuleStateVersion.TryParse(versionName, out _) &&
                         HasModulePayload(versionDirectory, moduleName))
                {
                    modules.Add(new InstalledModuleCandidate(moduleName, versionName, versionDirectory, manifest, guid: null, isFlatLayout: false));
                }
            }
        }

        return modules;
    }

    private static IReadOnlyList<InstalledModuleCandidate> EnumerateInstalledModules(
        IReadOnlyList<string> moduleRoots)
        => moduleRoots
            .SelectMany(EnumerateInstalledModules)
            .GroupBy(
                static candidate => ModuleStatePathIdentity.Normalize(candidate.ModulePath),
                ModuleStatePathIdentity.Comparer)
            .Select(static group => group.First())
            .ToArray();

    private static string[] NormalizeDependencyModuleRoots(
        string moduleRoot,
        IEnumerable<string>? dependencyModuleRoots)
    {
        var roots = new List<string> { moduleRoot };
        roots.AddRange(dependencyModuleRoots ?? Array.Empty<string>());
        return roots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => ModuleStatePathIdentity.Normalize(root))
            .Distinct(ModuleStatePathIdentity.Comparer)
            .ToArray();
    }

    private static bool HasModulePayload(string modulePath, string moduleName)
        => File.Exists(Path.Combine(modulePath, moduleName + ".psm1")) ||
           File.Exists(Path.Combine(modulePath, moduleName + ".dll"));

    private static bool TryReadManifestVersion(string manifestPath, out string version)
    {
        version = string.Empty;
        if (!ModuleManifestValueReader.TryGetTopLevelString(manifestPath, "ModuleVersion", out var manifestVersion) ||
            string.IsNullOrWhiteSpace(manifestVersion))
        {
            return false;
        }

        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Prerelease")
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        version = string.IsNullOrWhiteSpace(prerelease)
            ? manifestVersion!.Trim()
            : manifestVersion!.Trim() + "-" + prerelease!.Trim().TrimStart('-');
        return true;
    }

    private static string? ReadManifestGuid(string manifestPath)
        => ModuleManifestValueReader.ReadTopLevelString(manifestPath, "GUID");

    private static string? FindModuleManifest(string modulePath, string moduleName)
    {
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
}
