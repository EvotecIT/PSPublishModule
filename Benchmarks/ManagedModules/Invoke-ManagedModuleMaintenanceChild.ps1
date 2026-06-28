param(
    [Parameter(Mandatory)]
    [string] $ModuleName,

    [Parameter(Mandatory)]
    [string] $Repository,

    [Parameter(Mandatory)]
    [string] $Destination,

    [Parameter(Mandatory)]
    [string] $ModuleBinary,

    [string] $Version,

    [string] $MaintenanceReceiptPath,

    [string[]] $Family,

    [string] $Cleanup,

    [string] $LoadedModulePath,

    [switch] $IncludeLoaded,

    [switch] $Latest,

    [switch] $AcceptLicense,

    [string] $ResultPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $invariantCulture
[Threading.Thread]::CurrentThread.CurrentUICulture = $invariantCulture

function Get-PropertyValue {
    param(
        [object] $InputObject,
        [string] $Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($property) {
        return $property.Value
    }

    $null
}

function Write-MaintenanceDetail {
    param(
        [object] $Result,
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $plan = Get-PropertyValue -InputObject $Result -Name 'Plan'
    $test = Get-PropertyValue -InputObject $Result -Name 'Test'
    $actions = @(@(Get-PropertyValue -InputObject $plan -Name 'Actions') | Where-Object { $null -ne $_ })
    $findings = @(@(Get-PropertyValue -InputObject $plan -Name 'Findings') | Where-Object { $null -ne $_ })
    if ($findings.Count -eq 0) {
        $findings = @(@(Get-PropertyValue -InputObject $test -Name 'Findings') | Where-Object { $null -ne $_ })
    }

    $parent = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -Path $parent -ItemType Directory -Force | Out-Null
    }

    [pscustomobject]@{
        Summary = [pscustomobject]@{
            PackageCount = 0
            DependencyCount = 0
            RootElapsedMilliseconds = 0
            RootDependencyMilliseconds = 0
            TotalDownloadMilliseconds = 0
            TotalExtractionMilliseconds = 0
            TotalPromotionMilliseconds = 0
            TotalRepositoryRequestCount = 0
            TotalDownloadBytes = 0
            CacheHitCount = 0
            MaintenanceActionCount = $actions.Count
            MaintenanceFindingCount = $findings.Count
        }
        Actions = $actions
        Findings = $findings
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

Import-Module -Name $ModuleBinary -Force

if (-not [string]::IsNullOrWhiteSpace($LoadedModulePath)) {
    Import-Module -Name $LoadedModulePath -Force
}

$parameters = @{
    ModulePath = $Destination
    Repository = $Repository
    Transport = 'ManagedModule'
    ModuleRoot = $Destination
    Plan = $true
}
if (-not [string]::IsNullOrWhiteSpace($ModuleName)) {
    $parameters.Name = $ModuleName
}
if ($Latest.IsPresent) {
    $parameters.Latest = $true
}
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $parameters.Version = $Version
}
if (-not [string]::IsNullOrWhiteSpace($MaintenanceReceiptPath)) {
    $parameters.MaintenanceReceiptPath = $MaintenanceReceiptPath
}
if ($Family -and $Family.Count -gt 0) {
    $parameters.Family = $Family
}
if (-not [string]::IsNullOrWhiteSpace($Cleanup)) {
    $parameters.Cleanup = $Cleanup
}
if ($IncludeLoaded.IsPresent) {
    $parameters.IncludeLoaded = $true
}
if ($AcceptLicense.IsPresent) {
    $parameters.AcceptLicense = $true
}

$result = Repair-ManagedModule @parameters
Write-MaintenanceDetail -Result $result -Path $ResultPath
$result
