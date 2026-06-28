param(
    [Parameter(Mandatory)]
    [ValidateSet('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')]
    [string] $EngineName,

    [Parameter(Mandatory)]
    [string] $ModuleName,

    [string] $Version,

    [Parameter(Mandatory)]
    [string] $Repository,

    [Parameter(Mandatory)]
    [string] $RepositoryName,

    [string] $ModuleFastSource = 'https://pwsh.gallery/index.json',

    [Parameter(Mandatory)]
    [string] $Destination,

    [string] $ModuleBinary,

    [string] $ProviderModulePath,

    [string[]] $ProviderDependencyModulePath,

    [switch] $AcceptLicense,

    [string] $ResultPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $invariantCulture
[Threading.Thread]::CurrentThread.CurrentUICulture = $invariantCulture

function Get-VersionParameter {
    param(
        [string] $CommandName,
        [string] $ExactVersion
    )

    if ([string]::IsNullOrWhiteSpace($ExactVersion)) {
        return @{}
    }

    $command = Get-Command -Name $CommandName -ErrorAction Stop
    if ($command.Parameters.ContainsKey('RequiredVersion')) {
        return @{ RequiredVersion = $ExactVersion }
    }

    if ($command.Parameters.ContainsKey('Version')) {
        return @{ Version = $ExactVersion }
    }

    @{}
}

function Add-SwitchParameterIfSupported {
    param(
        [hashtable] $Parameters,
        [string] $CommandName,
        [string] $ParameterName,
        [bool] $Enabled
    )

    if (-not $Enabled) {
        return
    }

    $command = Get-Command -Name $CommandName -ErrorAction Stop
    if ($command.Parameters.ContainsKey($ParameterName)) {
        $Parameters[$ParameterName] = $true
    }
}

function ConvertTo-Milliseconds {
    param([object] $TimeSpan)

    if ($null -eq $TimeSpan) {
        return 0
    }

    [math]::Round($TimeSpan.TotalMilliseconds, 2)
}

function Add-ManagedInstallDetail {
    param(
        [Parameter(Mandatory)]
        [object] $Result,

        [string] $Parent,

        [int] $Depth,

        [System.Collections.Generic.List[object]] $Rows
    )

    $download = $Result.Download
    $Rows.Add([pscustomobject]@{
        Name = [string] $Result.Name
        Version = [string] $Result.Version
        Status = [string] $Result.Status
        Parent = $Parent
        Depth = $Depth
        ElapsedMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.Elapsed
        VersionResolutionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.VersionResolutionElapsed
        DownloadMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DownloadElapsed
        ExtractionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.ExtractionElapsed
        DependencyMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DependencyElapsed
        PromotionMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.PromotionElapsed
        RepositoryRequestCount = [long] $Result.RepositoryRequestCount
        FileCount = [int] $Result.FileCount
        ExtractedBytes = [long] $Result.ExtractedBytes
        DownloadBytes = if ($download) { [long] $download.BytesWritten } else { 0L }
        DownloadFromCache = if ($download) { [bool] $download.FromCache } else { $false }
    })

    foreach ($dependency in @($Result.DependencyResults)) {
        Add-ManagedInstallDetail -Result $dependency -Parent $Result.Name -Depth ($Depth + 1) -Rows $Rows
    }
}

function Write-ManagedInstallDetail {
    param(
        [object] $Result,
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or $null -eq $Result) {
        return
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    Add-ManagedInstallDetail -Result $Result -Parent '' -Depth 0 -Rows $rows
    $packages = @($rows)
    $summary = [pscustomobject]@{
        PackageCount = $packages.Count
        DependencyCount = [math]::Max(0, $packages.Count - 1)
        RootElapsedMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.Elapsed
        RootDependencyMilliseconds = ConvertTo-Milliseconds -TimeSpan $Result.DependencyElapsed
        TotalDownloadMilliseconds = [math]::Round((($packages | Measure-Object DownloadMilliseconds -Sum).Sum), 2)
        TotalExtractionMilliseconds = [math]::Round((($packages | Measure-Object ExtractionMilliseconds -Sum).Sum), 2)
        TotalPromotionMilliseconds = [math]::Round((($packages | Measure-Object PromotionMilliseconds -Sum).Sum), 2)
        TotalRepositoryRequestCount = [long] $Result.RepositoryRequestCount
        TotalDownloadBytes = [long] (($packages | Measure-Object DownloadBytes -Sum).Sum)
        CacheHitCount = @($packages | Where-Object DownloadFromCache).Count
    }

    $parent = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -Path $parent -ItemType Directory -Force | Out-Null
    }

    [pscustomobject]@{
        Summary = $summary
        Packages = $packages
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Import-BenchmarkProviderModule {
    param(
        [string] $Name,
        [string] $Path
    )

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        Import-Module -Name $Path -Force -ErrorAction Stop
        return
    }

    Import-Module -Name $Name -Force -ErrorAction Stop
}

function Ensure-PowerShellGetRepository {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return
    }

    if (-not (Get-PSRepository -Name $Name -ErrorAction SilentlyContinue)) {
        Register-PSRepository -Default -ErrorAction Stop
    }

    Set-PSRepository -Name $Name -InstallationPolicy Trusted -ErrorAction SilentlyContinue
}

switch ($EngineName) {
    'ModuleFast' {
        if ($PSVersionTable.PSEdition -eq 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'7.2') {
            throw 'ModuleFast requires PowerShell 7.2 or newer.'
        }

        Import-BenchmarkProviderModule -Name 'ModuleFast' -Path $ProviderModulePath
        $specification = if ([string]::IsNullOrWhiteSpace($Version)) {
            $ModuleName
        } else {
            '{0}={1}' -f $ModuleName, $Version
        }

        $parameters = @{
            Specification = $specification
            Destination = $Destination
            Source = $ModuleFastSource
            DestinationOnly = $true
            NoPSModulePathUpdate = $true
            NoProfileUpdate = $true
            Update = $true
            PassThru = $true
        }
        Install-ModuleFast @parameters
    }
    'Managed' {
        if ([string]::IsNullOrWhiteSpace($ModuleBinary)) {
            throw 'ModuleBinary is required for the managed install benchmark.'
        }

        Import-Module -Name $ModuleBinary -Force
        $parameters = @{
            Name = $ModuleName
            Repository = $Repository
            RepositoryName = $RepositoryName
            Scope = 'Custom'
            ModuleRoot = $Destination
            AllowClobber = $true
            Force = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($Version)) {
            $parameters.Version = $Version
        }

        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Install-ManagedModule' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
        $result = Install-ManagedModule @parameters
        Write-ManagedInstallDetail -Result $result -Path $ResultPath
        $result
    }
    'PSResourceGet' {
        foreach ($dependencyPath in @($ProviderDependencyModulePath)) {
            if (-not [string]::IsNullOrWhiteSpace($dependencyPath)) {
                Import-Module -Name $dependencyPath -Force -ErrorAction Stop
            }
        }
        Import-BenchmarkProviderModule -Name 'Microsoft.PowerShell.PSResourceGet' -Path $ProviderModulePath
        $parameters = @{
            Name = $ModuleName
            Repository = $RepositoryName
            Scope = 'CurrentUser'
            TrustRepository = $true
        }
        foreach ($entry in (Get-VersionParameter -CommandName 'Install-PSResource' -ExactVersion $Version).GetEnumerator()) {
            $parameters[$entry.Key] = $entry.Value
        }

        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Install-PSResource' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Install-PSResource' -ParameterName 'Reinstall' -Enabled $true
        Install-PSResource @parameters
    }
    'PowerShellGet' {
        foreach ($dependencyPath in @($ProviderDependencyModulePath)) {
            if (-not [string]::IsNullOrWhiteSpace($dependencyPath)) {
                Import-Module -Name $dependencyPath -Force -ErrorAction Stop
            }
        }
        Import-BenchmarkProviderModule -Name 'PowerShellGet' -Path $ProviderModulePath
        Ensure-PowerShellGetRepository -Name $RepositoryName
        $parameters = @{
            Name = $ModuleName
            Repository = $RepositoryName
            Scope = 'CurrentUser'
            Force = $true
            AllowClobber = $true
        }
        foreach ($entry in (Get-VersionParameter -CommandName 'Install-Module' -ExactVersion $Version).GetEnumerator()) {
            $parameters[$entry.Key] = $entry.Value
        }

        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Install-Module' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Install-Module' -ParameterName 'SkipPublisherCheck' -Enabled $true
        Install-Module @parameters
    }
}
