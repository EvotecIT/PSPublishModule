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
        bool prerelease = false,
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

            var decision = Decide(dep, installedBefore, force);
            if (!decision.NeedsInstall)
            {
                actions.Add(new ActionItem(dep.Name, installedBefore, decision.RequestedVersion, ModuleDependencyInstallStatus.Satisfied, installer: null, message: decision.Reason));
                continue;
            }

            try
            {
                var installStatus = installedBefore is null ? ModuleDependencyInstallStatus.Installed : ModuleDependencyInstallStatus.Updated;
                var usedInstaller = TryInstall(dep, decision.VersionArgument, repository, prerelease, force, perModuleTimeout);
                actions.Add(new ActionItem(dep.Name, installedBefore, decision.RequestedVersion, installStatus, installer: usedInstaller, message: decision.Reason));
            }
            catch (Exception ex)
            {
                actions.Add(new ActionItem(dep.Name, installedBefore, decision.RequestedVersion, ModuleDependencyInstallStatus.Failed, installer: null, message: ex.Message));
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

    private string TryInstall(ModuleDependency dep, string? versionArgument, string? repository, bool prerelease, bool force, TimeSpan timeout)
    {
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
                skipDependencyCheck: false);
            client.Install(opts, timeout);
            return "PSResourceGet";
        }
        catch (PowerShellToolNotAvailableException)
        {
            _logger.Warn($"PSResourceGet not available; falling back to PowerShellGet Install-Module for '{dep.Name}'.");
            InstallWithPowerShellGet(dep, repository, timeout);
            return "PowerShellGet";
        }
    }

    private void InstallWithPowerShellGet(ModuleDependency dep, string? repository, TimeSpan timeout)
    {
        var script = BuildInstallModuleScript();
        var args = new List<string>(4)
        {
            dep.Name,
            dep.RequiredVersion ?? string.Empty,
            dep.MinimumVersion ?? string.Empty,
            repository ?? string.Empty
        };
        var result = RunScript(script, args, timeout);
        if (result.ExitCode != 0)
        {
            var msg = TryExtractError(result.StdOut) ?? result.StdErr;
            throw new InvalidOperationException($"Install-Module failed (exit {result.ExitCode}). {msg}".Trim());
        }
    }

    private Dictionary<string, string?> GetLatestInstalledModuleVersions(IReadOnlyList<string> names)
    {
        var script = BuildGetInstalledVersionsScript();
        var args = new List<string>(1) { EncodeLines(names) };
        var result = RunScript(script, args, TimeSpan.FromMinutes(2));
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

    private static string BuildGetInstalledVersionsScript()
    {
        return @"
param(
  [string]$NamesB64
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function DecodeLines([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
  return $text -split ""`n"" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Enc([string]$s) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$s))
}

try {
  $names = DecodeLines $NamesB64
  foreach ($n in $names) {
    $mods = Get-Module -ListAvailable -Name $n -ErrorAction SilentlyContinue
    $ver = ''
    if ($mods) {
      $latest = ($mods | Sort-Object Version -Descending | Select-Object -First 1)
      if ($latest -and $latest.Version) { $ver = [string]$latest.Version }
    }
    Write-Output ('PFMOD::ITEM::' + (Enc $n) + '::' + (Enc $ver))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$msg))
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}
";
    }

    private static string BuildInstallModuleScript()
    {
        return @"
param(
  [string]$Name,
  [string]$RequiredVersion,
  [string]$MinimumVersion,
  [string]$Repository
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Enc([string]$s) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$s))
}

try {
  $params = @{
    Name = $Name
    Force = $true
    ErrorAction = 'Stop'
    SkipPublisherCheck = $true
    Scope = 'CurrentUser'
  }
  if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
  if (-not [string]::IsNullOrWhiteSpace($RequiredVersion)) { $params.RequiredVersion = $RequiredVersion }
  elseif (-not [string]::IsNullOrWhiteSpace($MinimumVersion)) { $params.MinimumVersion = $MinimumVersion }

  Install-Module @params | Out-Null
  Write-Output 'PFMOD::INSTALL::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$msg))
  Write-Output ('PFMOD::ERROR::' + $b64)
  exit 1
}
";
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
