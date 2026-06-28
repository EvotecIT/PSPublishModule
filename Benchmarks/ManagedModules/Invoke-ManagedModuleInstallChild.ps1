param(
    [Parameter(Mandatory)]
    [ValidateSet('Managed', 'PSResourceGet', 'PowerShellGet')]
    [string] $EngineName,

    [Parameter(Mandatory)]
    [string] $ModuleName,

    [string] $Version,

    [Parameter(Mandatory)]
    [string] $Repository,

    [Parameter(Mandatory)]
    [string] $RepositoryName,

    [Parameter(Mandatory)]
    [string] $Destination,

    [string] $ModuleBinary,

    [string] $ProviderModulePath,

    [string[]] $ProviderDependencyModulePath,

    [switch] $AcceptLicense
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

switch ($EngineName) {
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
        Install-ManagedModule @parameters
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
