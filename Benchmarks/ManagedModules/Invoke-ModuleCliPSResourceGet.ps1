param(
    [Parameter(Mandatory)] [string] $ModulePath,
    [Parameter(Mandatory)] [string] $IsolatedHome,
    [Parameter(Mandatory)] [string] $Destination,
    [Parameter(Mandatory)] [ValidateSet('Prepare', 'Startup', 'Install', 'Find', 'Save', 'List', 'Uninstall')] [string] $Operation,
    [Parameter(Mandatory)] [uri] $Source,
    [string] $Name,
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$WarningPreference = 'SilentlyContinue'
if ($HOME -cne $IsolatedHome -or $Destination -cne (Join-Path $IsolatedHome '.local/share/powershell/Modules') -or -not $Source.IsLoopback) {
    throw 'Expected an isolated benchmark home, module root and loopback source.'
}

Import-Module -Name $ModulePath -ErrorAction Stop
$common = @{ Name = $Name; Version = $Version; Repository = 'PowerForgeFixture' }
switch ($Operation) {
    'Prepare' {
        Register-PSResourceRepository -Name PowerForgeFixture -Uri $Source -ApiVersion V3 -Trusted
        Install-PSResource @common -Scope CurrentUser -AcceptLicense -Quiet -Confirm:$false
        if (-not (Test-Path -LiteralPath (Join-Path $Destination $Name $Version "$Name.psd1") -PathType Leaf)) {
            throw 'PSResourceGet did not install into the benchmark module root.'
        }
    }
    'Startup' { (Get-Command Install-PSResource -ErrorAction Stop).Name }
    'Install' { Install-PSResource @common -Scope CurrentUser -AcceptLicense -Quiet -Confirm:$false }
    'Save' { Save-PSResource @common -Path $Destination -AcceptLicense -Quiet }
    'Find' {
        Find-PSResource @common | ForEach-Object { @{ name = $_.Name; version = [string]$_.Version } } | ConvertTo-Json -Compress
    }
    'List' {
        Get-InstalledPSResource -Path $Destination -Name $Name -Version $Version |
            ForEach-Object { @{ name = $_.Name; version = [string]$_.Version } } | ConvertTo-Json -Compress
    }
    'Uninstall' { Uninstall-PSResource -Name $Name -Version $Version -Scope CurrentUser -Confirm:$false }
}
