using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Removes installed module versions from managed module roots.
/// </summary>
public sealed class ManagedModuleUninstallService
{
    /// <summary>
    /// Builds an uninstall plan without removing files.
    /// </summary>
    public ManagedModuleUninstallPlan PlanUninstall(ManagedModuleUninstallRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var names = NormalizeNames(request.Name);
        var candidates = EnumerateInstalledModules(moduleRoot);
        var matchingCandidates = candidates
            .Where(module => names.Any(pattern => pattern.IsMatch(module.Name)))
            .ToArray();
        var targets = SelectTargets(matchingCandidates, request)
            .Select(candidate => ToTarget(candidate, moduleRoot, request))
            .ToArray();

        if (!request.AllowLoadedModuleUninstall)
            ThrowIfLoaded(targets);

        if (!request.SkipDependencyCheck)
            ThrowIfDependencyBlocked(candidates, targets);

        return new ManagedModuleUninstallPlan
        {
            Name = names.Select(static pattern => pattern.Original).ToArray(),
            Version = request.Version,
            ModuleRoot = moduleRoot,
            SkipDependencyCheck = request.SkipDependencyCheck,
            Targets = targets
        };
    }

    /// <summary>
    /// Removes every target in the plan.
    /// </summary>
    public IReadOnlyList<ManagedModuleUninstallResult> Uninstall(ManagedModuleUninstallPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        if (plan.Targets.Count == 0)
        {
            return plan.Name.Select(name => new ManagedModuleUninstallResult
            {
                Name = name,
                Version = plan.Version ?? string.Empty,
                ModuleRoot = plan.ModuleRoot,
                Status = ManagedModuleUninstallStatus.NotInstalled,
                DependencyCheckSkipped = plan.SkipDependencyCheck
            }).ToArray();
        }

        ValidateUninstallPlan(plan);

        var results = new List<ManagedModuleUninstallResult>(plan.Targets.Count);
        foreach (var target in plan.Targets)
            results.Add(UninstallTarget(plan, target));

        return results;
    }

    /// <summary>
    /// Revalidates an uninstall plan before files are removed.
    /// </summary>
    public void ValidateUninstallPlan(ManagedModuleUninstallPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (plan.Targets.Count == 0 || plan.SkipDependencyCheck)
            return;

        ThrowIfDependencyBlocked(EnumerateInstalledModules(plan.ModuleRoot), plan.Targets);
    }

    private static ManagedModuleUninstallResult UninstallTarget(
        ManagedModuleUninstallPlan plan,
        ManagedModuleUninstallTarget target)
    {
        var stopwatch = Stopwatch.StartNew();
        EnsureTargetUnderRoot(plan.ModuleRoot, target.ModulePath);
        using (ManagedModuleInstallLock.Acquire(plan.ModuleRoot, target.Name, CancellationToken.None))
        {
            Directory.Delete(GetFileSystemPath(target.ModulePath), recursive: true);
            TryDeleteEmptyModuleDirectory(target.ModuleRoot, target.Name);
        }

        stopwatch.Stop();

        return new ManagedModuleUninstallResult
        {
            Name = target.Name,
            Version = target.Version,
            ModuleRoot = target.ModuleRoot,
            ModulePath = target.ModulePath,
            Status = ManagedModuleUninstallStatus.Uninstalled,
            Elapsed = stopwatch.Elapsed,
            DependencyCheckSkipped = plan.SkipDependencyCheck,
            WasLoaded = target.IsLoaded
        };
    }

    private static IReadOnlyList<InstalledModuleCandidate> SelectTargets(
        IReadOnlyList<InstalledModuleCandidate> candidates,
        ManagedModuleUninstallRequest request)
    {
        if (candidates.Count == 0)
            return Array.Empty<InstalledModuleCandidate>();

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return candidates
                .Where(candidate => request.Prerelease == candidate.IsPrerelease)
                .GroupBy(static candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(static candidate => candidate.Version, ManagedModuleVersionComparer.Instance).First())
                .ToArray();
        }

        var version = request.Version!.Trim();
        if (IsRangeExpression(version))
        {
            var range = ManagedModuleVersionRange.Parse(version);
            return candidates
                .Where(candidate => range.IsSatisfiedBy(candidate.Version))
                .Where(candidate => request.Prerelease || range.AllowsPrerelease || !candidate.IsPrerelease)
                .ToArray();
        }

        var exact = version.StartsWith("=", StringComparison.Ordinal)
            ? version.Substring(1).Trim()
            : version;

        return candidates
            .Where(candidate => VersionsEqual(candidate.Version, exact))
            .Where(candidate => !request.Prerelease || candidate.IsPrerelease)
            .ToArray();
    }

    private static ManagedModuleUninstallTarget ToTarget(
        InstalledModuleCandidate candidate,
        string moduleRoot,
        ManagedModuleUninstallRequest request)
        => new()
        {
            Name = candidate.Name,
            Version = candidate.Version,
            ModuleRoot = moduleRoot,
            ModulePath = candidate.ModulePath,
            IsLoaded = IsLoaded(candidate, request.LoadedModules)
        };

    private static void ThrowIfLoaded(IReadOnlyList<ManagedModuleUninstallTarget> targets)
    {
        var loaded = targets.Where(static target => target.IsLoaded).ToArray();
        if (loaded.Length == 0)
            return;

        throw new InvalidOperationException(
            "Refusing to uninstall loaded module version(s): " +
            string.Join(", ", loaded.Select(static target => target.Name + " " + target.Version)) +
            ". Use AllowLoadedModuleUninstall to permit this.");
    }

    private static void ThrowIfDependencyBlocked(
        IReadOnlyList<InstalledModuleCandidate> candidates,
        IReadOnlyList<ManagedModuleUninstallTarget> targets)
    {
        if (targets.Count == 0)
            return;

        var targetKeys = targets
            .Select(static target => ModuleKey.Create(target.Name, target.Version, target.ModulePath))
            .ToHashSet();
        var blockers = new List<(ManagedModuleUninstallTarget Target, ManagedModuleUninstallDependency Blocker)>();

        foreach (var target in targets)
        {
            foreach (var candidate in candidates)
            {
                if (targetKeys.Contains(ModuleKey.Create(candidate.Name, candidate.Version, candidate.ModulePath)))
                    continue;

                foreach (var requiredModule in ReadRequiredModules(candidate))
                {
                    if (!string.Equals(requiredModule.ModuleName, target.Name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!ModulePublisher.DoesVersionMatchRequiredModule(requiredModule, target.Version))
                        continue;
                    if (HasRemainingSatisfyingCandidate(candidates, targetKeys, requiredModule))
                        continue;

                    blockers.Add((target, new ManagedModuleUninstallDependency
                    {
                        ModuleName = candidate.Name,
                        Version = candidate.Version,
                        ModulePath = candidate.ModulePath,
                        RequiredVersion = ModulePublisher.FormatRequiredModuleConstraint(requiredModule)
                    }));
                }
            }
        }

        if (blockers.Count == 0)
            return;

        foreach (var group in blockers.GroupBy(static item => item.Target))
            group.Key.RequiredBy = group.Select(static item => item.Blocker).ToArray();

        var message = string.Join("; ", blockers
            .GroupBy(static item => item.Target)
            .Select(group => group.Key.Name + " " + group.Key.Version + " is required by " +
                             string.Join(", ", group.Select(static item => item.Blocker.ModuleName + " " + item.Blocker.Version).Distinct(StringComparer.OrdinalIgnoreCase))));
        throw new InvalidOperationException("Managed module uninstall dependency check failed. " + message + ". Use SkipDependencyCheck to permit removal.");
    }

    private static bool HasRemainingSatisfyingCandidate(
        IReadOnlyList<InstalledModuleCandidate> candidates,
        ISet<ModuleKey> targetKeys,
        RequiredModuleReference requiredModule)
        => candidates.Any(candidate =>
            !targetKeys.Contains(ModuleKey.Create(candidate.Name, candidate.Version, candidate.ModulePath)) &&
            string.Equals(candidate.Name, requiredModule.ModuleName, StringComparison.OrdinalIgnoreCase) &&
            ModulePublisher.DoesVersionMatchRequiredModule(requiredModule, candidate.Version));

    private static RequiredModuleReference[] ReadRequiredModules(InstalledModuleCandidate candidate)
        => string.IsNullOrWhiteSpace(candidate.ManifestPath)
            ? Array.Empty<RequiredModuleReference>()
            : ModuleManifestValueReader.ReadRequiredModules(candidate.ManifestPath!);

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
            {
                modules.Add(new InstalledModuleCandidate(moduleName, flatVersion, moduleDirectory, flatManifest));
                continue;
            }

            foreach (var versionDirectory in EnumerateDirectories(moduleDirectory))
            {
                var versionName = Path.GetFileName(versionDirectory);
                if (ManagedModuleInstallContext.IsManagedStageDirectory(versionName))
                    continue;

                var manifest = FindModuleManifest(versionDirectory, moduleName);
                if (manifest is not null && TryReadManifestVersion(manifest, out var manifestVersion))
                    modules.Add(new InstalledModuleCandidate(moduleName, manifestVersion, versionDirectory, manifest));
                else if (ModuleStateVersion.TryParse(versionName, out _))
                    modules.Add(new InstalledModuleCandidate(moduleName, versionName, versionDirectory, manifest));
            }
        }

        return modules;
    }

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

    private static IReadOnlyList<NamePattern> NormalizeNames(IEnumerable<string>? names)
    {
        var values = (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length == 0)
            throw new ArgumentException("At least one module name is required.", nameof(names));

        return values.Select(static name => new NamePattern(name)).ToArray();
    }

    private static bool IsLoaded(InstalledModuleCandidate candidate, IReadOnlyList<ManagedModuleLoadedModule>? loadedModules)
        => (loadedModules ?? Array.Empty<ManagedModuleLoadedModule>()).Any(loaded =>
            string.Equals(loaded.Name, candidate.Name, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(loaded.Version) || VersionsEqual(candidate.Version, loaded.Version!)) &&
            (string.IsNullOrWhiteSpace(loaded.ModuleBase) && string.IsNullOrWhiteSpace(loaded.Path) ||
             PathMatches(candidate.ModulePath, loaded.ModuleBase) ||
             PathMatches(candidate.ModulePath, loaded.Path)));

    private static bool VersionsEqual(string left, string right)
    {
        if (ModuleStateVersion.TryParse(left, out var parsedLeft) &&
            ModuleStateVersion.TryParse(right, out var parsedRight))
        {
            return parsedLeft.Equals(parsedRight);
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRangeExpression(string value)
        => value.StartsWith("[", StringComparison.Ordinal) ||
           value.StartsWith("(", StringComparison.Ordinal) ||
           value.StartsWith(">", StringComparison.Ordinal) ||
           value.StartsWith("<", StringComparison.Ordinal) ||
           value.Contains(",", StringComparison.Ordinal);

    private static bool PathMatches(string modulePath, string? loadedPath)
    {
        if (string.IsNullOrWhiteSpace(loadedPath))
            return false;

        var normalizedModulePath = NormalizePath(modulePath);
        var normalizedLoadedPath = NormalizePath(loadedPath!);
        return string.Equals(normalizedModulePath, normalizedLoadedPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedLoadedPath.StartsWith(normalizedModulePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedLoadedPath.StartsWith(normalizedModulePath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureTargetUnderRoot(string moduleRoot, string modulePath)
    {
        var root = NormalizePath(moduleRoot);
        var target = NormalizePath(modulePath);
        if (!string.Equals(root, target, StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove module path outside module root: {modulePath}");
        }
    }

    private static void TryDeleteEmptyModuleDirectory(string moduleRoot, string moduleName)
    {
        var moduleDirectory = Path.Combine(moduleRoot, moduleName);
        if (!Directory.Exists(moduleDirectory))
            return;

        if (Directory.EnumerateFileSystemEntries(moduleDirectory).Any())
            return;

        Directory.Delete(GetFileSystemPath(moduleDirectory), recursive: false);
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

    private static string NormalizePath(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string GetFileSystemPath(string path)
    {
        if (Path.DirectorySeparatorChar != '\\')
            return path;

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + fullPath.Substring(2);

        return @"\\?\" + fullPath;
    }

    private sealed class InstalledModuleCandidate
    {
        public InstalledModuleCandidate(string name, string version, string modulePath, string? manifestPath)
        {
            Name = name;
            Version = version;
            ModulePath = modulePath;
            ManifestPath = manifestPath;
        }

        public string Name { get; }

        public string Version { get; }

        public string ModulePath { get; }

        public string? ManifestPath { get; }

        public bool IsPrerelease => ManagedModuleVersionComparer.IsPrerelease(Version);
    }

    private sealed class NamePattern
    {
        private readonly Regex? _regex;

        public NamePattern(string original)
        {
            Original = original;
            if (HasWildcardCharacters(original))
                _regex = CreateWildcardRegex(original);
            else
                ManagedModulePackageIdentity.RequireSafeId(original, nameof(original));
        }

        public string Original { get; }

        public bool IsMatch(string value)
            => _regex is null
                ? string.Equals(Original, value, StringComparison.OrdinalIgnoreCase)
                : _regex.IsMatch(value);

        private static bool HasWildcardCharacters(string value)
            => value.IndexOfAny(new[] { '*', '?', '[' }) >= 0;

        private static Regex CreateWildcardRegex(string pattern)
        {
            var expression = new StringBuilder("^");
            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];
                switch (character)
                {
                    case '*':
                        expression.Append(".*");
                        break;
                    case '?':
                        expression.Append('.');
                        break;
                    case '[':
                        var closingIndex = pattern.IndexOf(']', index + 1);
                        if (closingIndex > index + 1)
                        {
                            expression.Append(ConvertWildcardCharacterClass(pattern.Substring(index + 1, closingIndex - index - 1)));
                            index = closingIndex;
                        }
                        else
                        {
                            expression.Append("\\[");
                        }

                        break;
                    default:
                        expression.Append(Regex.Escape(character.ToString()));
                        break;
                }
            }

            expression.Append('$');
            return new Regex(expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string ConvertWildcardCharacterClass(string body)
        {
            var expression = new StringBuilder("[");
            for (var index = 0; index < body.Length; index++)
            {
                var character = body[index];
                if (index == 0 && character == '!')
                {
                    expression.Append('^');
                    continue;
                }

                if (character is '\\' or ']')
                    expression.Append('\\');
                expression.Append(character);
            }

            expression.Append(']');
            return expression.ToString();
        }
    }

    private readonly struct ModuleKey : IEquatable<ModuleKey>
    {
        private ModuleKey(string name, string version, string path)
        {
            Name = name;
            Version = version;
            Path = NormalizePath(path);
        }

        private string Name { get; }

        private string Version { get; }

        private string Path { get; }

        public static ModuleKey Create(string name, string version, string path)
            => new(name, version, path);

        public bool Equals(ModuleKey other)
            => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
               VersionsEqual(Version, other.Version) &&
               string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj)
            => obj is ModuleKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(ModuleStateVersion.NormalizeOrOriginal(Version));
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(Path);
                return hash;
            }
        }
    }
}
