[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ConfigPath,
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [Alias('ModuleRunMode')]
    [ValidateSet('Manifest', 'Documentation', 'Build', 'Publish')]
    [string] $RunMode = 'Build',
    [switch] $Publish,
    [switch] $Plan,
    [switch] $Validate,
    [switch] $Json,
    [switch] $PackagesOnly,
    [switch] $ModuleOnly,
    [switch] $ToolsOnly,
    [switch] $PublishNuget,
    [Alias('PublishGitHub')]
    [switch] $PublishProjectGitHub,
    [switch] $PublishToolGitHub,
    [ValidateSet('auto', 'net10.0', 'net8.0')]
    [string] $ModuleFramework = 'auto',
    [string] $ModuleVersion,
    [Alias('PreReleaseTag')]
    [string] $ModulePreReleaseTag,
    [Alias('NoBuild')]
    [switch] $ModuleNoDotnetBuild,
    [Alias('NoSign')]
    [switch] $ModuleNoSign,
    [Alias('SignModule')]
    [switch] $ModuleSignModule,
    [Alias('CertificateThumbprint')]
    [string] $ModuleCertificateThumbprint,
    [Alias('SignIncludeBinaries')]
    [switch] $ModuleSignIncludeBinaries,
    [Alias('SignIncludeInternals')]
    [switch] $ModuleSignIncludeInternals,
    [Alias('SignIncludeExe')]
    [switch] $ModuleSignIncludeExe,
    [Alias('DiagnosticsBaselinePath')]
    [string] $ModuleDiagnosticsBaselinePath,
    [Alias('GenerateDiagnosticsBaseline')]
    [switch] $ModuleGenerateDiagnosticsBaseline,
    [Alias('UpdateDiagnosticsBaseline')]
    [switch] $ModuleUpdateDiagnosticsBaseline,
    [Alias('FailOnNewDiagnostics')]
    [switch] $ModuleFailOnNewDiagnostics,
    [Alias('FailOnDiagnosticsSeverity')]
    [ValidateSet('Warning', 'Error')]
    [string] $ModuleFailOnDiagnosticsSeverity,
    [Alias('Targets')]
    [string[]] $Target,
    [Alias('Runtime', 'Rid')]
    [string[]] $Runtimes,
    [Alias('Framework')]
    [string[]] $Frameworks,
    [Alias('Flavor')]
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string[]] $Flavors
)

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'release.json'
}

$operation = if ($Plan) {
    'Plan unified PowerForge release'
} elseif ($Validate) {
    'Validate unified PowerForge release'
} elseif ($Publish -or $PublishNuget -or $PublishProjectGitHub -or $PublishToolGitHub) {
    'Publish unified PowerForge release'
} else {
    'Build unified PowerForge release'
}
$shouldRun = $false
if (-not ($Json -and $WhatIfPreference)) {
    $shouldRun = $PSCmdlet.ShouldProcess($ConfigPath, $operation)
}
if (-not $shouldRun) {
    $skipped = [ordered]@{
        Success = $true
        Skipped = $true
        Reason  = 'ShouldProcess declined the operation.'
    }
    if ($Json) { $skipped | ConvertTo-Json -Depth 5 } else { [pscustomobject] $skipped }
    return
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bootstrapFrameworks = if ($PSEdition -eq 'Desktop') {
    $desktopChildFramework = if ($ModuleFramework -eq 'auto') { 'net8.0' } else { $ModuleFramework }
    @('net472', $desktopChildFramework) | Select-Object -Unique
} elseif ($ModuleFramework -eq 'net10.0') {
    @('net8.0', 'net10.0')
} else {
    @('net8.0')
}
$importFramework = @($bootstrapFrameworks)[0]
$moduleProject = Join-Path $repoRoot 'PSPublishModule\PSPublishModule.csproj'
$moduleBinary = Join-Path $repoRoot "PSPublishModule\bin\$Configuration\$importFramework\PSPublishModule.dll"

try {
    foreach ($framework in $bootstrapFrameworks) {
        $buildOutput = & dotnet build $moduleProject -c $Configuration -f $framework --nologo --verbosity quiet 2>&1
        if ($LASTEXITCODE -ne 0) {
            $buildDetails = ($buildOutput | Out-String).Trim()
            throw "Failed to bootstrap PSPublishModule ($framework, exit code $LASTEXITCODE).`n$buildDetails"
        }
    }

    Get-Module PSPublishModule -All -ErrorAction SilentlyContinue |
        Remove-Module -Force -ErrorAction SilentlyContinue
    Import-Module $moduleBinary -Force -ErrorAction Stop -Verbose:$false

    $invokeParams = @{}
    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        if ($entry.Key -notin @('Json', 'Publish', 'RunMode', 'WhatIf', 'Confirm')) {
            $invokeParams[$entry.Key] = $entry.Value
        }
    }
    foreach ($switchName in @(
            'ModuleSignIncludeBinaries',
            'ModuleSignIncludeInternals',
            'ModuleSignIncludeExe',
            'ModuleGenerateDiagnosticsBaseline',
            'ModuleUpdateDiagnosticsBaseline',
            'ModuleFailOnNewDiagnostics'
        )) {
        if ($PSBoundParameters.ContainsKey($switchName)) {
            $invokeParams[$switchName] = [bool] $PSBoundParameters[$switchName]
        }
    }
    $invokeParams.ConfigPath = $ConfigPath
    $invokeParams.Configuration = $Configuration
$invokeParams.ModuleRunMode = if ($Publish -or $PublishNuget -or $PublishProjectGitHub) { 'Publish' } else { $RunMode }
    $invokeParams.ErrorAction = 'Stop'
    if ($Publish) {
        $invokeParams.PublishNuget = $true
        if (-not $ModuleNoSign) {
            $invokeParams.ModuleSignModule = $true
        }
    }
    if ($Json) {
        $invokeParams.NoInteractive = $true
        $invokeParams.WarningAction = 'SilentlyContinue'
    }

    $result = Invoke-PowerForgeRelease @invokeParams
    if ($null -eq $result) {
        throw 'Unified release execution returned no result.'
    }
    if ($Json) { $result | ConvertTo-Json -Depth 20 } else { $result }
} catch {
    if ($Json) {
        [ordered]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
        } | ConvertTo-Json -Depth 5
    } else {
        Write-Error $_
    }
    exit 1
}
