param(
    [Parameter(Mandatory)]
    [string] $ModuleName,

    [Parameter(Mandatory)]
    [string] $Repository,

    [Parameter(Mandatory)]
    [string] $Destination,

    [Parameter(Mandatory)]
    [string] $ModuleBinary,

    [string] $MaintenanceReceiptPath,

    [string[]] $Family,

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
    $findings = @(@(Get-PropertyValue -InputObject $test -Name 'Findings') | Where-Object { $null -ne $_ })

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

$parameters = @{
    Installed = $true
    Repair = $true
    ModulePath = $Destination
    Repository = $Repository
    Transport = 'ManagedModule'
    ModuleRoot = $Destination
}
if ($Latest.IsPresent) {
    $parameters.Latest = $true
}
if (-not [string]::IsNullOrWhiteSpace($MaintenanceReceiptPath)) {
    $parameters.MaintenanceReceiptPath = $MaintenanceReceiptPath
}
if ($Family -and $Family.Count -gt 0) {
    $parameters.Family = $Family
}
if ($AcceptLicense.IsPresent) {
    $parameters.AcceptLicense = $true
}

$result = Invoke-ModuleState @parameters
Write-MaintenanceDetail -Result $result -Path $ResultPath
$result
