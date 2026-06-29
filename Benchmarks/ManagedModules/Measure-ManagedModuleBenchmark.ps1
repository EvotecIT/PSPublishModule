#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('SingleModule', 'GraphAuthentication', 'Graph', 'AzAccounts', 'Az')]
    [string[]] $ScenarioName = @('SingleModule', 'GraphAuthentication', 'Graph', 'AzAccounts', 'Az'),

    [ValidateSet('Find', 'Install', 'Save')]
    [string[]] $Operation = @('Find', 'Install', 'Save'),

    [ValidateSet('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')]
    [string[]] $Engine = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet'),

    [int] $RepeatCount = 1,

    [string] $Repository = 'PSGallery',

    [string] $RepositoryUri = 'https://www.powershellgallery.com/api/v3/index.json',

    [string] $ModuleFastSource = 'https://pwsh.gallery/index.json',

    [string] $OutputPath = '',

    [string] $OutputRoot = '',

    [string] $ManagedModuleBinary = '',

    [string] $HostLabel = '',

    [switch] $Append,

    [switch] $AllowUserProfileInstall,

    [switch] $ListScenarios
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:BenchmarkScriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
} elseif ($MyInvocation.MyCommand.Path) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    Get-Location
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $script:BenchmarkScriptRoot '..\..\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        $driveRoot = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetTempPath())
        if ([string]::IsNullOrWhiteSpace($driveRoot)) {
            $OutputRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'PFMMB'
        } else {
            $OutputRoot = Join-Path $driveRoot 'PFMMB'
        }
    } else {
        $OutputRoot = Join-Path $script:BenchmarkScriptRoot '..\..\Ignore\Benchmarks\ManagedModules\Runs'
    }
}

function Get-BenchmarkScenarios {
    @(
        [pscustomobject]@{ Name = 'SingleModule'; Label = 'PSScriptAnalyzer'; ModuleName = 'PSScriptAnalyzer'; Version = '1.25.0'; AcceptLicense = $false }
        [pscustomobject]@{ Name = 'GraphAuthentication'; Label = 'Graph.Authentication'; ModuleName = 'Microsoft.Graph.Authentication'; Version = '2.38.0'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'Graph'; Label = 'Graph'; ModuleName = 'Microsoft.Graph'; Version = '2.38.0'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'AzAccounts'; Label = 'Az.Accounts'; ModuleName = 'Az.Accounts'; Version = '5.5.0'; AcceptLicense = $true }
        [pscustomobject]@{ Name = 'Az'; Label = 'Az'; ModuleName = 'Az'; Version = '16.0.0'; AcceptLicense = $true }
    )
}

function Get-CurrentHostLabel {
    if (-not [string]::IsNullOrWhiteSpace($HostLabel)) {
        return $HostLabel
    }

    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        return 'Windows PowerShell 5.1'
    }

    'PowerShell {0}' -f $PSVersionTable.PSVersion.Major
}

function Resolve-ManagedModuleBinary {
    if (-not [string]::IsNullOrWhiteSpace($ManagedModuleBinary)) {
        return (Resolve-Path -LiteralPath $ManagedModuleBinary).Path
    }

    $target = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'net472' } else { 'net10.0' }
    $candidate = Join-Path $script:BenchmarkScriptRoot "..\..\PSPublishModule\bin\Release\$target\PSPublishModule.dll"
    if (Test-Path -LiteralPath $candidate) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    ''
}

function Import-ProviderCommand {
    param(
        [string] $CommandName,
        [string] $ModuleName
    )

    if (Get-Command -Name $CommandName -ErrorAction SilentlyContinue) {
        return
    }

    Import-Module -Name $ModuleName -ErrorAction Stop
}

function New-MeasurementResult {
    param(
        [object] $Scenario,
        [string] $OperationName,
        [string] $EngineName,
        [int] $Iteration,
        [string] $Status,
        [double] $Milliseconds,
        [string] $Reason
    )

    $roundedMilliseconds = [Math]::Round($Milliseconds, 2)
    $roundedSeconds = [Math]::Round($Milliseconds / 1000, 3)

    [pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString('o')
        Host = Get-CurrentHostLabel
        Scenario = $Scenario.Name
        ScenarioLabel = $Scenario.Label
        ModuleName = $Scenario.ModuleName
        Version = $Scenario.Version
        Operation = $OperationName
        Engine = $EngineName
        Iteration = $Iteration
        Status = $Status
        Milliseconds = $roundedMilliseconds.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
        Seconds = $roundedSeconds.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)
        Reason = $Reason
    }
}

function Invoke-MeasuredBlock {
    param(
        [scriptblock] $ScriptBlock
    )

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & $ScriptBlock
    $watch.Stop()
    $watch.Elapsed.TotalMilliseconds
}

function Remove-IsolatedModule {
    param(
        [string] $ModuleRoot,
        [string] $ModuleName
    )

    $path = Join-Path $ModuleRoot $ModuleName
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-WithIsolatedModulePath {
    param(
        [string] $ModuleRoot,
        [scriptblock] $ScriptBlock
    )

    New-Item -ItemType Directory -Path $ModuleRoot -Force | Out-Null
    $originalModulePath = $env:PSModulePath
    $separator = [System.IO.Path]::PathSeparator
    if ([string]::IsNullOrWhiteSpace($originalModulePath)) {
        $env:PSModulePath = $ModuleRoot
    } else {
        $env:PSModulePath = $ModuleRoot + $separator + $originalModulePath
    }

    try {
        & $ScriptBlock
    } finally {
        $env:PSModulePath = $originalModulePath
    }
}

function Invoke-BenchmarkCommand {
    param(
        [object] $Scenario,
        [string] $OperationName,
        [string] $EngineName,
        [string] $InstallRoot,
        [string] $SaveRoot
    )

    $name = $Scenario.ModuleName
    $version = $Scenario.Version
    $acceptLicense = [bool]$Scenario.AcceptLicense

    switch ($EngineName) {
        'Managed' {
            $binary = Resolve-ManagedModuleBinary
            if ([string]::IsNullOrWhiteSpace($binary)) {
                throw 'Build PSPublishModule first or pass -ManagedModuleBinary.'
            }

            Import-Module -Name $binary -Force
            switch ($OperationName) {
                'Find' {
                    Find-ManagedModule -Name $name -Repository $RepositoryUri | Out-Null
                }
                'Install' {
                    $parameters = @{
                        Name = $name
                        Repository = $RepositoryUri
                        ModuleRoot = $InstallRoot
                        Force = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.Version = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Install-ManagedModule @parameters | Out-Null
                }
                'Save' {
                    $parameters = @{
                        Name = $name
                        Repository = $RepositoryUri
                        Path = $SaveRoot
                        Force = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.Version = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Save-ManagedModule @parameters | Out-Null
                }
            }
        }
        'ModuleFast' {
            if ($OperationName -ne 'Install') {
                throw 'NotEquivalent: ModuleFast does not expose find/save commands.'
            }
            if ($PSVersionTable.PSEdition -eq 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'7.2') {
                throw 'NotAvailable: ModuleFast requires PowerShell 7.2 or newer.'
            }

            Import-ProviderCommand -CommandName 'Install-ModuleFast' -ModuleName 'ModuleFast'
            $specification = if ([string]::IsNullOrWhiteSpace($version)) { $name } else { '{0}={1}' -f $name, $version }
            $parameters = @{
                Specification = $specification
                Destination = $InstallRoot
                DestinationOnly = $true
                NoPSModulePathUpdate = $true
                NoProfileUpdate = $true
                PassThru = $true
            }
            if (-not [string]::IsNullOrWhiteSpace($ModuleFastSource)) { $parameters.Source = $ModuleFastSource }
            Install-ModuleFast @parameters | Out-Null
        }
        'PSResourceGet' {
            Import-ProviderCommand -CommandName 'Find-PSResource' -ModuleName 'Microsoft.PowerShell.PSResourceGet'
            switch ($OperationName) {
                'Find' {
                    Find-PSResource -Name $name -Repository $Repository | Out-Null
                }
                'Install' {
                    if (-not $AllowUserProfileInstall.IsPresent) {
                        throw 'Skipped: pass -AllowUserProfileInstall to measure native install providers.'
                    }
                    Remove-IsolatedModule -ModuleRoot $InstallRoot -ModuleName $name
                    $parameters = @{
                        Name = $name
                        Repository = $Repository
                        Scope = 'CurrentUser'
                        TrustRepository = $true
                        Reinstall = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.Version = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Invoke-WithIsolatedModulePath -ModuleRoot $InstallRoot -ScriptBlock {
                        Install-PSResource @parameters | Out-Null
                    }
                    Remove-IsolatedModule -ModuleRoot $InstallRoot -ModuleName $name
                }
                'Save' {
                    $parameters = @{
                        Name = $name
                        Repository = $Repository
                        Path = $SaveRoot
                        TrustRepository = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.Version = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Save-PSResource @parameters | Out-Null
                }
            }
        }
        'PowerShellGet' {
            Import-ProviderCommand -CommandName 'Find-Module' -ModuleName 'PowerShellGet'
            switch ($OperationName) {
                'Find' {
                    Find-Module -Name $name -Repository $Repository | Out-Null
                }
                'Install' {
                    if (-not $AllowUserProfileInstall.IsPresent) {
                        throw 'Skipped: pass -AllowUserProfileInstall to measure native install providers.'
                    }
                    Remove-IsolatedModule -ModuleRoot $InstallRoot -ModuleName $name
                    $parameters = @{
                        Name = $name
                        Repository = $Repository
                        Scope = 'CurrentUser'
                        Force = $true
                        AllowClobber = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.RequiredVersion = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Invoke-WithIsolatedModulePath -ModuleRoot $InstallRoot -ScriptBlock {
                        Install-Module @parameters | Out-Null
                    }
                    Remove-IsolatedModule -ModuleRoot $InstallRoot -ModuleName $name
                }
                'Save' {
                    $parameters = @{
                        Name = $name
                        Repository = $Repository
                        Path = $SaveRoot
                        Force = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.RequiredVersion = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Save-Module @parameters | Out-Null
                }
            }
        }
    }
}

if ($ListScenarios.IsPresent) {
    Get-BenchmarkScenarios |
        Where-Object { $ScenarioName -contains $_.Name } |
        Select-Object Name, Label, ModuleName, Version, AcceptLicense
    return
}

if ($RepeatCount -lt 1) {
    throw 'RepeatCount must be greater than zero.'
}

$selectedScenarios = @(Get-BenchmarkScenarios | Where-Object { $ScenarioName -contains $_.Name })
if ($selectedScenarios.Count -eq 0) {
    throw 'No benchmark scenarios were selected.'
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    $driveRoot = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetTempPath())
    $shortTempRoot = if ([string]::IsNullOrWhiteSpace($driveRoot)) {
        Join-Path ([System.IO.Path]::GetTempPath()) 'PFMMT'
    } else {
        Join-Path $driveRoot 'PFMMT'
    }
    New-Item -ItemType Directory -Path $shortTempRoot -Force | Out-Null
    $env:TEMP = $shortTempRoot
    $env:TMP = $shortTempRoot
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($scenario in $selectedScenarios) {
    foreach ($operationName in $Operation) {
        foreach ($engineName in $Engine) {
            foreach ($iteration in 1..$RepeatCount) {
                $runRoot = Join-Path $OutputRoot ('{0}-{1}-{2}-{3}' -f $scenario.Name, $operationName, $engineName, $iteration)
                $installRoot = Join-Path $runRoot 'Install'
                $saveRoot = Join-Path $runRoot 'Save'
                New-Item -ItemType Directory -Path $installRoot, $saveRoot -Force | Out-Null

                try {
                    $elapsed = Invoke-MeasuredBlock {
                        Invoke-BenchmarkCommand -Scenario $scenario -OperationName $operationName -EngineName $engineName -InstallRoot $installRoot -SaveRoot $saveRoot
                    }
                    $results.Add((New-MeasurementResult -Scenario $scenario -OperationName $operationName -EngineName $engineName -Iteration $iteration -Status 'Succeeded' -Milliseconds $elapsed -Reason '')) | Out-Null
                } catch {
                    $message = $_.Exception.Message
                    $status = if ($message.StartsWith('NotEquivalent:', [StringComparison]::OrdinalIgnoreCase)) {
                        'NotEquivalent'
                    } elseif ($message.StartsWith('NotAvailable:', [StringComparison]::OrdinalIgnoreCase)) {
                        'NotAvailable'
                    } elseif ($message.StartsWith('Skipped:', [StringComparison]::OrdinalIgnoreCase)) {
                        'Skipped'
                    } else {
                        'Failed'
                    }
                    $results.Add((New-MeasurementResult -Scenario $scenario -OperationName $operationName -EngineName $engineName -Iteration $iteration -Status $status -Milliseconds 0 -Reason $message)) | Out-Null
                }
            }
        }
    }
}

if ($Append.IsPresent -and (Test-Path -LiteralPath $OutputPath)) {
    $results | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Append
} else {
    $results | Export-Csv -LiteralPath $OutputPath -NoTypeInformation
}

$results
