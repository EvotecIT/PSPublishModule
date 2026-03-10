using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Ensures that a set of PowerShell module dependencies are installed (out-of-process),
/// preferring PSResourceGet and falling back to PowerShellGet when PSResourceGet is not available.
/// </summary>
public sealed class ModuleDependencyInstaller
{
    private static readonly TimeSpan ModuleLookupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExactVersionProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new installer using the provided runner and logger.
    /// </summary>
    public ModuleDependencyInstaller(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ensures that all <paramref name="dependencies"/> are installed.
    /// </summary>
    public IReadOnlyList<ModuleDependencyInstallResult> EnsureInstalled(
        IEnumerable<ModuleDependency> dependencies,
        IEnumerable<string>? skipModules = null,
        bool force = false,
        string? repository = null,
        RepositoryCredential? credential = null,
        bool prerelease = false,
        bool preferPowerShellGet = false,
        TimeSpan? timeoutPerModule = null)
    {
        var list = (dependencies ?? Array.Empty<ModuleDependency>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Name))
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (list.Length == 0) return Array.Empty<ModuleDependencyInstallResult>();

        var skip = new HashSet<string>(
            (skipModules ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var names = list.Select(d => d.Name).ToArray();
        var before = GetLatestInstalledModuleVersions(names);

        var actions = new List<ActionItem>(list.Length);
        var perModuleTimeout = timeoutPerModule ?? TimeSpan.FromMinutes(5);

        foreach (var dep in list)
        {
            var installedBefore = before.TryGetValue(dep.Name, out var v) ? v : null;
            if (skip.Contains(dep.Name))
            {
                actions.Add(new ActionItem(dep.Name, installedBefore, requestedVersion: null, ModuleDependencyInstallStatus.Skipped, installer: null, message: "Skipped"));
                continue;
            }

            Decision? decision = null;
            try
            {
                decision = Decide(dep, installedBefore, force);
                var currentDecision = decision.Value;
                var requiredVersion = dep.RequiredVersion;
                string? exactRequiredVersion = null;
                if (!string.IsNullOrWhiteSpace(requiredVersion))
                    exactRequiredVersion = requiredVersion!.Trim();
                if (!force &&
                    currentDecision.NeedsInstall &&
                    !string.IsNullOrWhiteSpace(installedBefore) &&
                    exactRequiredVersion is not null &&
                    HasInstalledRequiredVersion(dep.Name, exactRequiredVersion))
                {
                    var exactMessage = string.IsNullOrWhiteSpace(installedBefore) || string.Equals(installedBefore, exactRequiredVersion, StringComparison.OrdinalIgnoreCase)
                        ? $"Exact required version {exactRequiredVersion} already installed"
                        : $"Exact required version {exactRequiredVersion} already present (latest installed: {installedBefore})";
                    actions.Add(new ActionItem(
                        dep.Name,
                        installedBefore,
                        exactRequiredVersion,
                        ModuleDependencyInstallStatus.Satisfied,
                        installer: null,
                        message: exactMessage));
                    continue;
                }

                if (!currentDecision.NeedsInstall)
                {
                    actions.Add(new ActionItem(dep.Name, installedBefore, currentDecision.RequestedVersion, ModuleDependencyInstallStatus.Satisfied, installer: null, message: currentDecision.Reason));
                    continue;
                }

                var installStatus = installedBefore is null ? ModuleDependencyInstallStatus.Installed : ModuleDependencyInstallStatus.Updated;
                var usedInstaller = TryInstall(dep, currentDecision.VersionArgument, repository, credential, prerelease, force, preferPowerShellGet, perModuleTimeout);
                actions.Add(new ActionItem(dep.Name, installedBefore, currentDecision.RequestedVersion, installStatus, installer: usedInstaller, message: currentDecision.Reason));
            }
            catch (Exception ex)
            {
                actions.Add(new ActionItem(
                    dep.Name,
                    installedBefore,
                    decision?.RequestedVersion ?? dep.RequiredVersion ?? dep.MinimumVersion,
                    ModuleDependencyInstallStatus.Failed,
                    installer: null,
                    message: ex.Message));
            }
        }

        var after = GetLatestInstalledModuleVersions(names);
        return actions
            .Select(a =>
                new ModuleDependencyInstallResult(
                    name: a.Name,
                    installedVersion: a.InstalledBefore,
                    resolvedVersion: after.TryGetValue(a.Name, out var av) ? av : null,
                    requestedVersion: a.RequestedVersion,
                    status: a.Status,
                    installer: a.Installer,
                    message: a.Message))
            .ToArray();
    }

    private bool HasInstalledRequiredVersion(string name, string requiredVersion)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(requiredVersion))
            return false;

        var script = BuildFindInstalledModuleScript();
        var args = new List<string>(4)
        {
            name.Trim(),
            requiredVersion.Trim(),
            // The shared locator script expects positional Required/Minimum/Maximum values.
            // Keep blank placeholders here so the exact-version probe stays aligned with that signature.
            string.Empty,
            string.Empty
        };

        var result = RunScript(script, args, ExactVersionProbeTimeout);
        if (result.ExitCode != 0)
        {
            var msg = TryExtractModuleLocatorError(result.StdOut) ?? result.StdErr;
            throw new InvalidOperationException($"Get-Module -ListAvailable failed (exit {result.ExitCode}). {msg}".Trim());
        }

        return SplitLines(result.StdOut).Any(static line => line.StartsWith("PFMODLOC::FOUND::", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ensures that all <paramref name="dependencies"/> are updated when already installed, and installed when missing.
    /// </summary>
    public IReadOnlyList<ModuleDependencyInstallResult> EnsureUpdated(
        IEnumerable<ModuleDependency> dependencies,
        IEnumerable<string>? skipModules = null,
        string? repository = null,
        RepositoryCredential? credential = null,
        bool prerelease = false,
        bool preferPowerShellGet = false,
        TimeSpan? timeoutPerModule = null)
    {
        var list = (dependencies ?? Array.Empty<ModuleDependency>())
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Name))
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (list.Length == 0) return Array.Empty<ModuleDependencyInstallResult>();

        var skip = new HashSet<string>(
            (skipModules ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var names = list.Select(d => d.Name).ToArray();
        var before = GetLatestInstalledModuleVersions(names);

        var actions = new List<ActionItem>(list.Length);
        var perModuleTimeout = timeoutPerModule ?? TimeSpan.FromMinutes(5);

        foreach (var dep in list)
        {
            var installedBefore = before.TryGetValue(dep.Name, out var v) ? v : null;
            if (skip.Contains(dep.Name))
            {
                actions.Add(new ActionItem(dep.Name, installedBefore, requestedVersion: null, ModuleDependencyInstallStatus.Skipped, installer: null, message: "Skipped"));
                continue;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(installedBefore))
                {
                    var installStatus = TryInstall(dep, BuildVersionArgument(dep), repository, credential, prerelease, force: false, preferPowerShellGet, perModuleTimeout);
                    actions.Add(new ActionItem(dep.Name, installedBefore, dep.RequiredVersion ?? dep.MinimumVersion, ModuleDependencyInstallStatus.Installed, installer: installStatus, message: "Not installed"));
                }
                else
                {
                    var updateStatus = TryUpdate(dep, installedBefore!, repository, credential, prerelease, preferPowerShellGet, perModuleTimeout);
                    actions.Add(new ActionItem(dep.Name, installedBefore, dep.RequiredVersion ?? dep.MinimumVersion, ModuleDependencyInstallStatus.Updated, installer: updateStatus, message: "Update requested"));
                }
            }
            catch (Exception ex)
            {
                actions.Add(new ActionItem(dep.Name, installedBefore, dep.RequiredVersion ?? dep.MinimumVersion, ModuleDependencyInstallStatus.Failed, installer: null, message: ex.Message));
            }
        }

        var after = GetLatestInstalledModuleVersions(names);
        return actions
            .Select(a =>
            {
                var resolvedVersion = after.TryGetValue(a.Name, out var av) ? av : null;
                var status = a.Status;
                var message = a.Message;

                if (a.Status == ModuleDependencyInstallStatus.Updated &&
                    VersionsEquivalent(a.InstalledBefore, resolvedVersion))
                {
                    status = ModuleDependencyInstallStatus.Satisfied;
                    message = "Already up to date";
                }

                return new ModuleDependencyInstallResult(
                    name: a.Name,
                    installedVersion: a.InstalledBefore,
                    resolvedVersion: resolvedVersion,
                    requestedVersion: a.RequestedVersion,
                    status: status,
                    installer: a.Installer,
                    message: message);
            })
            .ToArray();
    }

    private static Decision Decide(ModuleDependency dep, string? installedVersion, bool force)
    {
        if (force)
        {
            var arg = BuildVersionArgument(dep);
            return new Decision(needsInstall: true, requestedVersion: dep.RequiredVersion ?? dep.MinimumVersion, versionArgument: arg, reason: "Force requested");
        }

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            var arg = BuildVersionArgument(dep);
            return new Decision(needsInstall: true, requestedVersion: dep.RequiredVersion ?? dep.MinimumVersion, versionArgument: arg, reason: "Not installed");
        }

        if (!TryParseVersion(installedVersion, out var installed))
        {
            var arg = BuildVersionArgument(dep);
            return new Decision(needsInstall: true, requestedVersion: dep.RequiredVersion ?? dep.MinimumVersion, versionArgument: arg, reason: $"Unable to parse installed version '{installedVersion}'");
        }

        if (!string.IsNullOrWhiteSpace(dep.RequiredVersion))
        {
            if (!TryParseVersion(dep.RequiredVersion, out var required))
            {
                return new Decision(needsInstall: true, requestedVersion: dep.RequiredVersion, versionArgument: dep.RequiredVersion, reason: $"Unable to parse RequiredVersion '{dep.RequiredVersion}'");
            }

            if (installed != required)
                return new Decision(needsInstall: true, requestedVersion: dep.RequiredVersion, versionArgument: dep.RequiredVersion, reason: $"Exact version required: {dep.RequiredVersion} (installed: {installedVersion})");

            return new Decision(needsInstall: false, requestedVersion: dep.RequiredVersion, versionArgument: dep.RequiredVersion, reason: "Exact version already installed");
        }

        if (!string.IsNullOrWhiteSpace(dep.MinimumVersion))
        {
            if (!TryParseVersion(dep.MinimumVersion, out var min))
            {
                var arg = BuildVersionArgument(dep);
                return new Decision(needsInstall: true, requestedVersion: dep.MinimumVersion, versionArgument: arg, reason: $"Unable to parse MinimumVersion '{dep.MinimumVersion}'");
            }

            if (installed < min)
            {
                var arg = BuildNuGetRange(minInclusive: dep.MinimumVersion, maxInclusive: dep.MaximumVersion);
                return new Decision(needsInstall: true, requestedVersion: dep.MinimumVersion, versionArgument: arg, reason: $"Below minimum version: {dep.MinimumVersion} (installed: {installedVersion})");
            }
        }

        if (!string.IsNullOrWhiteSpace(dep.MaximumVersion))
        {
            if (TryParseVersion(dep.MaximumVersion, out var max))
            {
                if (installed > max)
                    return new Decision(needsInstall: false, requestedVersion: null, versionArgument: null, reason: $"Above maximum version: {dep.MaximumVersion} (installed: {installedVersion}) - keeping newer");
            }
        }

        return new Decision(needsInstall: false, requestedVersion: dep.MinimumVersion, versionArgument: BuildVersionArgument(dep), reason: "Version requirements satisfied");
    }

    private string TryInstall(
        ModuleDependency dep,
        string? versionArgument,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease,
        bool force,
        bool preferPowerShellGet,
        TimeSpan timeout)
    {
        if (preferPowerShellGet)
        {
            try
            {
                InstallWithPowerShellGet(dep, repository, credential, timeout);
                return "PowerShellGet";
            }
            catch (PowerShellToolNotAvailableException)
            {
                _logger.Warn($"PowerShellGet not available; trying PSResourceGet Install-PSResource for '{dep.Name}'.");
            }
        }

        // Prefer PSResourceGet (out-of-process).
        try
        {
            var client = new PSResourceGetClient(_runner, _logger);
            var opts = new PSResourceInstallOptions(
                name: dep.Name,
                version: versionArgument,
                repository: repository,
                scope: "CurrentUser",
                prerelease: prerelease,
                reinstall: force,
                trustRepository: true,
                skipDependencyCheck: false,
                acceptLicense: true,
                quiet: true,
                credential: credential);
            client.Install(opts, timeout);
            return "PSResourceGet";
        }
        catch (PowerShellToolNotAvailableException)
        {
            _logger.Warn($"PSResourceGet not available; falling back to PowerShellGet Install-Module for '{dep.Name}'.");
            InstallWithPowerShellGet(dep, repository, credential, timeout);
            return "PowerShellGet";
        }
    }

    private string? TryUpdate(
        ModuleDependency dep,
        string installedVersion,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease,
        bool preferPowerShellGet,
        TimeSpan timeout)
    {
        if (preferPowerShellGet)
        {
            try
            {
                return UpdateWithPowerShellGet(dep, installedVersion, repository, credential, prerelease, timeout);
            }
            catch (PowerShellToolNotAvailableException)
            {
                _logger.Warn($"PowerShellGet not available; trying PSResourceGet Update-PSResource for '{dep.Name}'.");
            }
        }

        try
        {
            UpdateWithPSResourceGet(dep, repository, credential, prerelease, timeout);
            return "PSResourceGet";
        }
        catch (PowerShellToolNotAvailableException)
        {
            _logger.Warn($"PSResourceGet not available; falling back to PowerShellGet Update-Module for '{dep.Name}'.");
            return UpdateWithPowerShellGet(dep, installedVersion, repository, credential, prerelease, timeout);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warn($"Update-PSResource failed for '{dep.Name}'; trying PowerShellGet Update-Module fallback. {ex.Message}");
            try
            {
                return UpdateWithPowerShellGet(dep, installedVersion, repository, credential, prerelease, timeout);
            }
            catch (Exception fallbackEx) when (fallbackEx is PowerShellToolNotAvailableException or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Update-PSResource failed for '{dep.Name}' and PowerShellGet fallback also failed. PSResourceGet: {ex.Message} PowerShellGet: {fallbackEx.Message}");
            }
        }
    }

    private void InstallWithPowerShellGet(ModuleDependency dep, string? repository, RepositoryCredential? credential, TimeSpan timeout)
    {
        var script = BuildInstallModuleScript();
        var args = new List<string>(6)
        {
            dep.Name,
            dep.RequiredVersion ?? string.Empty,
            dep.MinimumVersion ?? string.Empty,
            repository ?? string.Empty,
            credential?.UserName ?? string.Empty,
            credential?.Secret ?? string.Empty
        };
        var result = RunScript(script, args, timeout);
        if (result.ExitCode != 0)
        {
            var msg = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Install-Module failed (exit {result.ExitCode}). {msg}".Trim();
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PowerShellGet", full);
            throw new InvalidOperationException(full);
        }
    }

    private void UpdateWithPSResourceGet(ModuleDependency dep, string? repository, RepositoryCredential? credential, bool prerelease, TimeSpan timeout)
    {
        var script = BuildUpdatePSResourceScript();
        var args = new List<string>(5)
        {
            dep.Name,
            repository ?? string.Empty,
            prerelease ? "1" : "0",
            credential?.UserName ?? string.Empty,
            credential?.Secret ?? string.Empty
        };
        var result = RunScript(script, args, timeout);
        if (result.ExitCode != 0)
        {
            var msg = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Update-PSResource failed (exit {result.ExitCode}). {msg}".Trim();
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PSResourceGet", full);
            throw new InvalidOperationException(full);
        }
    }

    private string? UpdateWithPowerShellGet(ModuleDependency dep, string installedVersion, string? repository, RepositoryCredential? credential, bool prerelease, TimeSpan timeout)
    {
        if (!string.IsNullOrWhiteSpace(repository))
        {
            var scopedRepository = repository!;
            var latestRepositoryVersion = GetLatestPowerShellGetRepositoryVersion(dep.Name, scopedRepository, credential, prerelease, timeout);
            if (string.IsNullOrWhiteSpace(latestRepositoryVersion))
                throw new InvalidOperationException($"Unable to find module '{dep.Name}' in repository '{scopedRepository}' for PowerShellGet update fallback.");

            if (VersionsEquivalent(installedVersion, latestRepositoryVersion))
                return null;

            if (CompareVersionStrings(latestRepositoryVersion, installedVersion) <= 0)
                return null;

            InstallWithPowerShellGet(new ModuleDependency(dep.Name, requiredVersion: latestRepositoryVersion), scopedRepository, credential, timeout);
            return "PowerShellGet";
        }

        var script = BuildUpdateModuleScript();
        var args = new List<string>(4)
        {
            dep.Name,
            prerelease ? "1" : "0",
            credential?.UserName ?? string.Empty,
            credential?.Secret ?? string.Empty
        };
        var result = RunScript(script, args, timeout);
        if (result.ExitCode != 0)
        {
            var msg = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Update-Module failed (exit {result.ExitCode}). {msg}".Trim();
            if (result.ExitCode == 3)
                throw new PowerShellToolNotAvailableException("PowerShellGet", full);
            throw new InvalidOperationException(full);
        }

        return "PowerShellGet";
    }

    private string? GetLatestPowerShellGetRepositoryVersion(string moduleName, string repository, RepositoryCredential? credential, bool prerelease, TimeSpan timeout)
    {
        var client = new PowerShellGetClient(_runner, _logger);
        var items = client.Find(
            new PowerShellGetFindOptions(
                names: new[] { moduleName },
                prerelease: prerelease,
                repositories: new[] { repository },
                credential: credential),
            timeout);

        return items
            .Where(static item => !string.IsNullOrWhiteSpace(item.Version))
            .OrderByDescending(static item => item.Version, Comparer<string>.Create(CompareVersionStrings))
            .Select(static item => item.Version)
            .FirstOrDefault();
    }

    private Dictionary<string, string?> GetLatestInstalledModuleVersions(IReadOnlyList<string> names)
    {
        var script = BuildGetInstalledVersionsScript();
        var args = new List<string>(1) { EncodeLines(names) };
        var result = RunScript(script, args, ModuleLookupTimeout);
        if (result.ExitCode != 0)
        {
            var msg = TryExtractError(result.StdOut) ?? result.StdErr;
            throw new InvalidOperationException($"Get-Module -ListAvailable failed (exit {result.ExitCode}). {msg}".Trim());
        }

        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(result.StdOut))
        {
            if (!line.StartsWith("PFMOD::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 4) continue;
            var name = Decode(parts[2]);
            var ver = Decode(parts[3]);
            if (string.IsNullOrWhiteSpace(name)) continue;
            map[name] = string.IsNullOrWhiteSpace(ver) ? null : ver;
        }
        // Ensure all requested names exist in map
        foreach (var n in names)
            if (!map.ContainsKey(n)) map[n] = null;
        return map;
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "moduledeps");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"moduledeps_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static string? BuildVersionArgument(ModuleDependency dep)
    {
        if (!string.IsNullOrWhiteSpace(dep.RequiredVersion)) return dep.RequiredVersion;
        if (!string.IsNullOrWhiteSpace(dep.MinimumVersion))
            return BuildNuGetRange(minInclusive: dep.MinimumVersion, maxInclusive: dep.MaximumVersion);
        return null;
    }

    private static string BuildNuGetRange(string? minInclusive, string? maxInclusive)
    {
        if (string.IsNullOrWhiteSpace(minInclusive) && string.IsNullOrWhiteSpace(maxInclusive))
            return string.Empty;

        // NuGet version range syntax. See Install-PSResource -Version help.
        // [min, ] = minimum inclusive
        // (, max] = maximum inclusive
        // [min, max] = inclusive range
        if (!string.IsNullOrWhiteSpace(minInclusive) && !string.IsNullOrWhiteSpace(maxInclusive))
            return $"[{minInclusive}, {maxInclusive}]";
        if (!string.IsNullOrWhiteSpace(minInclusive))
            return $"[{minInclusive}, ]";
        return $"[, {maxInclusive}]";
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            version = new Version(0, 0);
            return false;
        }

        var s = text!.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        if (Version.TryParse(s, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }

    private static bool VersionsEquivalent(string? left, string? right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryParseVersion(left, out var parsedLeft) && TryParseVersion(right, out var parsedRight))
            return parsedLeft == parsedRight;

        return false;
    }

    private static int CompareVersionStrings(string? left, string? right)
    {
        if (VersionsEquivalent(left, right))
            return 0;

        if (TryCompareSemanticVersions(left, right, out var semanticComparison))
            return semanticComparison;

        if (TryParseVersion(left, out var parsedLeft) && TryParseVersion(right, out var parsedRight))
            return parsedLeft.CompareTo(parsedRight);

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCompareSemanticVersions(string? left, string? right, out int comparison)
    {
        if (!TryParseSemanticVersion(left, out var parsedLeft) ||
            !TryParseSemanticVersion(right, out var parsedRight))
        {
            comparison = 0;
            return false;
        }

        comparison = parsedLeft.CompareTo(parsedRight);
        return true;
    }

    private static bool TryParseSemanticVersion(string? text, out SemanticVersionParts version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text!.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);

        var buildMetadataSeparator = value.IndexOf('+');
        if (buildMetadataSeparator >= 0) value = value.Substring(0, buildMetadataSeparator);

        var prereleaseSeparator = value.IndexOf('-');
        var coreVersion = prereleaseSeparator >= 0 ? value.Substring(0, prereleaseSeparator) : value;
        var prerelease = prereleaseSeparator >= 0 ? value.Substring(prereleaseSeparator + 1) : string.Empty;

        var coreParts = coreVersion.Split('.');
        if (coreParts.Length < 2 || coreParts.Length > 3)
            return false;

        if (!int.TryParse(coreParts[0], out var major) ||
            !int.TryParse(coreParts[1], out var minor))
            return false;

        var patch = 0;
        if (coreParts.Length == 3 && !int.TryParse(coreParts[2], out patch))
            return false;

        var prereleaseIdentifiers = string.IsNullOrWhiteSpace(prerelease)
            ? Array.Empty<string>()
            : prerelease.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

        version = new SemanticVersionParts(major, minor, patch, prereleaseIdentifiers);
        return true;
    }

    private readonly struct SemanticVersionParts : IComparable<SemanticVersionParts>
    {
        internal SemanticVersionParts(int major, int minor, int patch, string[] prereleaseIdentifiers)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PrereleaseIdentifiers = prereleaseIdentifiers;
        }

        internal int Major { get; }
        internal int Minor { get; }
        internal int Patch { get; }
        internal string[] PrereleaseIdentifiers { get; }

        public int CompareTo(SemanticVersionParts other)
        {
            var comparison = Major.CompareTo(other.Major);
            if (comparison != 0)
                return comparison;

            comparison = Minor.CompareTo(other.Minor);
            if (comparison != 0)
                return comparison;

            comparison = Patch.CompareTo(other.Patch);
            if (comparison != 0)
                return comparison;

            var isStable = PrereleaseIdentifiers.Length == 0;
            var otherIsStable = other.PrereleaseIdentifiers.Length == 0;
            if (isStable && otherIsStable)
                return 0;
            if (isStable)
                return 1;
            if (otherIsStable)
                return -1;

            var count = Math.Min(PrereleaseIdentifiers.Length, other.PrereleaseIdentifiers.Length);
            for (var i = 0; i < count; i++)
            {
                var leftPart = PrereleaseIdentifiers[i];
                var rightPart = other.PrereleaseIdentifiers[i];
                var leftIsNumeric = int.TryParse(leftPart, out var leftNumeric);
                var rightIsNumeric = int.TryParse(rightPart, out var rightNumeric);

                if (leftIsNumeric && rightIsNumeric)
                {
                    comparison = leftNumeric.CompareTo(rightNumeric);
                    if (comparison != 0)
                        return comparison;

                    continue;
                }

                if (leftIsNumeric != rightIsNumeric)
                    return leftIsNumeric ? -1 : 1;

                comparison = CompareMixedPrereleaseIdentifier(leftPart, rightPart);
                if (comparison != 0)
                    return comparison;
            }

            return PrereleaseIdentifiers.Length.CompareTo(other.PrereleaseIdentifiers.Length);
        }

        private static int CompareMixedPrereleaseIdentifier(string left, string right)
        {
            var leftIndex = 0;
            var rightIndex = 0;

            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                var leftDigits = char.IsDigit(left[leftIndex]);
                var rightDigits = char.IsDigit(right[rightIndex]);
                if (leftDigits != rightDigits)
                    return leftDigits ? -1 : 1;

                var leftStart = leftIndex;
                while (leftIndex < left.Length && char.IsDigit(left[leftIndex]) == leftDigits)
                    leftIndex++;

                var rightStart = rightIndex;
                while (rightIndex < right.Length && char.IsDigit(right[rightIndex]) == rightDigits)
                    rightIndex++;

                var leftPart = left.Substring(leftStart, leftIndex - leftStart);
                var rightPart = right.Substring(rightStart, rightIndex - rightStart);

                var comparison = leftDigits
                    ? CompareNumericStrings(leftPart, rightPart)
                    : string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);

                if (comparison != 0)
                    return comparison;
            }

            return left.Length.CompareTo(right.Length);
        }

        private static int CompareNumericStrings(string left, string right)
        {
            var trimmedLeft = left.TrimStart('0');
            var trimmedRight = right.TrimStart('0');
            if (trimmedLeft.Length == 0)
                trimmedLeft = "0";
            if (trimmedRight.Length == 0)
                trimmedRight = "0";

            var comparison = trimmedLeft.Length.CompareTo(trimmedRight.Length);
            if (comparison != 0)
                return comparison;

            return string.Compare(trimmedLeft, trimmedRight, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string EncodeLines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines ?? Array.Empty<string>());
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(joined));
    }

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string? TryExtractError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFMOD::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFMOD::ERROR::".Length);
            var msg = Decode(b64);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
        return null;
    }

    private static string? TryExtractModuleLocatorError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFMODLOC::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFMODLOC::ERROR::".Length);
            var msg = Decode(b64);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
        return null;
    }

    private static string BuildGetInstalledVersionsScript()
    {
        return EmbeddedScripts.Load("Scripts/ModuleDependencyInstaller/Get-InstalledVersions.ps1");
    }
    private static string BuildFindInstalledModuleScript()
    {
        return EmbeddedScripts.Load("Scripts/ModuleLocator/Find-InstalledModule.ps1");
    }

    private static string BuildInstallModuleScript()
    {
        return EmbeddedScripts.Load("Scripts/ModuleDependencyInstaller/Install-Module.ps1");
    }

    private static string BuildUpdateModuleScript()
    {
        return EmbeddedScripts.Load("Scripts/ModuleDependencyInstaller/Update-Module.ps1");
    }

    private static string BuildUpdatePSResourceScript()
    {
        return EmbeddedScripts.Load("Scripts/ModuleDependencyInstaller/Update-PSResource.ps1");
    }

    private readonly struct Decision
    {
        public bool NeedsInstall { get; }
        public string? RequestedVersion { get; }
        public string? VersionArgument { get; }
        public string? Reason { get; }

        public Decision(bool needsInstall, string? requestedVersion, string? versionArgument, string? reason)
        {
            NeedsInstall = needsInstall;
            RequestedVersion = requestedVersion;
            VersionArgument = versionArgument;
            Reason = reason;
        }
    }

    private readonly struct ActionItem
    {
        public string Name { get; }
        public string? InstalledBefore { get; }
        public string? RequestedVersion { get; }
        public ModuleDependencyInstallStatus Status { get; }
        public string? Installer { get; }
        public string? Message { get; }

        public ActionItem(string name, string? installedBefore, string? requestedVersion, ModuleDependencyInstallStatus status, string? installer, string? message)
        {
            Name = name;
            InstalledBefore = installedBefore;
            RequestedVersion = requestedVersion;
            Status = status;
            Installer = installer;
            Message = message;
        }
    }
}
