using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Installs a staged module to user module directories in a versioned layout.
/// </summary>
public sealed class ModuleInstaller
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new installer.
    /// </summary>
    public ModuleInstaller(ILogger logger) => _logger = logger;

    /// <summary>
    /// Installs <paramref name="stagingPath"/> contents into the destination roots under
    /// <c>&lt;root&gt;\&lt;moduleName&gt;\&lt;version&gt;</c> using the provided <paramref name="options"/>. Returns
    /// the resolved version and installed paths.
    /// </summary>
    public ModuleInstallerResult InstallFromStaging(string stagingPath, string moduleName, string moduleVersion, ModuleInstallerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(stagingPath) || !Directory.Exists(stagingPath))
            throw new DirectoryNotFoundException($"Staging path not found: {stagingPath}");
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name is required", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(moduleVersion))
            throw new ArgumentException("Module version is required", nameof(moduleVersion));

        options ??= new ModuleInstallerOptions();
        var roots = options.DestinationRoots.Count > 0 ? options.DestinationRoots : GetDefaultModuleRoots();
        var installed = new List<string>();
        var pruned = new List<string>();

        string resolvedVersion = ResolveVersion(roots, moduleName, moduleVersion, options.Strategy);
        _logger.Info($"Install strategy: {options.Strategy} → target version {resolvedVersion}");

        foreach (var root in roots)
        {
            var moduleRoot = Path.Combine(root, moduleName);
            Directory.CreateDirectory(moduleRoot);
            var finalPath = Path.Combine(moduleRoot, resolvedVersion);
            var tempPath = Path.Combine(moduleRoot, $".tmp_install_{Guid.NewGuid():N}");

            CopyDirectory(stagingPath, tempPath);
            // Attempt atomic move into final path
            if (Directory.Exists(finalPath))
            {
                // Extremely unlikely due to ResolveVersion, but guard anyway
                _logger.Warn($"Target exists, computing next revision for {finalPath}");
                resolvedVersion = ResolveVersion(new[] { root }, moduleName, moduleVersion, InstallationStrategy.AutoRevision);
                finalPath = Path.Combine(moduleRoot, resolvedVersion);
            }
            Directory.Move(tempPath, finalPath);
            installed.Add(finalPath);

            // Prune old versions
            var left = PruneOldVersions(moduleRoot, options.KeepVersions, out var removed);
            pruned.AddRange(removed);
            _logger.Verbose($"Installed at {finalPath}; versions kept={left}, pruned={removed.Count}");
        }

        return new ModuleInstallerResult(resolvedVersion, installed, pruned);
    }

    private static IReadOnlyList<string> GetDefaultModuleRoots()
    {
        var list = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(home))
        {
            var core = Path.Combine(home, "PowerShell", "Modules");
            list.Add(core);
            if (Path.DirectorySeparatorChar == '\\')
            {
                var desktop = Path.Combine(home, "WindowsPowerShell", "Modules");
                list.Add(desktop);
            }
        }
        // Deduplicate
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveVersion(IEnumerable<string> roots, string moduleName, string baseVersion, InstallationStrategy strategy)
    {
        if (strategy == InstallationStrategy.Exact) return baseVersion;
        // AutoRevision: find existing folders and compute next revision
        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var moduleRoot = Path.Combine(root, moduleName);
            if (!Directory.Exists(moduleRoot)) continue;
            foreach (var d in Directory.EnumerateDirectories(moduleRoot))
            {
                candidates.Add(Path.GetFileName(d) ?? string.Empty);
            }
        }
        var next = NextRevision(baseVersion, candidates);
        return next;
    }

    private static string NextRevision(string baseVersion, IEnumerable<string> existing)
    {
        // Accept v, v.x patterns
        var prefix = Regex.Escape(baseVersion) + "(?:\\.(\\d+))?$";
        var re = new Regex(prefix, RegexOptions.IgnoreCase);
        int max = 0;
        foreach (var v in existing)
        {
            var m = re.Match(v);
            if (m.Success && m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var n))
            {
                if (n > max) max = n;
            }
            else if (string.Equals(v, baseVersion, StringComparison.OrdinalIgnoreCase))
            {
                max = Math.Max(max, 0);
            }
        }
        var next = max + 1;
        return $"{baseVersion}.{next}";
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var target = Path.Combine(destDir, name!);
            File.Copy(file, target, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir);
            var target = Path.Combine(destDir, name!);
            CopyDirectory(dir, target);
        }
    }

    private static int PruneOldVersions(string moduleRoot, int keep, out List<string> removed)
    {
        removed = new List<string>();
        if (!Directory.Exists(moduleRoot)) return 0;
        var dirs = Directory.EnumerateDirectories(moduleRoot)
            .Select(d => Path.GetFileName(d) ?? string.Empty)
            .Where(n => n.Length > 0 && char.IsDigit(n[0]))
            .OrderByDescending(n => ParseVersionSortable(n!))
            .ToList();
        var keepSet = dirs.Take(keep).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs.Skip(keep))
        {
            var full = Path.Combine(moduleRoot, d);
            try { Directory.Delete(full, recursive: true); removed.Add(full); }
            catch { /* best effort */ }
        }
        return keepSet.Count;
    }

    private static Version ParseVersionSortable(string v)
    {
        // pad to 4 segments for correct ordering
        var parts = v.Split('.');
        var arr = new int[4];
        for (int i = 0; i < Math.Min(parts.Length, 4); i++) int.TryParse(parts[i], out arr[i]);
        return new Version(arr[0], arr[1], arr[2], arr[3]);
    }
}
