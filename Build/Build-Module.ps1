[CmdletBinding()]
param(
    [Alias('ConfigurationGateMode')]
    [ValidateSet('Manifest', 'Documentation', 'Build', 'Publish')]
    [string] $RunMode = 'Build',
    [ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto',
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [Alias('NoDotnetBuild')][switch] $NoBuild,
    [Alias('JsonOnly')][switch] $Json,
    [string] $JsonPath,
    [switch] $NoSign,
    [switch] $SignModule,
    [string] $ModuleVersion,
    [string] $PreReleaseTag,
    [string] $CertificateThumbprint = '92e95fb58effa6a4a75e77a33cdd6bfe6dd30f1a',
    [switch] $SignIncludeBinaries,
    [switch] $SignIncludeInternals,
    [switch] $SignIncludeExe,
    [string] $DiagnosticsBaselinePath,
    [switch] $GenerateDiagnosticsBaseline,
    [switch] $UpdateDiagnosticsBaseline,
    [switch] $FailOnNewDiagnostics,
    [ValidateSet('Warning', 'Error')]
    [string] $FailOnDiagnosticsSeverity
)

$script = Join-Path $PSScriptRoot '..\Module\Build\Build-ModuleSelf.ps1'
if (-not (Test-Path -LiteralPath $script)) {
    throw "Build script not found: $script"
}

if ($PSBoundParameters.ContainsKey('JsonPath')) {
    Write-Verbose "JsonPath is ignored in self-build mode."
}

$invoke = @{
    RunMode        = $RunMode
    Framework      = $Framework
    Configuration  = $Configuration
}
if ($NoBuild) { $invoke.NoBuild = $true }
if ($Json) { $invoke.Json = $true }
foreach ($parameterName in @(
        'NoSign',
        'SignModule',
        'ModuleVersion',
        'PreReleaseTag',
        'CertificateThumbprint',
        'SignIncludeBinaries',
        'SignIncludeInternals',
        'SignIncludeExe',
        'DiagnosticsBaselinePath',
        'GenerateDiagnosticsBaseline',
        'UpdateDiagnosticsBaseline',
        'FailOnNewDiagnostics',
        'FailOnDiagnosticsSeverity'
    )) {
    if ($PSBoundParameters.ContainsKey($parameterName)) {
        $invoke[$parameterName] = $PSBoundParameters[$parameterName]
    }
}

& $script @invoke
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
