param(
    [Parameter(Mandatory)]
    [ValidateSet('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')]
    [string] $EngineName,

    [ValidateSet('Install', 'Update')]
    [string] $Operation = 'Install',

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

    [string] $PackageCacheDirectory,

    [string] $ProviderModulePath,

    [string[]] $ProviderDependencyModulePath,

    [switch] $AcceptLicense,

    [switch] $AuthenticodeCheck,

    [switch] $Force,

    [string] $ResultPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $invariantCulture
[Threading.Thread]::CurrentThread.CurrentUICulture = $invariantCulture

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Artifacts.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.ManagedDetails.ps1')

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

function Ensure-PSResourceRepository {
    param(
        [string] $Name,
        [string] $Uri
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return
    }

    if (Get-PSResourceRepository -Name $Name -ErrorAction SilentlyContinue) {
        Set-PSResourceRepository -Name $Name -Trusted -ErrorAction SilentlyContinue
        return
    }

    if ([string]::IsNullOrWhiteSpace($Uri)) {
        return
    }

    Register-PSResourceRepository -Name $Name -Uri $Uri -Trusted -Force -ErrorAction Stop | Out-Null
}

switch ($EngineName) {
    'ModuleFast' {
        if ($PSVersionTable.PSEdition -eq 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'7.2') {
            throw 'ModuleFast requires PowerShell 7.2 or newer.'
        }
        if ($Operation -eq 'Update') {
            throw 'ModuleFast does not expose an equivalent update command.'
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
            PassThru = $true
        }
        if ($Force.IsPresent) {
            $parameters.Update = $true
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
        }
        if ($Force.IsPresent) {
            $parameters.Force = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($Version)) {
            $parameters.Version = $Version
        }
        if (-not [string]::IsNullOrWhiteSpace($PackageCacheDirectory)) {
            $parameters.PackageCacheDirectory = $PackageCacheDirectory
        }

        $commandName = if ($Operation -eq 'Update') { 'Update-ManagedModule' } else { 'Install-ManagedModule' }
        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'AuthenticodeCheck' -Enabled $AuthenticodeCheck.IsPresent
        $result = if ($Operation -eq 'Update') {
            Update-ManagedModule @parameters
        } else {
            Install-ManagedModule @parameters
        }
        $detailResult = if ($Operation -eq 'Update' -and $result.PSObject.Properties['InstallResult']) {
            $result.InstallResult
        } elseif ($Operation -eq 'Install') {
            $result
        } else {
            $null
        }
        Write-ManagedInstallDetail -Result $detailResult -Path $ResultPath
        $result
    }
    'PSResourceGet' {
        foreach ($dependencyPath in @($ProviderDependencyModulePath)) {
            if (-not [string]::IsNullOrWhiteSpace($dependencyPath)) {
                Import-Module -Name $dependencyPath -Force -ErrorAction Stop
            }
        }
        Import-BenchmarkProviderModule -Name 'Microsoft.PowerShell.PSResourceGet' -Path $ProviderModulePath
        Ensure-PSResourceRepository -Name $RepositoryName -Uri $Repository
        $parameters = @{
            Name = $ModuleName
            Repository = $RepositoryName
            Scope = 'CurrentUser'
            TrustRepository = $true
        }
        $commandName = if ($Operation -eq 'Update') { 'Update-PSResource' } else { 'Install-PSResource' }
        foreach ($entry in (Get-VersionParameter -CommandName $commandName -ExactVersion $Version).GetEnumerator()) {
            $parameters[$entry.Key] = $entry.Value
        }

        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'AuthenticodeCheck' -Enabled $AuthenticodeCheck.IsPresent
        if ($Operation -eq 'Install') {
            Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'Reinstall' -Enabled $Force.IsPresent
            Install-PSResource @parameters
        } else {
            Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'Force' -Enabled $Force.IsPresent
            Update-PSResource @parameters
        }
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
            Scope = 'CurrentUser'
        }
        if ($Operation -eq 'Install') {
            $parameters.Repository = $RepositoryName
            $parameters.AllowClobber = $true
        }
        if ($Force.IsPresent) {
            $parameters.Force = $true
        }
        $commandName = if ($Operation -eq 'Update') { 'Update-Module' } else { 'Install-Module' }
        foreach ($entry in (Get-VersionParameter -CommandName $commandName -ExactVersion $Version).GetEnumerator()) {
            $parameters[$entry.Key] = $entry.Value
        }

        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName $commandName -ParameterName 'SkipPublisherCheck' -Enabled $true
        if ($Operation -eq 'Update') {
            Update-Module @parameters
        } else {
            Install-Module @parameters
        }
    }
}
