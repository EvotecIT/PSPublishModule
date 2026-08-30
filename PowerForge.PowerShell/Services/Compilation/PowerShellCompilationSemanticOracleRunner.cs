using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Executes a script in an exact external PowerShell host and returns a normalized, portable semantic observation.
/// The runner does not load compiler implementation assemblies into the oracle host.
/// </summary>
public sealed class PowerShellCompilationSemanticOracleRunner
{
    private const string WrapperSource = """
param([Parameter(Mandatory)][string] $ConfigPath)
$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$culture = [System.Globalization.CultureInfo]::GetCultureInfo([string] $config.Culture)
[System.Globalization.CultureInfo]::CurrentCulture = $culture
[System.Globalization.CultureInfo]::CurrentUICulture = $culture

function Get-FileSnapshot([string] $Root) {
    $snapshot = @{}
    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) { return $snapshot }
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName) {
        $relative = $file.FullName.Substring($Root.Length).TrimStart([char[]]@('\','/')).Replace('\','/')
        $snapshot[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $snapshot
}

function Get-Lines([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @() }
    return @(Get-Content -LiteralPath $Path | ForEach-Object { [string] $_ })
}

$before = Get-FileSnapshot ([string] $config.FileSystemRoot)
$errorPath = Join-Path $PSScriptRoot 'error.txt'
$warningPath = Join-Path $PSScriptRoot 'warning.txt'
$verbosePath = Join-Path $PSScriptRoot 'verbose.txt'
$debugPath = Join-Path $PSScriptRoot 'debug.txt'
$informationPath = Join-Path $PSScriptRoot 'information.txt'
$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$global:LASTEXITCODE = $null
$invocationArguments = @($config.Arguments)
$success = @(& ([string] $config.ScriptPath) @invocationArguments 2> $errorPath 3> $warningPath 4> $verbosePath 5> $debugPath 6> $informationPath)
$scriptExitCode = if ($global:LASTEXITCODE -is [int]) { [int] $global:LASTEXITCODE } else { $null }
$ErrorActionPreference = $previousErrorAction
$after = Get-FileSnapshot ([string] $config.FileSystemRoot)

$effects = [System.Collections.Generic.List[string]]::new()
foreach ($path in @($before.Keys + $after.Keys | Sort-Object -Unique)) {
    if (-not $before.ContainsKey($path)) { $effects.Add("Added:${path}:$($after[$path])"); continue }
    if (-not $after.ContainsKey($path)) { $effects.Add("Removed:${path}:$($before[$path])"); continue }
    if ($before[$path] -ne $after[$path]) { $effects.Add("Modified:${path}:$($after[$path])") }
}

$values = foreach ($item in $success) {
    $isNull = $null -eq $item
    $properties = foreach ($name in @($config.ObservedPropertyNames | Sort-Object -Unique)) {
        $property = if ($isNull) { $null } else { $item.PSObject.Properties[[string] $name] }
        if ($null -eq $property) { continue }
        $propertyValue = $property.Value
        [ordered]@{
            Name = [string] $name
            Value = if ($null -eq $propertyValue) { '' } else { [string] $propertyValue }
            TypeName = if ($null -eq $propertyValue) { '' } else { $propertyValue.GetType().FullName }
            IsNull = $null -eq $propertyValue
        }
    }
    [ordered]@{
        Value = if ($isNull) { '' } else { [string] $item }
        TypeName = if ($isNull) { '' } else { $item.GetType().FullName }
        IsNull = $isNull
        Properties = @($properties)
    }
}

$envelope = [ordered]@{
    SchemaVersion = 1
    ProfileId = [string] $config.ProfileId
    ExecutionSurface = [string] $config.ExecutionSurface
    HostVersion = $PSVersionTable.PSVersion.ToString()
    PowerShellEdition = [string] $PSVersionTable.PSEdition
    OperatingSystem = if ($PSVersionTable.PSVersion.Major -le 5 -or $IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } elseif ($IsMacOS) { 'macOS' } else { 'Unknown' }
    Architecture = if ($PSVersionTable.PSVersion.Major -le 5) { [string] $env:PROCESSOR_ARCHITECTURE } else { [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() }
    Culture = [System.Globalization.CultureInfo]::CurrentCulture.Name
    Success = @($values)
    Information = @(Get-Lines $informationPath)
    Warnings = @(Get-Lines $warningPath)
    Verbose = @(Get-Lines $verbosePath)
    Debug = @(Get-Lines $debugPath)
    Errors = @(Get-Lines $errorPath)
    ExitCode = $scriptExitCode
    FileSystemEffects = @($effects)
    ProcessEffects = @()
}
$envelope | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath ([string] $config.OutputPath) -Encoding utf8
""";

    /// <summary>Executes one black-box observation in the external host named by the selected profile.</summary>
    public PowerShellCompilationSemanticOracleEnvelope Observe(PowerShellCompilationSemanticOracleRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (!File.Exists(request.ScriptPath)) throw new FileNotFoundException("Semantic-oracle script was not found.", request.ScriptPath);
        if (request.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(request.TimeoutSeconds));
        var profile = PowerShellCompilationSemanticOracleCatalog.Get(request.ProfileId);
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeSemanticOracle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wrapperPath = Path.Combine(root, "Observe.ps1");
            var configPath = Path.Combine(root, "request.json");
            var outputPath = Path.Combine(root, "observation.json");
            File.WriteAllText(wrapperPath, WrapperSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var config = new
            {
                request.ProfileId,
                request.ScriptPath,
                Arguments = request.Arguments ?? Array.Empty<string>(),
                ObservedPropertyNames = NormalizePropertyNames(request.ObservedPropertyNames),
                Culture = string.IsNullOrWhiteSpace(request.Culture) ? "en-US" : request.Culture.Trim(),
                FileSystemRoot = string.IsNullOrWhiteSpace(request.FileSystemRoot) ? string.Empty : Path.GetFullPath(request.FileSystemRoot),
                ExecutionSurface = string.IsNullOrWhiteSpace(request.ExecutionSurface) ? "Interpreted" : request.ExecutionSurface.Trim(),
                OutputPath = outputPath
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(config), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
                    profile.HostExecutable,
                    root,
                    new[] { "-NoProfile", "-NonInteractive", "-File", wrapperPath, "-ConfigPath", configPath },
                    TimeSpan.FromSeconds(request.TimeoutSeconds)))
                .GetAwaiter()
                .GetResult();
            if (run.TimedOut)
                throw new TimeoutException($"Semantic oracle '{profile.ProfileId}' exceeded {request.TimeoutSeconds} seconds.");
            if (run.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException($"Semantic oracle '{profile.ProfileId}' failed with exit code {run.ExitCode}. {Bound(run.StdErr)}");

            var envelope = JsonSerializer.Deserialize<PowerShellCompilationSemanticOracleEnvelope>(File.ReadAllText(outputPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Semantic oracle produced an empty observation.");
            ValidateHost(profile, envelope);
            return envelope;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string[] NormalizePropertyNames(IEnumerable<string>? names)
    {
        var normalized = (names ?? Array.Empty<string>())
            .Select(static name => name?.Trim() ?? string.Empty)
            .Where(static name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var forbidden = normalized.FirstOrDefault(static name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SessionId", StringComparison.OrdinalIgnoreCase));
        if (forbidden is not null)
            throw new ArgumentException($"Portable semantic observations forbid sensitive or live-runtime property '{forbidden}'.", nameof(names));
        return normalized;
    }

    private static void ValidateHost(
        PowerShellCompilationSemanticOracleProfile profile,
        PowerShellCompilationSemanticOracleEnvelope envelope)
    {
        if (!string.Equals(profile.ProfileId, envelope.ProfileId, StringComparison.Ordinal))
            throw new InvalidOperationException("Semantic oracle returned the wrong profile identity.");
        if (!string.Equals(profile.PowerShellEdition, envelope.PowerShellEdition, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Semantic profile '{profile.ProfileId}' requires PowerShell edition '{profile.PowerShellEdition}', but host reported '{envelope.PowerShellEdition}'.");
        if (!Version.TryParse(envelope.HostVersion, out var version))
            throw new InvalidOperationException($"Semantic oracle reported invalid host version '{envelope.HostVersion}'.");
        var expectedMajor = profile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51 ? 5 : 7;
        var expectedMinor = profile.ProfileId == PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId ? 4
            : profile.ProfileId == PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId ? 6
            : 1;
        if (version.Major != expectedMajor || version.Minor != expectedMinor)
            throw new InvalidOperationException($"Semantic profile '{profile.ProfileId}' does not accept host version '{version}'.");
        if (profile.OperatingSystem != "Any" && !string.Equals(profile.OperatingSystem, envelope.OperatingSystem, StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException($"Semantic profile '{profile.ProfileId}' requires {profile.OperatingSystem}, but host reported {envelope.OperatingSystem}.");
    }

    private static string Bound(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= 4096 ? value : value.Substring(value.Length - 4096);
}
