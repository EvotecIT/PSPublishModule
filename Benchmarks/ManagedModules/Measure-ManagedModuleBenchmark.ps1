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

    [switch] $SkipNativeCurrentUserInstall,

    [switch] $ListScenarios
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$invocationPath = if ($MyInvocation.MyCommand -and $MyInvocation.MyCommand.PSObject.Properties['Path']) {
    $MyInvocation.MyCommand.Path
} else {
    ''
}
$script:BenchmarkScriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
} elseif (-not [string]::IsNullOrWhiteSpace($invocationPath)) {
    Split-Path -Parent $invocationPath
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

    $target = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'net472' } else { 'net8.0' }
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
        [string] $Reason,
        [object] $ManagedResult = $null
    )

    $roundedMilliseconds = [Math]::Round($Milliseconds, 2)
    $roundedSeconds = [Math]::Round($Milliseconds / 1000, 3)

    $managedMetrics = New-ManagedBenchmarkMetrics -ManagedResult $ManagedResult
    $row = [ordered]@{
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

    foreach ($metric in $managedMetrics.GetEnumerator()) {
        $row[$metric.Key] = $metric.Value
    }

    [pscustomobject]$row
}

function Format-BenchmarkNumber {
    param([double] $Value)

    [Math]::Round($Value, 2).ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-BenchmarkMilliseconds {
    param([object] $Value)

    if ($null -eq $Value) {
        return 0
    }

    if ($Value -is [TimeSpan]) {
        return $Value.TotalMilliseconds
    }

    if ($Value.PSObject.Properties['TotalMilliseconds']) {
        return [double]$Value.TotalMilliseconds
    }

    0
}

function Add-ManagedInstallResultNode {
    param(
        [object] $Node,
        [System.Collections.Generic.List[object]] $Nodes
    )

    if ($null -eq $Node) {
        return
    }

    $Nodes.Add($Node) | Out-Null
    if ($Node.PSObject.Properties['DependencyResults'] -and $null -ne $Node.DependencyResults) {
        foreach ($dependency in @($Node.DependencyResults)) {
            Add-ManagedInstallResultNode -Node $dependency -Nodes $Nodes
        }
    }
}

function New-ManagedBenchmarkMetrics {
    param([object] $ManagedResult)

    $empty = [ordered]@{
        ManagedPackageCount = ''
        ManagedInstalledCount = ''
        ManagedAlreadyInstalledCount = ''
        ManagedFileCount = ''
        ManagedDownloadedBytes = ''
        ManagedExtractedBytes = ''
        ManagedPackageRepositoryRequests = ''
        ManagedPackageRepositoryRedirects = ''
        ManagedVersionResolutionMillisecondsSum = ''
        ManagedVersionSelectionWaitMillisecondsSum = ''
        ManagedDownloadMillisecondsSum = ''
        ManagedDownloadMillisecondsMax = ''
        ManagedExtractionMillisecondsSum = ''
        ManagedExtractionMillisecondsMax = ''
        ManagedExtractionCacheLockWaitMillisecondsSum = ''
        ManagedDependencyMillisecondsRoot = ''
        ManagedDependencyQueueWaitMillisecondsSum = ''
        ManagedDependencyBranchMillisecondsMax = ''
        ManagedPromotionMillisecondsSum = ''
        ManagedPromotionMillisecondsMax = ''
        ManagedPromotionLockWaitMillisecondsSum = ''
        ManagedInstallLockWaitMillisecondsSum = ''
        ManagedCoalescedWaitMillisecondsSum = ''
        ManagedAuthenticodeCheckedFiles = ''
        ManagedAuthenticodeCatalogFiles = ''
    }

    if ($null -eq $ManagedResult -or -not $ManagedResult.PSObject.Properties['DependencyResults']) {
        return $empty
    }

    $nodes = [System.Collections.Generic.List[object]]::new()
    Add-ManagedInstallResultNode -Node $ManagedResult -Nodes $nodes

    $installedCount = 0
    $alreadyInstalledCount = 0
    $fileCount = 0
    [long] $downloadedBytes = 0
    [long] $extractedBytes = 0
    [long] $packageRepositoryRequests = 0
    [long] $packageRepositoryRedirects = 0
    [double] $versionResolutionMillisecondsSum = 0
    [double] $versionSelectionWaitMillisecondsSum = 0
    [double] $downloadMillisecondsSum = 0
    [double] $downloadMillisecondsMax = 0
    [double] $extractionMillisecondsSum = 0
    [double] $extractionMillisecondsMax = 0
    [double] $extractionCacheLockWaitMillisecondsSum = 0
    [double] $dependencyQueueWaitMillisecondsSum = 0
    [double] $dependencyBranchMillisecondsMax = 0
    [double] $promotionMillisecondsSum = 0
    [double] $promotionMillisecondsMax = 0
    [double] $promotionLockWaitMillisecondsSum = 0
    [double] $installLockWaitMillisecondsSum = 0
    [double] $coalescedWaitMillisecondsSum = 0
    $authenticodeCheckedFiles = 0
    $authenticodeCatalogFiles = 0

    foreach ($node in $nodes) {
        $status = [string]$node.Status
        if ($status.Equals('Installed', [StringComparison]::OrdinalIgnoreCase)) {
            $installedCount++
        } elseif ($status.Equals('AlreadyInstalled', [StringComparison]::OrdinalIgnoreCase)) {
            $alreadyInstalledCount++
        }

        $fileCount += [int]$node.FileCount
        $extractedBytes += [long]$node.ExtractedBytes
        $packageRepositoryRequests += [long]$node.PackageRepositoryRequestCount
        $packageRepositoryRedirects += [long]$node.PackageRepositoryRedirectCount

        if ($null -ne $node.Download) {
            $downloadedBytes += [long]$node.Download.BytesWritten
        }

        $versionResolutionMillisecondsSum += ConvertTo-BenchmarkMilliseconds $node.VersionResolutionElapsed
        $versionSelectionWaitMillisecondsSum += ConvertTo-BenchmarkMilliseconds $node.VersionSelectionWaitElapsed

        $downloadMilliseconds = ConvertTo-BenchmarkMilliseconds $node.DownloadElapsed
        $downloadMillisecondsSum += $downloadMilliseconds
        if ($downloadMilliseconds -gt $downloadMillisecondsMax) {
            $downloadMillisecondsMax = $downloadMilliseconds
        }

        $extractionMilliseconds = ConvertTo-BenchmarkMilliseconds $node.ExtractionElapsed
        $extractionMillisecondsSum += $extractionMilliseconds
        if ($extractionMilliseconds -gt $extractionMillisecondsMax) {
            $extractionMillisecondsMax = $extractionMilliseconds
        }

        $extractionCacheLockWaitMillisecondsSum += ConvertTo-BenchmarkMilliseconds $node.ExtractionCacheLockWaitElapsed
        $dependencyQueueWaitMillisecondsSum += ConvertTo-BenchmarkMilliseconds $node.DependencyQueueWaitElapsed

        $dependencyBranchMilliseconds = ConvertTo-BenchmarkMilliseconds $node.DependencyBranchElapsed
        if ($dependencyBranchMilliseconds -gt $dependencyBranchMillisecondsMax) {
            $dependencyBranchMillisecondsMax = $dependencyBranchMilliseconds
        }

        $promotionMilliseconds = ConvertTo-BenchmarkMilliseconds $node.PromotionElapsed
        $promotionMillisecondsSum += $promotionMilliseconds
        if ($promotionMilliseconds -gt $promotionMillisecondsMax) {
            $promotionMillisecondsMax = $promotionMilliseconds
        }

        $promotionLockWaitMillisecondsSum += ConvertTo-BenchmarkMilliseconds $node.PromotionLockWaitElapsed
        $installLockWaitMillisecondsSum += ConvertTo-BenchmarkMilliseconds $node.InstallLockWaitElapsed
        $coalescedWaitMillisecondsSum += ConvertTo-BenchmarkMilliseconds $node.CoalescedWaitElapsed

        if ($null -ne $node.AuthenticodeVerification) {
            $authenticodeCheckedFiles += [int]$node.AuthenticodeVerification.CheckedFiles
            $authenticodeCatalogFiles += [int]$node.AuthenticodeVerification.CatalogFiles
        }
    }

    [ordered]@{
        ManagedPackageCount = $nodes.Count.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedInstalledCount = $installedCount.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedAlreadyInstalledCount = $alreadyInstalledCount.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedFileCount = $fileCount.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedDownloadedBytes = $downloadedBytes.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedExtractedBytes = $extractedBytes.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedPackageRepositoryRequests = $packageRepositoryRequests.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedPackageRepositoryRedirects = $packageRepositoryRedirects.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedVersionResolutionMillisecondsSum = Format-BenchmarkNumber $versionResolutionMillisecondsSum
        ManagedVersionSelectionWaitMillisecondsSum = Format-BenchmarkNumber $versionSelectionWaitMillisecondsSum
        ManagedDownloadMillisecondsSum = Format-BenchmarkNumber $downloadMillisecondsSum
        ManagedDownloadMillisecondsMax = Format-BenchmarkNumber $downloadMillisecondsMax
        ManagedExtractionMillisecondsSum = Format-BenchmarkNumber $extractionMillisecondsSum
        ManagedExtractionMillisecondsMax = Format-BenchmarkNumber $extractionMillisecondsMax
        ManagedExtractionCacheLockWaitMillisecondsSum = Format-BenchmarkNumber $extractionCacheLockWaitMillisecondsSum
        ManagedDependencyMillisecondsRoot = Format-BenchmarkNumber (ConvertTo-BenchmarkMilliseconds $ManagedResult.DependencyElapsed)
        ManagedDependencyQueueWaitMillisecondsSum = Format-BenchmarkNumber $dependencyQueueWaitMillisecondsSum
        ManagedDependencyBranchMillisecondsMax = Format-BenchmarkNumber $dependencyBranchMillisecondsMax
        ManagedPromotionMillisecondsSum = Format-BenchmarkNumber $promotionMillisecondsSum
        ManagedPromotionMillisecondsMax = Format-BenchmarkNumber $promotionMillisecondsMax
        ManagedPromotionLockWaitMillisecondsSum = Format-BenchmarkNumber $promotionLockWaitMillisecondsSum
        ManagedInstallLockWaitMillisecondsSum = Format-BenchmarkNumber $installLockWaitMillisecondsSum
        ManagedCoalescedWaitMillisecondsSum = Format-BenchmarkNumber $coalescedWaitMillisecondsSum
        ManagedAuthenticodeCheckedFiles = $authenticodeCheckedFiles.ToString([Globalization.CultureInfo]::InvariantCulture)
        ManagedAuthenticodeCatalogFiles = $authenticodeCatalogFiles.ToString([Globalization.CultureInfo]::InvariantCulture)
    }
}

function Write-MeasurementResult {
    param(
        [object] $Result
    )

    $results.Add($Result) | Out-Null
    if ($script:BenchmarkOutputHasRows) {
        $Result | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Append
    } else {
        $Result | Export-Csv -LiteralPath $OutputPath -NoTypeInformation
        $script:BenchmarkOutputHasRows = $true
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
                        AllowClobber = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.Version = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    $script:LastManagedBenchmarkResult = Install-ManagedModule @parameters
                }
                'Save' {
                    $parameters = @{
                        Name = $name
                        Repository = $RepositoryUri
                        Path = $SaveRoot
                        Force = $true
                        AllowClobber = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.Version = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    $script:LastManagedBenchmarkResult = Save-ManagedModule @parameters
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
                    if ($SkipNativeCurrentUserInstall.IsPresent) {
                        throw 'Skipped: native CurrentUser install was disabled by -SkipNativeCurrentUserInstall.'
                    }
                    $parameters = @{
                        Name = $name
                        Repository = $Repository
                        Scope = 'CurrentUser'
                        TrustRepository = $true
                        Reinstall = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.Version = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Install-PSResource @parameters | Out-Null
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
                    if ($SkipNativeCurrentUserInstall.IsPresent) {
                        throw 'Skipped: native CurrentUser install was disabled by -SkipNativeCurrentUserInstall.'
                    }
                    $parameters = @{
                        Name = $name
                        Repository = $Repository
                        Scope = 'CurrentUser'
                        Force = $true
                        AllowClobber = $true
                    }
                    if (-not [string]::IsNullOrWhiteSpace($version)) { $parameters.RequiredVersion = $version }
                    if ($acceptLicense) { $parameters.AcceptLicense = $true }
                    Install-Module @parameters | Out-Null
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
$script:BenchmarkOutputHasRows = $Append.IsPresent -and (Test-Path -LiteralPath $OutputPath)
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
$script:LastManagedBenchmarkResult = $null
foreach ($scenario in $selectedScenarios) {
    foreach ($operationName in $Operation) {
        foreach ($engineName in $Engine) {
            foreach ($iteration in 1..$RepeatCount) {
                $runRoot = Join-Path $OutputRoot ('{0}-{1}-{2}-{3}' -f $scenario.Name, $operationName, $engineName, $iteration)
                $installRoot = Join-Path $runRoot 'Install'
                $saveRoot = Join-Path $runRoot 'Save'
                New-Item -ItemType Directory -Path $installRoot, $saveRoot -Force | Out-Null

                try {
                    $script:LastManagedBenchmarkResult = $null
                    $elapsed = Invoke-MeasuredBlock {
                        Invoke-BenchmarkCommand -Scenario $scenario -OperationName $operationName -EngineName $engineName -InstallRoot $installRoot -SaveRoot $saveRoot
                    }
                    Write-MeasurementResult -Result (New-MeasurementResult -Scenario $scenario -OperationName $operationName -EngineName $engineName -Iteration $iteration -Status 'Succeeded' -Milliseconds $elapsed -Reason '' -ManagedResult $script:LastManagedBenchmarkResult)
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
                    Write-MeasurementResult -Result (New-MeasurementResult -Scenario $scenario -OperationName $operationName -EngineName $engineName -Iteration $iteration -Status $status -Milliseconds 0 -Reason $message)
                }
            }
        }
    }
}

$results
