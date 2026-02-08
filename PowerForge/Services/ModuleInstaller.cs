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
        var preserveVersions = new HashSet<string>(options.PreserveVersions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

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

                // If the user has an old "flat" install (no version folder), it can mask versioned installs.
                // Handle it before installing the new version folder.
                HandleLegacyFlatInstall(moduleRoot, moduleName, options.LegacyFlatHandling, preserveVersions);

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
                        var keptExact = PruneOldVersions(moduleRoot, options.KeepVersions, preserveVersions, out var removedInExact);
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
                var left = PruneOldVersions(moduleRoot, options.KeepVersions, preserveVersions, out var removed);
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

    private void HandleLegacyFlatInstall(
        string moduleRoot,
        string moduleName,
        LegacyFlatModuleHandling handling,
        ISet<string> preserveVersions)
    {
        if (handling == LegacyFlatModuleHandling.Ignore)
            return;

        var flatManifest = Path.Combine(moduleRoot, $"{moduleName}.psd1");
        if (!File.Exists(flatManifest))
            return;

        if (handling == LegacyFlatModuleHandling.Warn)
        {
            _logger.Warn(
                $"Legacy flat module install detected at '{moduleRoot}'. This can conflict with versioned installs. " +
                $"Configure Install.LegacyFlatHandling (Warn/Convert/Delete/Ignore) or remove it manually.");
            return;
        }

        if (handling == LegacyFlatModuleHandling.Delete)
        {
            _logger.Warn($"Deleting legacy flat module install items under '{moduleRoot}'.");
            DeleteLegacyFlatItems(moduleRoot);
            return;
        }

        if (handling != LegacyFlatModuleHandling.Convert)
            return;

        if (!ManifestEditor.TryGetTopLevelString(flatManifest, "ModuleVersion", out var version) ||
            string.IsNullOrWhiteSpace(version))
        {
            var target = EnsureLegacyFlatQuarantineFolder(moduleRoot, "unknown");
            _logger.Warn($"Legacy flat install detected but ModuleVersion could not be read. Quarantining to '{target}'.");
            MoveLegacyFlatItems(moduleRoot, target);
            return;
        }

        var legacyVersion = version!.Trim();
        try { ValidatePathSegment(legacyVersion, nameof(legacyVersion)); }
        catch
        {
            var target = EnsureLegacyFlatQuarantineFolder(moduleRoot, "invalidversion");
            _logger.Warn($"Legacy flat install detected but ModuleVersion '{legacyVersion}' is not a safe folder name. Quarantining to '{target}'.");
            MoveLegacyFlatItems(moduleRoot, target);
            return;
        }

        var destination = EnsureChildPath(moduleRoot, legacyVersion);
        if (Directory.Exists(destination))
        {
            var target = EnsureLegacyFlatQuarantineFolder(moduleRoot, legacyVersion);
            _logger.Warn($"Legacy flat install version '{legacyVersion}' already exists as a version folder. Quarantining flat items to '{target}'.");
            MoveLegacyFlatItems(moduleRoot, target);
            preserveVersions.Add(legacyVersion);
            return;
        }

        Directory.CreateDirectory(destination);
        _logger.Info($"Converting legacy flat module install to versioned layout: '{moduleRoot}' -> '{destination}'.");
        MoveLegacyFlatItems(moduleRoot, destination);

        // Ensure converted versions don't get pruned immediately when KeepVersions is small.
        preserveVersions.Add(legacyVersion);
    }

    private static string EnsureLegacyFlatQuarantineFolder(string moduleRoot, string suffix)
    {
        var quarantine = EnsureChildPath(moduleRoot, "_legacy_flat");
        Directory.CreateDirectory(quarantine);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var folder = $"{suffix}_{stamp}";
        var target = EnsureChildPath(quarantine, folder);
        Directory.CreateDirectory(target);
        return target;
    }

    private void DeleteLegacyFlatItems(string moduleRoot)
    {
        foreach (var file in Directory.EnumerateFiles(moduleRoot, "*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(file);
        }

        foreach (var dir in Directory.EnumerateDirectories(moduleRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir) ?? string.Empty;
            if (name.Length == 0) continue;
            if (IsVersionFolderName(name)) continue;
            if (name.StartsWith(".tmp_install_", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, "_legacy_flat", StringComparison.OrdinalIgnoreCase)) continue;
            TryDeleteDirectory(dir);
        }
    }

    private void MoveLegacyFlatItems(string moduleRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var file in Directory.EnumerateFiles(moduleRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file) ?? string.Empty;
            if (name.Length == 0) continue;
            var target = Path.Combine(destinationRoot, name);
            TryMoveFile(file, target);
        }

        foreach (var dir in Directory.EnumerateDirectories(moduleRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir) ?? string.Empty;
            if (name.Length == 0) continue;
            if (IsVersionFolderName(name)) continue;
            if (name.StartsWith(".tmp_install_", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, "_legacy_flat", StringComparison.OrdinalIgnoreCase)) continue;

            var target = Path.Combine(destinationRoot, name);
            TryMoveDirectory(dir, target);
        }
    }

    private static bool IsVersionFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !char.IsDigit(name[0]))
            return false;

        var basePart = name.Split(new[] { '-' }, 2)[0];
        return Version.TryParse(basePart, out _);
    }

    private void TryMoveFile(string sourcePath, string destPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            if (File.Exists(destPath))
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                TryDeleteFile(sourcePath);
                return;
            }
            File.Move(sourcePath, destPath);
        }
        catch (IOException)
        {
            try { File.Copy(sourcePath, destPath, overwrite: true); }
            catch (Exception ex) { _logger.Warn($"Failed to copy legacy flat file '{sourcePath}' -> '{destPath}': {ex.Message}"); return; }
            TryDeleteFile(sourcePath);
        }
        catch (UnauthorizedAccessException)
        {
            try { File.Copy(sourcePath, destPath, overwrite: true); }
            catch (Exception ex) { _logger.Warn($"Failed to copy legacy flat file '{sourcePath}' -> '{destPath}': {ex.Message}"); return; }
            TryDeleteFile(sourcePath);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to move legacy flat file '{sourcePath}' -> '{destPath}': {ex.Message}");
        }
    }

    private void TryMoveDirectory(string sourceDir, string destDir)
    {
        try
        {
            if (Directory.Exists(destDir))
            {
                CopyDirectory(sourceDir, destDir);
                TryDeleteDirectory(sourceDir);
                return;
            }

            Directory.Move(sourceDir, destDir);
        }
        catch (IOException)
        {
            try { CopyDirectory(sourceDir, destDir); }
            catch (Exception ex) { _logger.Warn($"Failed to copy legacy flat directory '{sourceDir}' -> '{destDir}': {ex.Message}"); return; }
            TryDeleteDirectory(sourceDir);
        }
        catch (UnauthorizedAccessException)
        {
            try { CopyDirectory(sourceDir, destDir); }
            catch (Exception ex) { _logger.Warn($"Failed to copy legacy flat directory '{sourceDir}' -> '{destDir}': {ex.Message}"); return; }
            TryDeleteDirectory(sourceDir);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to move legacy flat directory '{sourceDir}' -> '{destDir}': {ex.Message}");
        }
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

    private static int PruneOldVersions(string moduleRoot, int keep, ISet<string>? preserveVersions, out List<string> removed)
    {
        removed = new List<string>();
        if (!Directory.Exists(moduleRoot)) return 0;
        var dirs = Directory.EnumerateDirectories(moduleRoot)
            .Select(d => Path.GetFileName(d) ?? string.Empty)
            .Where(IsVersionFolderName)
            .OrderByDescending(n => ParseVersionSortable(n!))
            .ToList();

        var preserve = preserveVersions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in preserve.Where(p => !string.IsNullOrWhiteSpace(p)))
            keepSet.Add(p.Trim());

        // Keep up to 'keep' non-preserved versions (dirs are already ordered descending).
        var keptNonPreserved = 0;
        foreach (var d in dirs)
        {
            if (keepSet.Contains(d)) continue;
            if (keptNonPreserved >= keep) continue;
            keepSet.Add(d);
            keptNonPreserved++;
        }

        foreach (var d in dirs)
        {
            if (keepSet.Contains(d)) continue;
            var full = Path.Combine(moduleRoot, d);
            try { Directory.Delete(full, recursive: true); removed.Add(full); }
            catch { /* best effort */ }
        }
        return dirs.Count(d => keepSet.Contains(d));
    }

    private void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _logger.Warn($"Failed to delete file '{path}': {ex.Message}"); }
    }

    private static Version ParseVersionSortable(string v)
    {
        var basePart = v.Split(new[] { '-' }, 2)[0];
        if (Version.TryParse(basePart, out var parsed))
        {
            var build = parsed.Build < 0 ? 0 : parsed.Build;
            var rev = parsed.Revision < 0 ? 0 : parsed.Revision;
            return new Version(parsed.Major, parsed.Minor, build, rev);
        }

        return new Version(0, 0, 0, 0);
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

        if (v.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Value contains invalid path characters.", name);
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
