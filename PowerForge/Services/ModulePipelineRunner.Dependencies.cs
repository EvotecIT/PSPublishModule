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
    private ModuleDependencyInstallResult[] EnsureBuildDependenciesInstalled(ModulePipelinePlan plan)
    {
        if (plan is null) return Array.Empty<ModuleDependencyInstallResult>();

        var required = plan.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        if (required.Length == 0)
        {
            var manifestPath = Path.Combine(plan.ProjectRoot, $"{plan.ModuleName}.psd1");
            if (File.Exists(manifestPath) &&
                ManifestEditor.TryGetRequiredModules(manifestPath, out var fromManifest) &&
                fromManifest is not null)
            {
                required = fromManifest;
            }
        }

        if (required.Length == 0)
        {
            _logger.Info("InstallMissingModules enabled, but no RequiredModules were found.");
            return Array.Empty<ModuleDependencyInstallResult>();
        }

        var depList = required
            .Where(r => !string.IsNullOrWhiteSpace(r.ModuleName))
            .Select(r => new ModuleDependency(
                name: r.ModuleName.Trim(),
                requiredVersion: r.RequiredVersion,
                minimumVersion: r.ModuleVersion,
                maximumVersion: r.MaximumVersion))
            .ToList();

        if (plan.ExternalModuleDependencies is { Length: > 0 })
        {
            var known = new HashSet<string>(depList.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in plan.ExternalModuleDependencies)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var trimmed = name.Trim();
                if (known.Contains(trimmed)) continue;
                known.Add(trimmed);
                depList.Add(new ModuleDependency(trimmed, requiredVersion: null, minimumVersion: null, maximumVersion: null));
            }
        }

        var deps = depList.ToArray();

        if (deps.Length == 0)
        {
            _logger.Info("InstallMissingModules enabled, but no valid module dependencies were resolved.");
            return Array.Empty<ModuleDependencyInstallResult>();
        }

        _logger.Info($"Installing missing modules ({deps.Length}): {string.Join(", ", deps.Select(d => d.Name))}");

        var installer = new ModuleDependencyInstaller(new PowerShellRunner(), _logger);
        var results = installer.EnsureInstalled(
            dependencies: deps,
            skipModules: plan.ModuleSkip?.IgnoreModuleName,
            force: plan.InstallMissingModulesForce,
            repository: plan.InstallMissingModulesRepository,
            credential: plan.InstallMissingModulesCredential,
            prerelease: plan.InstallMissingModulesPrerelease);

        var failures = results.Where(r => r.Status == ModuleDependencyInstallStatus.Failed).ToArray();
        if (failures.Length > 0)
            throw new InvalidOperationException($"Dependency installation failed for {failures.Length} module{(failures.Length == 1 ? string.Empty : "s")}.");

        if (results.Count > 0)
        {
            var installed = results.Count(r => r.Status == ModuleDependencyInstallStatus.Installed);
            var updated = results.Count(r => r.Status == ModuleDependencyInstallStatus.Updated);
            var satisfied = results.Count(r => r.Status == ModuleDependencyInstallStatus.Satisfied);
            var skipped = results.Count(r => r.Status == ModuleDependencyInstallStatus.Skipped);
            _logger.Info($"Dependency install summary: {installed} installed, {updated} updated, {satisfied} satisfied, {skipped} skipped.");
        }

        return results.ToArray();
    }

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

    private Dictionary<string, (string? Version, string? Guid)> TryGetLatestInstalledModuleInfo(IReadOnlyList<string> names)
    {
        var list = (names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length == 0) return new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        var runner = new PowerShellRunner();
        var script = BuildGetInstalledModuleInfoScript();
        var args = new List<string>(1) { EncodeLines(list) };

        var result = RunScript(runner, script, args, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            return new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(result.StdOut))
        {
            if (!line.StartsWith("PFMODINFO::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 5) continue;

            var name = Decode(parts[2]);
            var ver = EmptyToNull(Decode(parts[3]));
            var guid = EmptyToNull(Decode(parts[4]));
            if (string.IsNullOrWhiteSpace(name)) continue;
            map[name] = (ver, guid);
        }

        foreach (var n in list)
            if (!map.ContainsKey(n)) map[n] = (null, null);

        return map;
    }

    private Dictionary<string, (string? Version, string? Guid)> TryResolveLatestOnlineVersions(
        IReadOnlyCollection<string> names,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease)
    {
        var list = (names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length == 0)
            return new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        var repos = ParseRepositoryList(repository);
        var runner = new PowerShellRunner();

        IReadOnlyList<PSResourceInfo> items = Array.Empty<PSResourceInfo>();
        var resolved = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psrg = new PSResourceGetClient(runner, _logger);
            var opts = new PSResourceFindOptions(list, version: null, prerelease: prerelease, repositories: repos, credential: credential);
            items = psrg.Find(opts, timeout: TimeSpan.FromMinutes(2));
            resolved = SelectLatestVersions(items, prerelease);
            if (resolved.Count > 0) return resolved;
        }
        catch (PowerShellToolNotAvailableException)
        {
            // fall back to PowerShellGet
        }
        catch (Exception ex)
        {
            _logger.Warn($"Find-PSResource failed while resolving RequiredModules. {ex.Message}");
        }

        try
        {
            var psg = new PowerShellGetClient(runner, _logger);
            var useRepos = repos.Length == 0 ? new[] { "PSGallery" } : repos;
            var opts = new PowerShellGetFindOptions(list, prerelease: prerelease, repositories: useRepos, credential: credential);
            items = psg.Find(opts, timeout: TimeSpan.FromMinutes(2));
            resolved = SelectLatestVersions(items, prerelease);
        }
        catch (PowerShellToolNotAvailableException ex)
        {
            _logger.Warn($"PowerShellGet not available for online resolution. {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Find-Module failed while resolving RequiredModules. {ex.Message}");
        }

        return resolved;
    }

    private static string[] ParseRepositoryList(string? repository)
    {
        var repoText = repository ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repoText)) return Array.Empty<string>();
        return repoText
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, (string? Version, string? Guid)> SelectLatestVersions(IEnumerable<PSResourceInfo> items, bool allowPrerelease)
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

        // Release > prerelease when same core version
        return hasPreA ? -1 : 1;
    }

}
