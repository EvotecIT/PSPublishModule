using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static bool IsAutoVersion(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static bool IsAutoOrLatest(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Latest", StringComparison.OrdinalIgnoreCase));

    private static bool IsAutoGuid(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static bool HasAutoRequiredModules(IEnumerable<RequiredModuleDraft> drafts)
    {
        if (drafts is null) return false;
        foreach (var d in drafts)
        {
            if (d is null) continue;
            if (IsAutoOrLatest(d.RequiredVersion) ||
                IsAutoOrLatest(d.MinimumVersion) ||
                IsAutoOrLatest(d.ModuleVersion) ||
                IsAutoGuid(d.Guid))
                return true;
        }
        return false;
    }

    private static bool AreRequiredModuleDraftListsEquivalent(
        IReadOnlyList<RequiredModuleDraft> left,
        IReadOnlyList<RequiredModuleDraft> right)
    {
        var leftList = (left ?? Array.Empty<RequiredModuleDraft>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ModuleName))
            .ToArray();
        var rightList = (right ?? Array.Empty<RequiredModuleDraft>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ModuleName))
            .ToArray();

        if (leftList.Length != rightList.Length)
            return false;

        if (leftList.Length == 0)
            return true;

        var leftCounts = BuildRequiredModuleDraftCounts(leftList);
        var rightCounts = BuildRequiredModuleDraftCounts(rightList);
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

    private static Dictionary<(string ModuleName, string ModuleVersion, string MinimumVersion, string RequiredVersion, string Guid), int>
        BuildRequiredModuleDraftCounts(IEnumerable<RequiredModuleDraft> drafts)
    {
        var counts = new Dictionary<(string ModuleName, string ModuleVersion, string MinimumVersion, string RequiredVersion, string Guid), int>();
        foreach (var draft in drafts ?? Array.Empty<RequiredModuleDraft>())
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

    private ManifestEditor.RequiredModule[] ResolveRequiredModules(
        IReadOnlyList<RequiredModuleDraft> drafts,
        bool resolveMissingModulesOnline,
        bool warnIfRequiredModulesOutdated,
        bool prerelease,
        string? repository,
        RepositoryCredential? credential)
    {
        var list = (drafts ?? Array.Empty<RequiredModuleDraft>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.ModuleName))
            .ToArray();
        if (list.Length == 0) return Array.Empty<ManifestEditor.RequiredModule>();

        var moduleNames = list.Select(d => d.ModuleName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var installed = TryGetLatestInstalledModuleInfo(moduleNames);

        Dictionary<string, (string? Version, string? Guid)>? onlineVersions = null;
        if (resolveMissingModulesOnline || warnIfRequiredModulesOutdated)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in list)
            {
                if (warnIfRequiredModulesOutdated)
                {
                    candidates.Add(d.ModuleName);
                    continue;
                }

                installed.TryGetValue(d.ModuleName, out var info);
                if (!string.IsNullOrWhiteSpace(info.Version)) continue;

                var minimumSource = !string.IsNullOrWhiteSpace(d.MinimumVersion) ? d.MinimumVersion : d.ModuleVersion;
                if (IsAutoOrLatest(d.RequiredVersion) || IsAutoOrLatest(minimumSource))
                    candidates.Add(d.ModuleName);
            }

            if (candidates.Count > 0)
                onlineVersions = TryResolveLatestOnlineVersions(candidates, repository, credential, prerelease);
        }

        var resolvedOnline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedVersion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedGuid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<ManifestEditor.RequiredModule>(list.Length);
        foreach (var d in list)
        {
            installed.TryGetValue(d.ModuleName, out var info);

            var availableVersion = info.Version;
            var availableGuid = info.Guid;
            if (onlineVersions is not null &&
                onlineVersions.TryGetValue(d.ModuleName, out var onlineInfo))
            {
                if (string.IsNullOrWhiteSpace(availableVersion) && !string.IsNullOrWhiteSpace(onlineInfo.Version))
                {
                    availableVersion = onlineInfo.Version;
                    resolvedOnline.Add(d.ModuleName);
                }
                if (string.IsNullOrWhiteSpace(availableGuid) && !string.IsNullOrWhiteSpace(onlineInfo.Guid))
                    availableGuid = onlineInfo.Guid;
            }

            var required = ResolveAutoOrLatest(d.RequiredVersion, availableVersion);
            var minimumSource = !string.IsNullOrWhiteSpace(d.MinimumVersion) ? d.MinimumVersion : d.ModuleVersion;
            if (!string.IsNullOrWhiteSpace(d.MinimumVersion) &&
                !string.IsNullOrWhiteSpace(d.ModuleVersion) &&
                !string.Equals(d.MinimumVersion, d.ModuleVersion, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn($"Module dependency '{d.ModuleName}' specifies both MinimumVersion and ModuleVersion; using MinimumVersion '{d.MinimumVersion}'.");
            }
            var moduleVersion = ResolveAutoOrLatest(minimumSource, availableVersion);
            var guid = ResolveAutoGuid(d.Guid, availableGuid);

            if (IsAutoOrLatest(d.RequiredVersion) && string.IsNullOrWhiteSpace(required))
                unresolvedVersion.Add(d.ModuleName);
            if (IsAutoOrLatest(minimumSource) && string.IsNullOrWhiteSpace(moduleVersion))
                unresolvedVersion.Add(d.ModuleName);
            if (IsAutoGuid(d.Guid) && string.IsNullOrWhiteSpace(guid))
                unresolvedGuid.Add(d.ModuleName);

            // RequiredVersion is exact; do not also emit ModuleVersion when present.
            if (!string.IsNullOrWhiteSpace(required)) moduleVersion = null;

            results.Add(new ManifestEditor.RequiredModule(d.ModuleName, moduleVersion: moduleVersion, requiredVersion: required, guid: guid));
        }

        if (resolvedOnline.Count > 0)
        {
            var listText = string.Join(", ", resolvedOnline.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            _logger.Info($"Resolved RequiredModules from repository without installing: {listText}.");
        }

        if (warnIfRequiredModulesOutdated)
        {
            var outdated = new List<(string Name, string Installed, string Latest)>();
            var missing = new List<(string Name, string Latest)>();
            var unparsable = new List<string>();

            foreach (var name in moduleNames)
            {
                installed.TryGetValue(name, out var info);
                var installedVersion = info.Version;

                string? latestVersion = null;
                if (onlineVersions is not null &&
                    onlineVersions.TryGetValue(name, out var onlineInfo))
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
                    .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(o => $"{o.Name} ({o.Installed} -> {o.Latest})"));
                _logger.Warn($"RequiredModules outdated compared to repository: {items}.");
            }

            if (missing.Count > 0)
            {
                var items = string.Join(", ", missing
                    .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(o => $"{o.Name} (latest {o.Latest})"));
                _logger.Warn($"RequiredModules not installed locally (repository has newer versions): {items}.");
            }

            if (unparsable.Count > 0)
            {
                var items = string.Join(", ", unparsable.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                _logger.Warn($"RequiredModules installed versions could not be parsed for outdated check: {items}.");
            }
        }

        if (unresolvedVersion.Count > 0)
        {
            var listText = string.Join(", ", unresolvedVersion.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            var hint = resolveMissingModulesOnline
                ? "Module was not installed and online resolution did not return a version."
                : "Module is not installed and online resolution is disabled.";
            _logger.Warn($"RequiredModules set to Auto/Latest but version could not be resolved for: {listText}. {hint} Install it or enable InstallMissingModules to resolve versions.");
        }

        if (unresolvedGuid.Count > 0)
        {
            var listText = string.Join(", ", unresolvedGuid.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            _logger.Warn($"RequiredModules set Guid=Auto but module not installed: {listText}. Install it or specify the Guid explicitly.");
        }

        return results.ToArray();
    }

    private static ManifestEditor.RequiredModule[] ResolveOutputRequiredModules(
        ManifestEditor.RequiredModule[] modules,
        bool mergeMissing,
        IReadOnlyCollection<string> approvedModules)
    {
        if (!mergeMissing)
            return modules ?? Array.Empty<ManifestEditor.RequiredModule>();

        return FilterRequiredModules(modules, approvedModules);
    }

    private static ManifestEditor.RequiredModule[] FilterRequiredModules(
        ManifestEditor.RequiredModule[] modules,
        IReadOnlyCollection<string> approvedModules)
    {
        if (modules is null || modules.Length == 0) return Array.Empty<ManifestEditor.RequiredModule>();
        if (approvedModules is null || approvedModules.Count == 0) return modules;

        var approved = new HashSet<string>(approvedModules, StringComparer.OrdinalIgnoreCase);
        return modules
            .Where(m => !string.IsNullOrWhiteSpace(m.ModuleName) && !approved.Contains(m.ModuleName!))
            .ToArray();
    }

}
