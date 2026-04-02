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

        var required = plan.RequiredModules ?? Array.Empty<RequiredModuleReference>();
        if (required.Length == 0)
        {
            var manifestPath = Path.Combine(plan.ProjectRoot, $"{plan.ModuleName}.psd1");
            if (File.Exists(manifestPath))
            {
                var fromManifest = ModuleManifestValueReader.ReadRequiredModules(manifestPath);
                if (fromManifest.Length > 0)
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

    private Dictionary<string, InstalledModuleReference> TryGetLatestInstalledModuleInfo(IReadOnlyList<string> names)
    {
        var list = (names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length == 0) return new Dictionary<string, InstalledModuleReference>(StringComparer.OrdinalIgnoreCase);

        var script = BuildGetInstalledModuleInfoScript();
        var args = new List<string>(1) { EncodeLines(list) };

        var result = RunScript(_powerShellRunner, script, args, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            return new Dictionary<string, InstalledModuleReference>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, InstalledModuleReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(result.StdOut))
        {
            if (!line.StartsWith("PFMODINFO::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 6) continue;

            var name = Decode(parts[2]);
            var ver = EmptyToNull(Decode(parts[3]));
            var guid = EmptyToNull(Decode(parts[4]));
            var moduleBasePath = EmptyToNull(Decode(parts[5]));
            if (string.IsNullOrWhiteSpace(name)) continue;
            map[name] = new InstalledModuleReference(name, ver, guid, moduleBasePath);
        }

        foreach (var n in list)
            if (!map.ContainsKey(n)) map[n] = new InstalledModuleReference(n, null, null, null);

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
        IReadOnlyList<PSResourceInfo> items = Array.Empty<PSResourceInfo>();
        var resolved = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psrg = new PSResourceGetClient(_powerShellRunner, _logger);
            var opts = new PSResourceFindOptions(list, version: null, prerelease: prerelease, repositories: repos, credential: credential);
            items = psrg.Find(opts, timeout: TimeSpan.FromMinutes(2));
            resolved = RequiredModuleResolutionEngine.SelectLatestVersions(items, prerelease);
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
            var psg = new PowerShellGetClient(_powerShellRunner, _logger);
            var useRepos = repos.Length == 0 ? new[] { "PSGallery" } : repos;
            var opts = new PowerShellGetFindOptions(list, prerelease: prerelease, repositories: useRepos, credential: credential);
            items = psg.Find(opts, timeout: TimeSpan.FromMinutes(2));
            resolved = RequiredModuleResolutionEngine.SelectLatestVersions(items, prerelease);
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

}

public sealed partial class ModulePipelineRunner
{
    private sealed class InstalledModuleReference
    {
        public string Name { get; }
        public string? Version { get; }
        public string? Guid { get; }
        public string? ModuleBasePath { get; }

        public InstalledModuleReference(string name, string? version, string? guid, string? moduleBasePath)
        {
            Name = name;
            Version = version;
            Guid = guid;
            ModuleBasePath = moduleBasePath;
        }
    }
}
