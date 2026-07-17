namespace PowerForge;

public sealed partial class ManagedModuleUninstallService
{
    private static RequiredModuleReference[] ReadRequiredModules(InstalledModuleCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ManifestPath))
            return Array.Empty<RequiredModuleReference>();

        EnsureDependencyManifestReadable(candidate.ManifestPath!);
        return ModuleManifestValueReader.ReadRequiredModules(candidate.ManifestPath!);
    }

    private static RequiredModuleReference[] ReadRequiredModules(ManagedModuleUninstallTarget target)
    {
        var manifestPath = FindModuleManifest(target.ModulePath, target.Name);
        return string.IsNullOrWhiteSpace(manifestPath)
            ? Array.Empty<RequiredModuleReference>()
            : ModuleManifestValueReader.ReadRequiredModules(manifestPath!);
    }

    private static IReadOnlyList<InstalledModuleCandidate> EnumerateInstalledModules(
        string moduleRoot,
        bool failIfUnavailable = false)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot))
            return Array.Empty<InstalledModuleCandidate>();
        if (!Directory.Exists(moduleRoot))
        {
            if (failIfUnavailable)
                throw new InvalidOperationException($"Dependency module root '{moduleRoot}' is no longer available; uninstall was blocked before mutation.");
            return Array.Empty<InstalledModuleCandidate>();
        }

        var modules = new List<InstalledModuleCandidate>();
        foreach (var moduleDirectory in EnumerateDirectories(moduleRoot, failIfUnavailable))
        {
            var moduleName = Path.GetFileName(moduleDirectory);
            if (ManagedModuleInstallContext.IsManagedStageDirectory(moduleName))
                continue;

            var flatManifest = FindModuleManifest(moduleDirectory, moduleName, failIfUnavailable);
            if (failIfUnavailable && flatManifest is not null)
                EnsureDependencyManifestReadable(flatManifest);
            if (flatManifest is not null && TryReadManifestVersion(flatManifest, out var flatVersion))
                modules.Add(new InstalledModuleCandidate(moduleName, flatVersion, moduleDirectory, flatManifest, ReadManifestGuid(flatManifest), isFlatLayout: true));

            foreach (var versionDirectory in EnumerateDirectories(moduleDirectory, failIfUnavailable))
            {
                var versionName = Path.GetFileName(versionDirectory);
                if (ManagedModuleInstallContext.IsManagedStageDirectory(versionName))
                    continue;

                var manifest = FindModuleManifest(versionDirectory, moduleName, failIfUnavailable);
                if (failIfUnavailable && manifest is not null)
                    EnsureDependencyManifestReadable(manifest);
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

    private static IReadOnlyList<InstalledModuleCandidate> EnumerateInstalledModulesForDependencyValidation(
        IReadOnlyList<string> moduleRoots,
        IReadOnlyList<string> rootsRequiringAvailability)
    {
        var required = new HashSet<string>(
            rootsRequiringAvailability ?? Array.Empty<string>(),
            ModuleStatePathIdentity.Comparer);
        return moduleRoots
            .SelectMany(root => EnumerateInstalledModules(root, required.Contains(root)))
            .GroupBy(
                static candidate => ModuleStatePathIdentity.Normalize(candidate.ModulePath),
                ModuleStatePathIdentity.Comparer)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string[] SnapshotAvailableDependencyModuleRoots(IEnumerable<string> moduleRoots)
    {
        var available = new List<string>();
        foreach (var root in moduleRoots)
        {
            if (!Directory.Exists(root))
            {
                try
                {
                    _ = File.GetAttributes(root);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    throw new InvalidOperationException(
                        $"Dependency module root '{root}' could not be inspected; uninstall was blocked before mutation.",
                        ex);
                }

                continue;
            }

            try
            {
                _ = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).Take(1).ToArray();
                available.Add(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                throw new InvalidOperationException(
                    $"Dependency module root '{root}' could not be enumerated; uninstall was blocked before mutation.",
                    ex);
            }
        }

        return available.ToArray();
    }

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

    private static string? FindModuleManifest(
        string modulePath,
        string moduleName,
        bool failIfUnavailable = false)
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
            if (failIfUnavailable)
                throw new InvalidOperationException($"Dependency module path '{modulePath}' could not be inspected; uninstall was blocked before mutation.");
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            if (failIfUnavailable)
                throw new InvalidOperationException($"Dependency module path '{modulePath}' could not be inspected; uninstall was blocked before mutation.");
            return null;
        }
    }

    private static void EnsureDependencyManifestReadable(string manifestPath)
    {
        try
        {
            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = stream.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"Dependency manifest '{manifestPath}' could not be read; uninstall was blocked before mutation.",
                ex);
        }
    }
}
