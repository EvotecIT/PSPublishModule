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

$moduleBuildScript = Join-Path $PSScriptRoot '..\Module\Build\Build-Module.ps1'
$releaseBuildScript = Join-Path $PSScriptRoot 'Build-Project.ps1'
foreach ($requiredScript in @($moduleBuildScript, $releaseBuildScript)) {
    if (-not (Test-Path -LiteralPath $requiredScript)) {
        throw "Build script not found: $requiredScript"
    }
}

if ($RunMode -eq 'Publish') {
    $invoke = @{
        Configuration = $Configuration
        ModuleRunMode = 'Publish'
        PublishNuget = $true
    }
    if ($NoBuild) { $invoke.ModuleNoDotnetBuild = $true }
    if ($Json) { $invoke.Json = $true }
    if ($PSBoundParameters.ContainsKey('ModuleVersion')) { $invoke.ModuleVersion = $ModuleVersion }
    if ($PSBoundParameters.ContainsKey('PreReleaseTag')) { $invoke.ModulePreReleaseTag = $PreReleaseTag }
    foreach ($parameterName in @(
            'NoSign',
            'SignModule',
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
        if (-not $PSBoundParameters.ContainsKey($parameterName)) { continue }
        switch ($parameterName) {
            'NoSign' { $invoke.ModuleNoSign = $NoSign.IsPresent }
            'SignModule' { $invoke.ModuleSignModule = $SignModule.IsPresent }
            default { $invoke[$parameterName] = $PSBoundParameters[$parameterName] }
        }
    }
    if ($Framework -ne 'auto') { $invoke.ModuleFramework = $Framework }
    & $releaseBuildScript @invoke
} else {
    $invoke = @{
        RunMode = $RunMode
        Framework = $Framework
        Configuration = $Configuration
    }
    if ($NoBuild) { $invoke.NoDotnetBuild = $true }
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

    if ($Json) {
        if ($PSBoundParameters.ContainsKey('JsonPath')) {
            Write-Verbose 'JsonPath is ignored when -Json requests an executed build result.'
        }
        $invoke.NoInteractive = $true
        $invoke.NoExitCode = $true
        $invoke.Quiet = $true
        $invoke.PassThru = $true
        $invoke.ErrorAction = 'Stop'
        try {
            $result = & $moduleBuildScript @invoke 3>$null 4>$null 6>$null
            if ($null -eq $result) {
                throw 'The module build completed without returning a structured result.'
            }
            $result | ConvertTo-Json -Depth 20
        } catch {
            [ordered]@{
                Success = $false
                ErrorMessage = $_.Exception.Message
            } | ConvertTo-Json -Depth 5
            exit 1
        }
    } else {
        & $moduleBuildScript @invoke
    }
}

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
