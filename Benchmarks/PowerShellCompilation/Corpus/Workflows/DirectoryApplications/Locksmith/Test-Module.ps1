param(
    [Parameter(Mandatory)] [string] $ArtifactPath,
    [switch] $PassThru
)

$ErrorActionPreference = 'Stop'
if (!$IsWindows -or $PSVersionTable.PSVersion.ToString() -cne '7.6.6') {
    throw 'Classic Locksmith qualification requires PowerShell 7.6.6 on Windows.'
}
$env:ADPS_LoadDefaultDrive = '0'
$windowsModules = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/Modules'
$env:PSModulePath = @((Join-Path $PSHOME 'Modules'), $windowsModules) -join [IO.Path]::PathSeparator
# The read-only workflow does not use the Windows Server feature-management surface.
if (!(Get-Module -ListAvailable ServerManager) -and !(Get-Module ServerManager)) {
    New-Module -Name ServerManager -ScriptBlock {} | Import-Module -Global
}

$moduleBase = Split-Path -LiteralPath $ArtifactPath
$module = Import-Module $ArtifactPath -PassThru -Force -WarningAction SilentlyContinue 6>$null |
    Where-Object { $_ -is [Management.Automation.PSModuleInfo] -and $_.Name -ceq 'Locksmith' } |
    Select-Object -Last 1
if (!$module -or $module.ModuleBase -ine $moduleBase -or !$module.ExportedFunctions.ContainsKey('Invoke-Locksmith')) {
    throw 'The requested Locksmith surface did not export its own invocation command.'
}
$dictionary = & $module { Get-Command New-Dictionary -CommandType Function -ErrorAction Stop }
$audit = & $module { Get-Command Find-AuditingIssue -CommandType Function -ErrorAction Stop }
if ($dictionary.Module.ModuleBase -ine $moduleBase -or $audit.Module.ModuleBase -ine $moduleBase -or
    ![object]::ReferenceEquals($audit.Parameters.ADCSObjects.ParameterType, [Microsoft.ActiveDirectory.Management.ADEntity[]])) {
    throw 'The loaded functions did not bind their own Windows AD module type.'
}
$rows = @(& $dictionary)
$entity = [Microsoft.ActiveDirectory.Management.ADObject]::new()
$entity.ObjectClass = 'container'
$matches = @(& $audit -ADCSObjects @($entity))
if ($rows.Count -ne 12 -or $matches.Count -ne 0 -or
    @($rows | Where-Object {
        $_.GetType().Name -cne 'VulnerableConfigurationItem' -or $_.FindIt -isnot [scriptblock] -or
        $_.FixIt -isnot [scriptblock] -or $_.ReferenceUrls -isnot [uri[]]
    }).Count) {
    throw 'The lexical class records or the inert AD binding scenario differed from the pinned input.'
}
if ($PassThru) {
    [pscustomobject]@{
        Module = $module
        DictionaryCount = $rows.Count
        DirectoryParameterType = $audit.Parameters.ADCSObjects.ParameterType.FullName
        InertAuditMatches = $matches.Count
    }
}
