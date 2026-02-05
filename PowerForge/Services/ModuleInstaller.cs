using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Installs a staged module to user module directories in a versioned layout.
/// </summary>
public sealed class ModuleInstaller
{
    private readonly ILogger _logger;
    private static readonly char[] PathSeparators = { '/', '\\' };

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

        stagingPath = Path.GetFullPath(stagingPath.Trim().Trim('"'));
        moduleName = moduleName.Trim();
        moduleVersion = moduleVersion.Trim();

        ValidatePathSegment(moduleName, nameof(moduleName));
        ValidatePathSegment(moduleVersion, nameof(moduleVersion));

        options ??= new ModuleInstallerOptions();
        var roots = options.DestinationRoots.Count > 0 ? options.DestinationRoots : GetDefaultModuleRoots();
        var installed = new List<string>();
        var pruned = new List<string>();

        string resolvedVersion = ResolveVersion(roots, moduleName, moduleVersion, options.Strategy);
        _logger.Info($"Install strategy: {options.Strategy} → target version {resolvedVersion}");

        var failures = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                var rootFull = Path.GetFullPath(root.Trim().Trim('"'));
                var moduleRoot = EnsureChildPath(rootFull, moduleName);
                Directory.CreateDirectory(moduleRoot);
                var finalPath = EnsureChildPath(moduleRoot, resolvedVersion);
                var tempPath = EnsureChildPath(moduleRoot, $".tmp_install_{Guid.NewGuid():N}");

                // Prefer temp under moduleRoot for fast rename; fall back to OS temp on access issues
                try
                {
                    CopyDirectory(stagingPath, tempPath);
                }
                catch (UnauthorizedAccessException)
                {
                    var globalTemp = Path.Combine(Path.GetTempPath(), "PowerForgeInstall", Guid.NewGuid().ToString("N"));
                    CopyDirectory(stagingPath, globalTemp);
                    tempPath = globalTemp;
                }
                catch (IOException)
                {
                    var globalTemp = Path.Combine(Path.GetTempPath(), "PowerForgeInstall", Guid.NewGuid().ToString("N"));
                    CopyDirectory(stagingPath, globalTemp);
                    tempPath = globalTemp;
                }

                // If target exists
                if (Directory.Exists(finalPath))
                {
                    if (options.Strategy == InstallationStrategy.AutoRevision)  
                    {
                        // Compute next revision
                        _logger.Warn($"Target exists, computing next revision for {finalPath}");
                        resolvedVersion = ResolveVersion(new[] { root }, moduleName, moduleVersion, InstallationStrategy.AutoRevision);
                        ValidatePathSegment(resolvedVersion, nameof(moduleVersion));
                        finalPath = EnsureChildPath(moduleRoot, resolvedVersion);
                    }
                    else
                    {
                        // Exact: overwrite existing contents in place
                        _logger.Info($"Target exists; overwriting Exact version in-place at {finalPath}");
                        SyncDirectoryToSource(tempPath, finalPath);
                        TryDeleteDirectory(tempPath);
                        installed.Add(finalPath);
                        var keptExact = PruneOldVersions(moduleRoot, options.KeepVersions, out var removedInExact);
                        pruned.AddRange(removedInExact);
                        _logger.Verbose($"Installed at {finalPath}; versions kept={keptExact}, pruned={removedInExact.Count}");
                        continue;
                    }
                }
                try
                {
                    Directory.Move(tempPath, finalPath);
                }
                catch (IOException)
                {
                    // Cross-volume or locked rename; fall back to copy-then-delete
                    CopyDirectory(tempPath, finalPath);
                    TryDeleteDirectory(tempPath);
                }
                catch (UnauthorizedAccessException)
                {
                    // Permissions edge case: try copy-then-delete
                    CopyDirectory(tempPath, finalPath);
                    TryDeleteDirectory(tempPath);
                }
                installed.Add(finalPath);

                // Prune old versions
                var left = PruneOldVersions(moduleRoot, options.KeepVersions, out var removed);
                pruned.AddRange(removed);
                _logger.Verbose($"Installed at {finalPath}; versions kept={left}, pruned={removed.Count}");
            }
            catch (Exception ex)
            {
                failures.Add($"{root}: {ex.Message}");
            }
        }

        if (installed.Count == 0)
        {
            throw new UnauthorizedAccessException($"Failed to install into any module root. Errors: {string.Join("; ", failures)}");
        }

        return new ModuleInstallerResult(resolvedVersion, installed, pruned);
    }

    /// <summary>
    /// Resolves the final version that should be installed given desired <paramref name="moduleVersion"/> and <paramref name="strategy"/>.
    /// When <paramref name="strategy"/> is <see cref="InstallationStrategy.AutoRevision"/>, computes the next revision across
    /// the provided <paramref name="roots"/>. When null/empty, default module roots are used.
    /// </summary>
    public static string ResolveTargetVersion(IEnumerable<string>? roots, string moduleName, string moduleVersion, InstallationStrategy strategy)
    {
        var list = (roots == null ? Array.Empty<string>() : roots.ToArray());
        var effectiveRoots = list.Length > 0 ? list : GetDefaultModuleRoots();
        return ResolveVersion(effectiveRoots, moduleName, moduleVersion, strategy);
    }

    private static IReadOnlyList<string> GetDefaultModuleRoots()
    {
        var list = new List<string>();

        if (Path.DirectorySeparatorChar == '\\')
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrWhiteSpace(docs))
            {
                list.Add(Path.Combine(docs, "PowerShell", "Modules"));
                list.Add(Path.Combine(docs, "WindowsPowerShell", "Modules"));
            }
        }
        else
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var dataHome = !string.IsNullOrWhiteSpace(xdgDataHome)
                ? xdgDataHome
                : (!string.IsNullOrWhiteSpace(home)
                    ? Path.Combine(home!, ".local", "share")
                    : null);

            if (!string.IsNullOrWhiteSpace(dataHome))
                list.Add(Path.Combine(dataHome!, "powershell", "Modules"));
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

    private void SyncDirectoryToSource(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        // Remove stale files/dirs from dest so the installed module matches staging.
        RemoveItemsNotInSource(sourceDir, destDir);
        CopyDirectory(sourceDir, destDir);

        void RemoveItemsNotInSource(string source, string dest)
        {
            foreach (var file in Directory.EnumerateFiles(dest, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var sourcePath = Path.Combine(source, name!);
                if (File.Exists(sourcePath)) continue;
                TryDeleteFile(file);
            }

            foreach (var dir in Directory.EnumerateDirectories(dest, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                var sourcePath = Path.Combine(source, name!);

                if (!Directory.Exists(sourcePath))
                {
                    // source has no such directory (or has a file with this name); remove it.
                    TryDeleteStaleDirectory(dir);
                    continue;
                }

                RemoveItemsNotInSource(sourcePath, dir);
            }
        }

        void TryDeleteFile(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { _logger.Warn($"Failed to delete stale file '{path}': {ex.Message}"); }
        }

        void TryDeleteStaleDirectory(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (Exception ex) { _logger.Warn($"Failed to delete stale directory '{path}': {ex.Message}"); }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { /* best effort */ }
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

    private static void ValidatePathSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", name);

        var v = value.Trim().Trim('"');
        if (v == "." || v == "..")
            throw new ArgumentException("Value cannot be '.' or '..'.", name);

        if (v.IndexOfAny(PathSeparators) >= 0)
            throw new ArgumentException("Value cannot contain path separators.", name);

        if (v.Contains(':'))
            throw new ArgumentException("Value cannot contain drive specifiers.", name);
    }

    private static string EnsureChildPath(string root, string child)
    {
        var rootFull = Path.GetFullPath(root);
        var rootPrefix = rootFull.TrimEnd(PathSeparators) + Path.DirectorySeparatorChar;

        var combined = Path.GetFullPath(Path.Combine(rootFull, child));
        if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Resolved path '{combined}' escapes root '{rootFull}'.");

        return combined;
    }
}
