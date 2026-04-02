using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class RequiredModuleDraftDescriptor
{
    public string ModuleName { get; }
    public string? ModuleVersion { get; }
    public string? MinimumVersion { get; }
    public string? RequiredVersion { get; }
    public string? Guid { get; }

    public RequiredModuleDraftDescriptor(string moduleName, string? moduleVersion, string? minimumVersion, string? requiredVersion, string? guid)
    {
        ModuleName = moduleName;
        ModuleVersion = moduleVersion;
        MinimumVersion = minimumVersion;
        RequiredVersion = requiredVersion;
        Guid = guid;
    }
}

internal sealed class RequiredModuleResolutionEngine
{
    private readonly ILogger _logger;

    internal RequiredModuleResolutionEngine(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal bool HasAutoRequiredModules(IEnumerable<RequiredModuleDraftDescriptor> drafts)
    {
        if (drafts is null) return false;
        foreach (var draft in drafts)
        {
            if (draft is null) continue;
            if (IsAutoOrLatest(draft.RequiredVersion) ||
                IsAutoOrLatest(draft.MinimumVersion) ||
                IsAutoOrLatest(draft.ModuleVersion) ||
                IsAutoGuid(draft.Guid))
                return true;
        }

        return false;
    }

    internal RequiredModuleReference[] ResolveRequiredModules(
        IReadOnlyList<RequiredModuleDraftDescriptor> drafts,
        IReadOnlyDictionary<string, (string? Version, string? Guid)> installed,
        Func<IReadOnlyCollection<string>, IReadOnlyDictionary<string, (string? Version, string? Guid)>>? onlineLookup,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated)
    {
        var list = (drafts ?? Array.Empty<RequiredModuleDraftDescriptor>())
            .Where(static draft => draft is not null && !string.IsNullOrWhiteSpace(draft.ModuleName))
            .ToArray();
        if (list.Length == 0)
            return Array.Empty<RequiredModuleReference>();

        installed ??= new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, (string? Version, string? Guid)> onlineVersions =
            new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        if ((resolveMissingModulesOnline || warnIfRequiredModulesOutdated) && onlineLookup is not null)
        {
            var candidates = BuildOnlineLookupCandidates(list, installed, warnIfRequiredModulesOutdated);
            if (candidates.Count > 0)
                onlineVersions = onlineLookup(candidates) ??
                                 new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        }

        var resolvedOnline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedVersion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedGuid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<RequiredModuleReference>(list.Length);
        foreach (var draft in list)
        {
            installed.TryGetValue(draft.ModuleName, out var installedInfo);
            var availableVersion = installedInfo.Version;
            var availableGuid = installedInfo.Guid;

            if (onlineVersions.TryGetValue(draft.ModuleName, out var onlineInfo))
            {
                if (string.IsNullOrWhiteSpace(availableVersion) && !string.IsNullOrWhiteSpace(onlineInfo.Version))
                {
                    availableVersion = onlineInfo.Version;
                    resolvedOnline.Add(draft.ModuleName);
                }

                if (string.IsNullOrWhiteSpace(availableGuid) && !string.IsNullOrWhiteSpace(onlineInfo.Guid))
                    availableGuid = onlineInfo.Guid;
            }

            var required = ResolveAutoOrLatest(draft.RequiredVersion, availableVersion);
            var minimumSource = !string.IsNullOrWhiteSpace(draft.MinimumVersion) ? draft.MinimumVersion : draft.ModuleVersion;
            if (!string.IsNullOrWhiteSpace(draft.MinimumVersion) &&
                !string.IsNullOrWhiteSpace(draft.ModuleVersion) &&
                !string.Equals(draft.MinimumVersion, draft.ModuleVersion, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn($"Module dependency '{draft.ModuleName}' specifies both MinimumVersion and ModuleVersion; using MinimumVersion '{draft.MinimumVersion}'.");
            }

            var moduleVersion = ResolveAutoOrLatest(minimumSource, availableVersion);
            var guid = ResolveAutoGuid(draft.Guid, availableGuid);

            if (IsAutoOrLatest(draft.RequiredVersion) && string.IsNullOrWhiteSpace(required))
                unresolvedVersion.Add(draft.ModuleName);
            if (IsAutoOrLatest(minimumSource) && string.IsNullOrWhiteSpace(moduleVersion))
                unresolvedVersion.Add(draft.ModuleName);
            if (IsAutoGuid(draft.Guid) && string.IsNullOrWhiteSpace(guid))
                unresolvedGuid.Add(draft.ModuleName);

            if (!string.IsNullOrWhiteSpace(required))
                moduleVersion = null;

            results.Add(new RequiredModuleReference(draft.ModuleName, moduleVersion: moduleVersion, requiredVersion: required, guid: guid));
        }

        if (resolvedOnline.Count > 0)
        {
            var listText = string.Join(", ", resolvedOnline.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
            _logger.Info($"Resolved RequiredModules from repository without installing: {listText}.");
        }

        if (warnIfRequiredModulesOutdated)
            WarnIfRequiredModulesOutdated(list, installed, onlineVersions);

        if (unresolvedVersion.Count > 0)
        {
            var listText = string.Join(", ", unresolvedVersion.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
            var hint = resolveMissingModulesOnline
                ? "Module was not installed and online resolution did not return a version."
                : "Module is not installed and online resolution is disabled.";
            _logger.Warn($"RequiredModules set to Auto/Latest but version could not be resolved for: {listText}. {hint} Install it or enable InstallMissingModules to resolve versions.");
        }

        if (unresolvedGuid.Count > 0)
        {
            var listText = string.Join(", ", unresolvedGuid.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
            _logger.Warn($"RequiredModules set Guid=Auto but module not installed: {listText}. Install it or specify the Guid explicitly.");
        }

        return results.ToArray();
    }

    internal static bool AreDraftListsEquivalent(
        IReadOnlyList<RequiredModuleDraftDescriptor> left,
        IReadOnlyList<RequiredModuleDraftDescriptor> right)
    {
        var leftList = (left ?? Array.Empty<RequiredModuleDraftDescriptor>())
            .Where(static draft => draft is not null && !string.IsNullOrWhiteSpace(draft.ModuleName))
            .ToArray();
        var rightList = (right ?? Array.Empty<RequiredModuleDraftDescriptor>())
            .Where(static draft => draft is not null && !string.IsNullOrWhiteSpace(draft.ModuleName))
            .ToArray();

        if (leftList.Length != rightList.Length)
            return false;

        if (leftList.Length == 0)
            return true;

        var leftCounts = BuildDraftCounts(leftList);
        var rightCounts = BuildDraftCounts(rightList);
        if (leftCounts.Count != rightCounts.Count)
            return false;

        foreach (var kvp in leftCounts)
        {
            if (!rightCounts.TryGetValue(kvp.Key, out var rightCount))
                return false;
            if (kvp.Value != rightCount)
                return false;
        }

        return true;
    }

    internal static RequiredModuleReference[] ResolveOutputRequiredModules(
        RequiredModuleReference[] modules,
        bool mergeMissing,
        IReadOnlyCollection<string> approvedModules)
    {
        if (!mergeMissing)
            return modules ?? Array.Empty<RequiredModuleReference>();

        if (modules is null || modules.Length == 0)
            return Array.Empty<RequiredModuleReference>();
        if (approvedModules is null || approvedModules.Count == 0)
            return modules;

        var approved = new HashSet<string>(approvedModules, StringComparer.OrdinalIgnoreCase);
        return modules
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleName))
            .Where(module => !approved.Contains(module.ModuleName!))
            .ToArray();
    }

    internal static Dictionary<string, (string? Version, string? Guid)> SelectLatestVersions(IEnumerable<PSResourceInfo> items, bool allowPrerelease)
    {
        var map = new Dictionary<string, (Version Version, string? Pre, string Raw, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items ?? Array.Empty<PSResourceInfo>())
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Version))
                continue;

            if (!TryParseVersionParts(item.Version, out var version, out var pre))
                continue;

            if (!allowPrerelease && !string.IsNullOrWhiteSpace(pre))
                continue;

            if (!map.TryGetValue(item.Name, out var current))
            {
                map[item.Name] = (version, pre, item.Version, item.Guid);
                continue;
            }

            if (CompareVersionParts(version, pre, current.Version, current.Pre) > 0)
                map[item.Name] = (version, pre, item.Version, item.Guid);
        }

        var result = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in map)
            result[kvp.Key] = (kvp.Value.Raw, kvp.Value.Guid);
        return result;
    }

    private static IReadOnlyCollection<string> BuildOnlineLookupCandidates(
        IEnumerable<RequiredModuleDraftDescriptor> drafts,
        IReadOnlyDictionary<string, (string? Version, string? Guid)> installed,
        bool warnIfRequiredModulesOutdated)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in drafts ?? Array.Empty<RequiredModuleDraftDescriptor>())
        {
            if (warnIfRequiredModulesOutdated)
            {
                candidates.Add(draft.ModuleName);
                continue;
            }

            installed.TryGetValue(draft.ModuleName, out var info);
            var minimumSource = !string.IsNullOrWhiteSpace(draft.MinimumVersion) ? draft.MinimumVersion : draft.ModuleVersion;
            var needsVersionLookup = string.IsNullOrWhiteSpace(info.Version) &&
                                     (IsAutoOrLatest(draft.RequiredVersion) || IsAutoOrLatest(minimumSource));
            var needsGuidLookup = string.IsNullOrWhiteSpace(info.Guid) && IsAutoGuid(draft.Guid);
            if (needsVersionLookup || needsGuidLookup)
                candidates.Add(draft.ModuleName);
        }

        return candidates;
    }

    private void WarnIfRequiredModulesOutdated(
        IReadOnlyList<RequiredModuleDraftDescriptor> drafts,
        IReadOnlyDictionary<string, (string? Version, string? Guid)> installed,
        IReadOnlyDictionary<string, (string? Version, string? Guid)> onlineVersions)
    {
        var moduleNames = drafts
            .Where(static draft => !string.IsNullOrWhiteSpace(draft.ModuleName))
            .Select(static draft => draft.ModuleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var outdated = new List<(string Name, string Installed, string Latest)>();
        var missing = new List<(string Name, string Latest)>();
        var unparsable = new List<string>();

        foreach (var name in moduleNames)
        {
            installed.TryGetValue(name, out var installedInfo);
            var installedVersion = installedInfo.Version;

            string? latestVersion = null;
            if (onlineVersions.TryGetValue(name, out var onlineInfo))
                latestVersion = onlineInfo.Version;

            if (string.IsNullOrWhiteSpace(latestVersion))
                continue;

            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                missing.Add((name, latestVersion!));
                continue;
            }

            if (!TryParseVersionParts(installedVersion!, out var installedParsed, out var installedPre) ||
                !TryParseVersionParts(latestVersion!, out var latestParsed, out var latestPre))
            {
                unparsable.Add(name);
                continue;
            }

            if (CompareVersionParts(latestParsed, latestPre, installedParsed, installedPre) > 0)
                outdated.Add((name, installedVersion!, latestVersion!));
        }

        if (outdated.Count > 0)
        {
            var items = string.Join(", ", outdated
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static item => $"{item.Name} ({item.Installed} -> {item.Latest})"));
            _logger.Warn($"RequiredModules outdated compared to repository: {items}.");
        }

        if (missing.Count > 0)
        {
            var items = string.Join(", ", missing
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static item => $"{item.Name} (latest {item.Latest})"));
            _logger.Warn($"RequiredModules not installed locally (repository has newer versions): {items}.");
        }

        if (unparsable.Count > 0)
        {
            var items = string.Join(", ", unparsable.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
            _logger.Warn($"RequiredModules installed versions could not be parsed for outdated check: {items}.");
        }
    }

    private static Dictionary<(string ModuleName, string ModuleVersion, string MinimumVersion, string RequiredVersion, string Guid), int>
        BuildDraftCounts(IEnumerable<RequiredModuleDraftDescriptor> drafts)
    {
        var counts = new Dictionary<(string ModuleName, string ModuleVersion, string MinimumVersion, string RequiredVersion, string Guid), int>();
        foreach (var draft in drafts ?? Array.Empty<RequiredModuleDraftDescriptor>())
        {
            if (draft is null || string.IsNullOrWhiteSpace(draft.ModuleName))
                continue;

            var key = (
                ModuleName: NormalizeDraftValue(draft.ModuleName),
                ModuleVersion: NormalizeDraftValue(draft.ModuleVersion),
                MinimumVersion: NormalizeDraftValue(draft.MinimumVersion),
                RequiredVersion: NormalizeDraftValue(draft.RequiredVersion),
                Guid: NormalizeDraftValue(draft.Guid));

            counts.TryGetValue(key, out var current);
            counts[key] = current + 1;
        }

        return counts;
    }

    private static string NormalizeDraftValue(string? value)
    {
        if (value is null)
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length == 0
            ? string.Empty
            : trimmed.ToUpperInvariant();
    }

    private static bool IsAutoOrLatest(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Latest", StringComparison.OrdinalIgnoreCase));

    private static bool IsAutoGuid(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveAutoOrLatest(string? value, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (trimmed.Equals("Latest", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(installedVersion) ? null : installedVersion;
        }

        return trimmed;
    }

    private static string? ResolveAutoGuid(string? value, string? installedGuid)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(installedGuid) ? null : installedGuid;
        return trimmed;
    }

    private static bool TryParseVersionParts(string text, out Version version, out string? preRelease)
    {
        preRelease = null;
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var parts = trimmed.Split(new[] { '-' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (!Version.TryParse(parts[0], out var parsed) || parsed is null) return false;
        version = parsed;
        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            preRelease = parts[1].Trim();
        return true;
    }

    private static int CompareVersionParts(Version a, string? preA, Version b, string? preB)
    {
        var cmp = a.CompareTo(b);
        if (cmp != 0) return cmp;

        var hasPreA = !string.IsNullOrWhiteSpace(preA);
        var hasPreB = !string.IsNullOrWhiteSpace(preB);
        if (hasPreA == hasPreB)
        {
            if (!hasPreA) return 0;
            return string.Compare(preA, preB, StringComparison.OrdinalIgnoreCase);
        }

        return hasPreA ? -1 : 1;
    }
}
