using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Removes installed module versions from managed module roots.
/// </summary>
public sealed partial class ManagedModuleUninstallService
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
        var dependencyModuleRootGroups = NormalizeDependencyModuleRootGroups(moduleRoot, request.DependencyModuleRootGroups);
        var rootsRequiredByInventory = (request.DependencyModuleRootsRequiringAvailability ?? Array.Empty<string>())
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => ModuleStatePathIdentity.Normalize(root))
            .Distinct(ModuleStatePathIdentity.Comparer)
            .ToArray();
        var dependencyModuleRoots = NormalizeDependencyModuleRoots(
            moduleRoot,
            request.DependencyModuleRoots
                .Concat(dependencyModuleRootGroups.SelectMany(static group => group))
                .Concat(rootsRequiredByInventory));
        var matchingCandidates = candidates
            .Where(module => names.Any(pattern => pattern.IsMatch(module.Name)))
            .Where(module => string.IsNullOrWhiteSpace(request.InstalledLocation) ||
                             PathsEqual(module.ModulePath, request.InstalledLocation!))
            .ToArray();
        var targets = SelectTargets(matchingCandidates, request)
            .Select(candidate => ToTarget(candidate, moduleRoot, request))
            .ToArray();
        var dependencyModuleRootsRequiringAvailability = request.SkipDependencyCheck || targets.Length == 0
            ? Array.Empty<string>()
            : SnapshotAvailableDependencyModuleRoots(dependencyModuleRoots, rootsRequiredByInventory);
        var missingNames = names
            .Where(static pattern => !pattern.IsWildcard)
            .Where(pattern => !targets.Any(target => pattern.IsMatch(target.Name)))
            .Select(static pattern => pattern.Original)
            .ToArray();

        if (!request.AllowLoadedModuleUninstall && !request.DeferLoadedModuleCheck)
            ThrowIfLoaded(targets);

        if (!request.SkipDependencyCheck && !request.DeferDependencyCheck)
            ThrowIfDependencyBlockedAcrossRootGroups(
                dependencyModuleRoots,
                dependencyModuleRootGroups,
                dependencyModuleRootsRequiringAvailability,
                targets,
                targets);

        return new ManagedModuleUninstallPlan
        {
            Name = names.Select(static pattern => pattern.Original).ToArray(),
            Version = request.Version,
            ModuleRoot = moduleRoot,
            DependencyModuleRoots = dependencyModuleRoots,
            DependencyModuleRootGroups = dependencyModuleRootGroups,
            DependencyModuleRootsRequiringAvailability = dependencyModuleRootsRequiringAvailability,
            SkipDependencyCheck = request.SkipDependencyCheck,
            AllowLoadedModuleUninstall = request.AllowLoadedModuleUninstall,
            Targets = targets,
            MissingNames = missingNames
        };
    }

    /// <summary>
    /// Removes every target in the plan.
    /// </summary>
    public IReadOnlyList<ManagedModuleUninstallResult> Uninstall(ManagedModuleUninstallPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var missingNames = plan.MissingNames.Count == 0 && plan.Targets.Count == 0
            ? plan.Name
            : plan.MissingNames;
        var results = new List<ManagedModuleUninstallResult>(plan.Targets.Count + missingNames.Count);
        if (plan.Targets.Count > 0)
        {
            ValidateUninstallPlan(plan);
            foreach (var target in OrderTargetsForRemoval(plan.Targets))
                results.Add(UninstallTarget(plan, target));
        }

        foreach (var name in missingNames)
        {
            results.Add(new ManagedModuleUninstallResult
            {
                Name = name,
                Version = plan.Version ?? string.Empty,
                ModuleRoot = plan.ModuleRoot,
                Status = ManagedModuleUninstallStatus.NotInstalled,
                DependencyCheckSkipped = plan.SkipDependencyCheck
            });
        }

        return results;
    }

    /// <summary>
    /// Revalidates an uninstall plan before files are removed.
    /// </summary>
    public void ValidateUninstallPlan(ManagedModuleUninstallPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        if (plan.Targets.Count == 0)
            return;

        EnsureModuleRootSpecified(plan.ModuleRoot);

        if (!plan.AllowLoadedModuleUninstall)
            ThrowIfLoaded(plan.Targets);

        if (!plan.SkipDependencyCheck)
            ThrowIfDependencyBlockedAcrossRootGroups(
                NormalizeDependencyModuleRoots(plan.ModuleRoot, plan.DependencyModuleRoots),
                NormalizeDependencyModuleRootGroups(plan.ModuleRoot, plan.DependencyModuleRootGroups),
                plan.DependencyModuleRootsRequiringAvailability,
                plan.Targets,
                plan.DependencyRemovalTargets.Count > 0 ? plan.DependencyRemovalTargets : plan.Targets);
    }

    private static void ThrowIfDependencyBlockedAcrossRootGroups(
        IReadOnlyList<string> dependencyModuleRoots,
        IReadOnlyList<IReadOnlyList<string>> dependencyModuleRootGroups,
        IReadOnlyList<string> rootsRequiringAvailability,
        IReadOnlyList<ManagedModuleUninstallTarget> targets,
        IReadOnlyList<ManagedModuleUninstallTarget> removalTargets)
    {
        var groups = dependencyModuleRootGroups.Count == 0
            ? new[] { dependencyModuleRoots }
            : dependencyModuleRootGroups;
        foreach (var group in groups)
        {
            ThrowIfDependencyBlocked(
                EnumerateInstalledModulesForDependencyValidation(group, rootsRequiringAvailability),
                targets,
                removalTargets);
        }
    }

    private static ManagedModuleUninstallResult UninstallTarget(
        ManagedModuleUninstallPlan plan,
        ManagedModuleUninstallTarget target)
    {
        var stopwatch = Stopwatch.StartNew();
        ManagedModulePackageIdentity.RequireSafeId(target.Name, nameof(target.Name));
        EnsureTargetUnderRoot(plan.ModuleRoot, target.ModulePath);
        using (ManagedModuleInstallLock.Acquire(plan.ModuleRoot, target.Name, CancellationToken.None))
        {
            if (target.IsFlatLayout)
                DeleteFlatModuleTarget(target);
            else
                Directory.Delete(GetFileSystemPath(target.ModulePath), recursive: true);
            TryDeleteEmptyModuleDirectory(plan.ModuleRoot, target.Name);
        }

        stopwatch.Stop();

        return new ManagedModuleUninstallResult
        {
            Name = target.Name,
            Version = target.Version,
            ModuleRoot = plan.ModuleRoot,
            ModulePath = target.ModulePath,
            Status = ManagedModuleUninstallStatus.Uninstalled,
            Elapsed = stopwatch.Elapsed,
            DependencyCheckSkipped = plan.SkipDependencyCheck,
            WasLoaded = target.IsLoaded
        };
    }

    internal static IReadOnlyList<ManagedModuleUninstallTarget> OrderTargetsForRemoval(
        IReadOnlyList<ManagedModuleUninstallTarget> targets)
    {
        if (targets.Count < 2)
            return targets;

        var byKey = targets.ToDictionary(
            static target => ModuleKey.Create(target.Name, target.Version, target.ModulePath),
            static target => target);
        var ordered = new List<ManagedModuleUninstallTarget>(targets.Count);
        var states = new Dictionary<ModuleKey, int>();

        foreach (var target in targets)
            VisitRemovalTarget(target, byKey, states, ordered);

        return ordered;
    }

    private static void VisitRemovalTarget(
        ManagedModuleUninstallTarget target,
        IReadOnlyDictionary<ModuleKey, ManagedModuleUninstallTarget> byKey,
        IDictionary<ModuleKey, int> states,
        ICollection<ManagedModuleUninstallTarget> ordered)
    {
        var key = ModuleKey.Create(target.Name, target.Version, target.ModulePath);
        if (states.TryGetValue(key, out var state))
        {
            if (state == 1)
                return;
            if (state == 2)
                return;
        }

        states[key] = 1;
        foreach (var dependent in byKey.Values)
        {
            if (ReferenceEquals(dependent, target))
                continue;
            if (ReadRequiredModules(dependent).Any(requiredModule => RequiredModuleMatchesTarget(requiredModule, target)))
                VisitRemovalTarget(dependent, byKey, states, ordered);
        }

        ordered.Add(target);
        states[key] = 2;
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
            var range = ParseUninstallRange(version);
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
            Guid = candidate.Guid,
            IsFlatLayout = candidate.IsFlatLayout,
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
        IReadOnlyList<ManagedModuleUninstallTarget> targets,
        IReadOnlyList<ManagedModuleUninstallTarget> removalTargets)
    {
        if (targets.Count == 0)
            return;

        var targetKeys = removalTargets
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
                    if (!RequiredModuleMatchesTarget(requiredModule, target))
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
            RequiredModuleMatchesCandidate(requiredModule, candidate));

    private static bool RequiredModuleMatchesTarget(
        RequiredModuleReference requiredModule,
        ManagedModuleUninstallTarget target)
        => string.Equals(requiredModule.ModuleName, target.Name, StringComparison.OrdinalIgnoreCase) &&
           ModulePublisher.DoesVersionMatchRequiredModule(requiredModule, target.Version) &&
           RequiredModuleGuidMatches(requiredModule.Guid, target.Guid);

    private static bool RequiredModuleMatchesCandidate(
        RequiredModuleReference requiredModule,
        InstalledModuleCandidate candidate)
        => string.Equals(requiredModule.ModuleName, candidate.Name, StringComparison.OrdinalIgnoreCase) &&
           ModulePublisher.DoesVersionMatchRequiredModule(requiredModule, candidate.Version) &&
           RequiredModuleGuidMatches(requiredModule.Guid, candidate.Guid);

    private static bool RequiredModuleGuidMatches(string? requiredGuid, string? installedGuid)
        => string.IsNullOrWhiteSpace(requiredGuid) ||
           string.Equals(requiredGuid!.Trim(), installedGuid?.Trim(), StringComparison.OrdinalIgnoreCase);

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

    private static ManagedModuleVersionRange ParseUninstallRange(string value)
    {
        ValidateUninstallRangeSyntax(value);
        var range = ManagedModuleVersionRange.Parse(value);
        var trimmed = value.Trim();
        if ((trimmed.StartsWith(">", StringComparison.Ordinal) || trimmed.StartsWith("<", StringComparison.Ordinal)) &&
            range.ExactVersion is null &&
            range.MaximumVersion is null &&
            string.Equals(range.MinimumVersion, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));
        }

        return range;
    }

    private static void ValidateUninstallRangeSyntax(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith(">", StringComparison.Ordinal) ||
            trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            var tokens = trimmed.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));
            }

            var hasMinimum = false;
            var hasMaximum = false;
            foreach (var token in tokens)
            {
                if (!TryParseComparatorToken(token, out var comparator))
                    throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));

                if (comparator.StartsWith(">", StringComparison.Ordinal))
                {
                    if (hasMinimum)
                        throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));
                    hasMinimum = true;
                }
                else
                {
                    if (hasMaximum)
                        throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));
                    hasMaximum = true;
                }
            }
        }
        else if (trimmed.Contains(",", StringComparison.Ordinal) &&
                 !trimmed.StartsWith("[", StringComparison.Ordinal) &&
                 !trimmed.StartsWith("(", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));
        }

        var startsBracketed = trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("(", StringComparison.Ordinal);
        var endsBracketed = trimmed.EndsWith("]", StringComparison.Ordinal) || trimmed.EndsWith(")", StringComparison.Ordinal);
        if (startsBracketed != endsBracketed)
            throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));

        if (startsBracketed)
        {
            var body = trimmed.Trim('[', ']', '(', ')').Trim();
            var commaCount = body.Count(static character => character == ',');
            if (commaCount > 1 || (commaCount == 0 && body.Length == 0))
                throw new ArgumentException($"Invalid version range syntax: '{value}'.", nameof(value));

            var operands = commaCount == 0
                ? new[] { body }
                : body.Split(new[] { ',' }, StringSplitOptions.None);
            foreach (var operand in operands)
                ValidateRangeOperand(value, operand);
        }
    }

    private static bool TryParseComparatorToken(string token, out string comparator)
    {
        comparator = string.Empty;
        var match = Regex.Match(token, @"^(>=|>|<=|<)([^\s<>=,]+)$", RegexOptions.CultureInvariant);
        if (!match.Success || !ModuleStateVersion.TryParse(match.Groups[2].Value, out _))
            return false;

        comparator = match.Groups[1].Value;
        return true;
    }

    private static void ValidateRangeOperand(string range, string operand)
    {
        var trimmedOperand = operand.Trim();
        if (trimmedOperand.Length > 0 && !ModuleStateVersion.TryParse(trimmedOperand, out _))
            throw new ArgumentException($"Invalid version range syntax: '{range}'.", nameof(range));
    }

    private static bool PathMatches(string modulePath, string? loadedPath)
    {
        if (string.IsNullOrWhiteSpace(loadedPath))
            return false;

        var normalizedModulePath = NormalizePath(modulePath);
        var normalizedLoadedPath = NormalizePath(loadedPath!);
        return string.Equals(normalizedModulePath, normalizedLoadedPath, PathStringComparison) ||
               normalizedLoadedPath.StartsWith(normalizedModulePath + Path.DirectorySeparatorChar, PathStringComparison) ||
               normalizedLoadedPath.StartsWith(normalizedModulePath + Path.AltDirectorySeparatorChar, PathStringComparison);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(NormalizePath(left), NormalizePath(right), PathStringComparison);

    private static void EnsureTargetUnderRoot(string moduleRoot, string modulePath)
    {
        EnsureModuleRootSpecified(moduleRoot);
        var root = NormalizePath(moduleRoot);
        var target = NormalizePath(modulePath);
        if (string.Equals(root, target, PathStringComparison))
            throw new InvalidOperationException($"Refusing to remove module root as a module target: {modulePath}");

        if (!target.StartsWith(root + Path.DirectorySeparatorChar, PathStringComparison) &&
            !target.StartsWith(root + Path.AltDirectorySeparatorChar, PathStringComparison))
        {
            throw new InvalidOperationException($"Refusing to remove module path outside module root: {modulePath}");
        }
    }

    private static void EnsureModuleRootSpecified(string moduleRoot)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot))
            throw new InvalidOperationException("Refusing to remove modules when the module root is empty.");
    }

    private static StringComparison PathStringComparison
        => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathStringComparer
        => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void TryDeleteEmptyModuleDirectory(string moduleRoot, string moduleName)
    {
        var moduleDirectory = Path.Combine(moduleRoot, moduleName);
        if (!Directory.Exists(moduleDirectory))
            return;

        if (Directory.EnumerateFileSystemEntries(moduleDirectory).Any())
            return;

        Directory.Delete(GetFileSystemPath(moduleDirectory), recursive: false);
    }

    private static void DeleteFlatModuleTarget(ManagedModuleUninstallTarget target)
    {
        if (!Directory.Exists(target.ModulePath))
            return;

        foreach (var file in Directory.GetFiles(target.ModulePath, "*", SearchOption.TopDirectoryOnly))
            File.Delete(GetFileSystemPath(file));

        foreach (var directory in Directory.GetDirectories(target.ModulePath, "*", SearchOption.TopDirectoryOnly))
        {
            var directoryName = Path.GetFileName(directory);
            if (ManagedModuleInstallContext.IsManagedStageDirectory(directoryName) ||
                ModuleStateVersion.TryParse(directoryName, out _))
            {
                continue;
            }

            Directory.Delete(GetFileSystemPath(directory), recursive: true);
        }
    }

    private static IReadOnlyList<string> EnumerateDirectories(string path, bool failIfUnavailable = false)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                if (failIfUnavailable)
                    throw new InvalidOperationException($"Dependency module path '{path}' is no longer available; uninstall was blocked before mutation.");
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(path);
        }
        catch (IOException)
        {
            if (failIfUnavailable)
                throw new InvalidOperationException($"Dependency module path '{path}' could not be enumerated; uninstall was blocked before mutation.");
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            if (failIfUnavailable)
                throw new InvalidOperationException($"Dependency module path '{path}' could not be enumerated; uninstall was blocked before mutation.");
            return Array.Empty<string>();
        }
        catch (System.Security.SecurityException)
        {
            if (failIfUnavailable)
                throw new InvalidOperationException($"Dependency module path '{path}' could not be enumerated; uninstall was blocked before mutation.");
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
        public InstalledModuleCandidate(
            string name,
            string version,
            string modulePath,
            string? manifestPath,
            string? guid,
            bool isFlatLayout)
        {
            Name = name;
            Version = version;
            ModulePath = modulePath;
            ManifestPath = manifestPath;
            Guid = guid;
            IsFlatLayout = isFlatLayout;
        }

        public string Name { get; }

        public string Version { get; }

        public string ModulePath { get; }

        public string? ManifestPath { get; }

        public string? Guid { get; }

        public bool IsFlatLayout { get; }

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

        public bool IsWildcard => _regex is not null;

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
               string.Equals(Path, other.Path, PathStringComparison);

        public override bool Equals(object? obj)
            => obj is ModuleKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(ModuleStateVersion.NormalizeOrOriginal(Version));
                hash = (hash * 31) + PathStringComparer.GetHashCode(Path);
                return hash;
            }
        }
    }
}
