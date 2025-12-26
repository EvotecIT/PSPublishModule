using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// A minimal representation of a PowerShell resource returned by PSResourceGet.
/// </summary>
public sealed class PSResourceInfo
{
    /// <summary>Name of the resource.</summary>
    public string Name { get; }
    /// <summary>Resolved resource version.</summary>
    public string Version { get; }
    /// <summary>Repository name (when available).</summary>
    public string? Repository { get; }
    /// <summary>Author (when available).</summary>
    public string? Author { get; }
    /// <summary>Description (when available).</summary>
    public string? Description { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PSResourceInfo(string name, string version, string? repository, string? author, string? description)
    {
        Name = name;
        Version = version;
        Repository = repository;
        Author = author;
        Description = description;
    }
}

/// <summary>
/// Options for <c>Find-PSResource</c>.
/// </summary>
public sealed class PSResourceFindOptions
{
    /// <summary>Resource names to search for.</summary>
    public IReadOnlyList<string> Names { get; }
    /// <summary>Version constraint string (PSResourceGet -Version value).</summary>
    public string? Version { get; }
    /// <summary>Whether to include prerelease versions.</summary>
    public bool Prerelease { get; }
    /// <summary>Repository names to search in.</summary>
    public IReadOnlyList<string> Repositories { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourceFindOptions(IReadOnlyList<string> names, string? version = null, bool prerelease = false, IReadOnlyList<string>? repositories = null)
    {
        Names = names ?? Array.Empty<string>();
        Version = version;
        Prerelease = prerelease;
        Repositories = repositories ?? Array.Empty<string>();
    }
}

/// <summary>
/// Options for <c>Publish-PSResource</c>.
/// </summary>
public sealed class PSResourcePublishOptions
{
    /// <summary>Path to a module folder (<c>-Path</c>) or a .nupkg file (<c>-NupkgPath</c>).</summary>
    public string Path { get; }
    /// <summary>When true, uses <c>-NupkgPath</c> instead of <c>-Path</c>.</summary>
    public bool IsNupkg { get; }
    /// <summary>Repository to publish to.</summary>
    public string? Repository { get; }
    /// <summary>API key for the repository.</summary>
    public string? ApiKey { get; }
    /// <summary>DestinationPath passed to Publish-PSResource.</summary>
    public string? DestinationPath { get; }
    /// <summary>Skip dependency check.</summary>
    public bool SkipDependenciesCheck { get; }
    /// <summary>Skip module manifest validation.</summary>
    public bool SkipModuleManifestValidate { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourcePublishOptions(
        string path,
        bool isNupkg = false,
        string? repository = null,
        string? apiKey = null,
        string? destinationPath = null,
        bool skipDependenciesCheck = false,
        bool skipModuleManifestValidate = false)
    {
        Path = path;
        IsNupkg = isNupkg;
        Repository = repository;
        ApiKey = apiKey;
        DestinationPath = destinationPath;
        SkipDependenciesCheck = skipDependenciesCheck;
        SkipModuleManifestValidate = skipModuleManifestValidate;
    }
}

/// <summary>
/// Options for <c>Install-PSResource</c>.
/// </summary>
public sealed class PSResourceInstallOptions
{
    /// <summary>Resource name to install.</summary>
    public string Name { get; }
    /// <summary>Version constraint string (PSResourceGet -Version value).</summary>
    public string? Version { get; }
    /// <summary>Repository to install from (optional).</summary>
    public string? Repository { get; }
    /// <summary>Install scope (CurrentUser/AllUsers). Default: CurrentUser.</summary>
    public string Scope { get; }
    /// <summary>Whether to include prerelease versions.</summary>
    public bool Prerelease { get; }
    /// <summary>Whether to reinstall even if already installed.</summary>
    public bool Reinstall { get; }
    /// <summary>Whether to trust repository (avoid prompts).</summary>
    public bool TrustRepository { get; }
    /// <summary>Whether to skip dependency checks.</summary>
    public bool SkipDependencyCheck { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourceInstallOptions(
        string name,
        string? version = null,
        string? repository = null,
        string scope = "CurrentUser",
        bool prerelease = false,
        bool reinstall = false,
        bool trustRepository = true,
        bool skipDependencyCheck = false)
    {
        Name = name;
        Version = version;
        Repository = repository;
        Scope = string.IsNullOrWhiteSpace(scope) ? "CurrentUser" : scope;
        Prerelease = prerelease;
        Reinstall = reinstall;
        TrustRepository = trustRepository;
        SkipDependencyCheck = skipDependencyCheck;
    }
}

/// <summary>
/// Options for <c>Save-PSResource</c>.
/// </summary>
public sealed class PSResourceSaveOptions
{
    /// <summary>Resource name to save.</summary>
    public string Name { get; }
    /// <summary>Destination path passed to <c>-Path</c>.</summary>
    public string DestinationPath { get; }
    /// <summary>Version constraint string (PSResourceGet -Version value).</summary>
    public string? Version { get; }
    /// <summary>Repository to save from (optional).</summary>
    public string? Repository { get; }
    /// <summary>Whether to include prerelease versions.</summary>
    public bool Prerelease { get; }
    /// <summary>Whether to trust repository (avoid prompts).</summary>
    public bool TrustRepository { get; }
    /// <summary>Whether to skip dependency checks.</summary>
    public bool SkipDependencyCheck { get; }
    /// <summary>Whether to accept license prompts.</summary>
    public bool AcceptLicense { get; }

    /// <summary>
    /// Creates a new options instance.
    /// </summary>
    public PSResourceSaveOptions(
        string name,
        string destinationPath,
        string? version = null,
        string? repository = null,
        bool prerelease = false,
        bool trustRepository = true,
        bool skipDependencyCheck = true,
        bool acceptLicense = true)
    {
        Name = name;
        DestinationPath = destinationPath;
        Version = version;
        Repository = repository;
        Prerelease = prerelease;
        TrustRepository = trustRepository;
        SkipDependencyCheck = skipDependencyCheck;
        AcceptLicense = acceptLicense;
    }
}

/// <summary>
/// Out-of-process wrapper for PSResourceGet (Find-PSResource / Publish-PSResource).
/// </summary>
public sealed class PSResourceGetClient
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new client using the provided runner and logger.
    /// </summary>
    public PSResourceGetClient(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Finds PowerShell resources using PSResourceGet.
    /// </summary>
    public IReadOnlyList<PSResourceInfo> Find(PSResourceFindOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var names = (options.Names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0) throw new ArgumentException("At least one name is required.", nameof(options));

        var repos = (options.Repositories ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var script = BuildFindScript();
        var args = new List<string>(4)
        {
            EncodeLines(names),
            options.Version ?? string.Empty,
            EncodeLines(repos),
            options.Prerelease ? "1" : "0"
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(2));
        var items = ParseFindOutput(result.StdOut);

        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Find-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            throw new InvalidOperationException(full);
        }

        return items;
    }

    /// <summary>
    /// Publishes a PowerShell resource using PSResourceGet.
    /// </summary>
    public void Publish(PSResourcePublishOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Path)) throw new ArgumentException("Path is required.", nameof(options));

        var script = BuildPublishScript();
        var args = new List<string>(7)
        {
            options.Path,
            options.IsNupkg ? "1" : "0",
            options.Repository ?? string.Empty,
            options.ApiKey ?? string.Empty,
            options.DestinationPath ?? string.Empty,
            options.SkipDependenciesCheck ? "1" : "0",
            options.SkipModuleManifestValidate ? "1" : "0"
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Publish-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            throw new InvalidOperationException(full);
        }
    }

    /// <summary>
    /// Installs a PowerShell resource using PSResourceGet.
    /// </summary>
    public void Install(PSResourceInstallOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Name is required.", nameof(options));

        var script = BuildInstallScript();
        var args = new List<string>(8)
        {
            options.Name,
            options.Version ?? string.Empty,
            options.Repository ?? string.Empty,
            options.Scope ?? string.Empty,
            options.Prerelease ? "1" : "0",
            options.Reinstall ? "1" : "0",
            options.TrustRepository ? "1" : "0",
            options.SkipDependencyCheck ? "1" : "0"
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Install-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut))
                _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr))
                _logger.Verbose(result.StdErr.Trim());
            throw new InvalidOperationException(full);
        }
    }

    /// <summary>
    /// Saves a PowerShell resource using PSResourceGet (<c>Save-PSResource</c>).
    /// </summary>
    public IReadOnlyList<PSResourceInfo> Save(PSResourceSaveOptions options, TimeSpan? timeout = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("Name is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DestinationPath)) throw new ArgumentException("DestinationPath is required.", nameof(options));

        var dest = Path.GetFullPath(options.DestinationPath);
        Directory.CreateDirectory(dest);

        var script = BuildSaveScript();
        var args = new List<string>(8)
        {
            options.Name,
            options.Version ?? string.Empty,
            options.Repository ?? string.Empty,
            dest,
            options.Prerelease ? "1" : "0",
            options.TrustRepository ? "1" : "0",
            options.SkipDependencyCheck ? "1" : "0",
            options.AcceptLicense ? "1" : "0"
        };

        var result = RunScript(script, args, timeout ?? TimeSpan.FromMinutes(10));
        var items = ParseSaveOutput(result.StdOut);

        if (result.ExitCode != 0)
        {
            var message = TryExtractError(result.StdOut) ?? result.StdErr;
            var full = $"Save-PSResource failed (exit {result.ExitCode}). {message}".Trim();
            _logger.Error(full);
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut))
                _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr))
                _logger.Verbose(result.StdErr.Trim());
            throw new InvalidOperationException(full);
        }

        return items;
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PowerForge", "psresourceget");
        Directory.CreateDirectory(tempDir);
        var scriptPath = System.IO.Path.Combine(tempDir, $"psresourceget_{Guid.NewGuid():N}.ps1");
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

    private static IReadOnlyList<PSResourceInfo> ParseFindOutput(string stdout)
    {
        var list = new List<PSResourceInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPSRG::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 7) continue;

            var name = Decode(parts[2]);
            var version = Decode(parts[3]);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
            var repo = EmptyToNull(Decode(parts[4]));
            var author = EmptyToNull(Decode(parts[5]));
            var desc = EmptyToNull(Decode(parts[6]));
            list.Add(new PSResourceInfo(name, version, repo, author, desc));
        }
        return list;
    }

    private static IReadOnlyList<PSResourceInfo> ParseSaveOutput(string stdout)
    {
        var list = new List<PSResourceInfo>();
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPSRG::SAVE::ITEM::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 5) continue;

            var name = Decode(parts[3]);
            var version = Decode(parts[4]);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
            list.Add(new PSResourceInfo(name, version, repository: null, author: null, description: null));
        }
        return list;
    }

    private static string? TryExtractError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFPSRG::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFPSRG::ERROR::".Length);
            var msg = Decode(b64);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
        return null;
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

    private static string? EmptyToNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string BuildFindScript()
    {
        return @"
param(
  [string]$NamesB64,
  [string]$Version,
  [string]$ReposB64,
  [string]$PrereleaseFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function DecodeLines([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
  return $text -split ""`n"" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

try {
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

$names = DecodeLines $NamesB64
$repos = DecodeLines $ReposB64
$prerelease = ($PrereleaseFlag -eq '1')

$params = @{ ErrorAction = 'Stop' }
if ($names.Count -gt 0) { $params.Name = $names }
if (-not [string]::IsNullOrWhiteSpace($Version)) { $params.Version = $Version }
if ($repos.Count -gt 0) { $params.Repository = $repos }
if ($prerelease) { $params.Prerelease = $true }

try {
  $results = Find-PSResource @params
  foreach ($r in $results) {
    $name = [string]$r.Name
    $ver = [string]$r.Version
    $repo = [string]$r.Repository
    $author = [string]$r.Author
    $desc = [string]$r.Description
    $fields = @($name, $ver, $repo, $author, $desc) | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFPSRG::ITEM::' + ($fields -join '::'))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}
";
    }

    private static string BuildPublishScript()
    {
        return @"
param(
  [string]$Path,
  [string]$IsNupkgFlag,
  [string]$Repository,
  [string]$ApiKey,
  [string]$DestinationPath,
  [string]$SkipDependenciesFlag,
  [string]$SkipManifestFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

$params = @{ ErrorAction = 'Stop' }
if ($IsNupkgFlag -eq '1') { $params.NupkgPath = $Path } else { $params.Path = $Path }
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $params.ApiKey = $ApiKey }
if (-not [string]::IsNullOrWhiteSpace($DestinationPath)) { $params.DestinationPath = $DestinationPath }
if ($SkipDependenciesFlag -eq '1') { $params.SkipDependenciesCheck = $true }
if ($SkipManifestFlag -eq '1') { $params.SkipModuleManifestValidate = $true }

try {
  Publish-PSResource @params | Out-Null
  Write-Output 'PFPSRG::PUBLISH::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($msg))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}
";
    }

    private static string BuildInstallScript()
    {
        return @"
param(
  [string]$Name,
  [string]$Version,
  [string]$Repository,
  [string]$Scope,
  [string]$PrereleaseFlag,
  [string]$ReinstallFlag,
  [string]$TrustRepositoryFlag,
  [string]$SkipDependencyFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(($msg)))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

$params = @{ ErrorAction = 'Stop' }
$params.Name = $Name
if (-not [string]::IsNullOrWhiteSpace($Version)) { $params.Version = $Version }
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
if (-not [string]::IsNullOrWhiteSpace($Scope)) { $params.Scope = $Scope }
if ($PrereleaseFlag -eq '1') { $params.Prerelease = $true }
if ($ReinstallFlag -eq '1') { $params.Reinstall = $true }
if ($TrustRepositoryFlag -eq '1') { $params.TrustRepository = $true }
if ($SkipDependencyFlag -eq '1') { $params.SkipDependencyCheck = $true }

try {
  Install-PSResource @params | Out-Null
  Write-Output 'PFPSRG::INSTALL::OK'
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(($msg)))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}
";
    }

    private static string BuildSaveScript()
    {
        return @"
param(
  [string]$Name,
  [string]$Version,
  [string]$Repository,
  [string]$Path,
  [string]$PrereleaseFlag,
  [string]$TrustRepositoryFlag,
  [string]$SkipDependencyFlag,
  [string]$AcceptLicenseFlag
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
  Import-Module Microsoft.PowerShell.PSResourceGet -ErrorAction Stop | Out-Null
} catch {
  $msg = 'Microsoft.PowerShell.PSResourceGet not available: ' + $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(($msg)))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 3
}

$params = @{ ErrorAction = 'Stop' }
$params.Name = $Name
$params.Path = $Path
if (-not [string]::IsNullOrWhiteSpace($Version)) { $params.Version = $Version }
if (-not [string]::IsNullOrWhiteSpace($Repository)) { $params.Repository = $Repository }
if ($PrereleaseFlag -eq '1') { $params.Prerelease = $true }
if ($TrustRepositoryFlag -eq '1') { $params.TrustRepository = $true }
if ($SkipDependencyFlag -eq '1') { $params.SkipDependencyCheck = $true }
if ($AcceptLicenseFlag -eq '1') { $params.AcceptLicense = $true }

try {
  $saved = Save-PSResource @params -PassThru
  foreach ($r in @($saved)) {
    $name = [string]$r.Name
    $ver = [string]$r.Version
    $fields = @($name, $ver) | ForEach-Object { [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$_)) }
    Write-Output ('PFPSRG::SAVE::ITEM::' + ($fields -join '::'))
  }
  exit 0
} catch {
  $msg = $_.Exception.Message
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(($msg)))
  Write-Output ('PFPSRG::ERROR::' + $b64)
  exit 1
}
";
    }
}
