param(
    [Parameter(Mandatory)][string] $ModuleDll,
    [Parameter(Mandatory)][string] $Root,
    [Parameter(Mandatory)][string] $ActualVersion,
    [Parameter(Mandatory)][string] $ExpectedGuid,
    [Parameter(Mandatory)][string] $ActualGuid,
    [ValidateSet(0, 1)][int] $WithDependency = 0
)
$ErrorActionPreference = 'Stop'
Import-Module $ModuleDll -Force -WarningAction SilentlyContinue -ErrorAction Stop
$identity = [guid]::NewGuid().ToString('N')
$moduleName = 'Selected.Binary.' + $identity
$moduleRoot = Join-Path $Root $moduleName
$null = New-Item -ItemType Directory -Path $moduleRoot
$binary = Join-Path $moduleRoot ($moduleName + '.dll')
$source = @"
using System.Management.Automation;
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[Cmdlet("Get", "SelectedBinaryRuntime")]
public sealed class SelectedBinary$identity : PSCmdlet {
    protected override void ProcessRecord() { WriteObject("selected-binary"); }
}
"@
Add-Type -TypeDefinition $source -OutputAssembly $binary -ErrorAction Stop
Set-Content -LiteralPath (Join-Path $moduleRoot ($moduleName + '.psm1')) -Value 'Export-ModuleMember -Cmdlet Get-SelectedBinaryRuntime'
$manifest = "@{ RootModule='$moduleName.psm1'; NestedModules=@('$moduleName.dll'); ModuleVersion='$ActualVersion'; GUID='$ActualGuid'; FunctionsToExport=@(); CmdletsToExport=@('Get-SelectedBinaryRuntime'); AliasesToExport=@() }"
if ($WithDependency) {
    $dependencyName = 'Selected.Sidecar.' + $identity
    $dependencyRoot = Join-Path $Root $dependencyName
    $null = New-Item -ItemType Directory -Path $dependencyRoot
    Set-Content -LiteralPath (Join-Path $dependencyRoot ($dependencyName + '.psm1')) -Value 'function Get-SelectedSidecar { 1 }; Export-ModuleMember -Function Get-SelectedSidecar'
    Set-Content -LiteralPath (Join-Path $dependencyRoot ($dependencyName + '.psd1')) -Value "@{ RootModule='$dependencyName.psm1'; ModuleVersion='1.0.0'; GUID='$([guid]::NewGuid())'; FunctionsToExport=@('Get-SelectedSidecar') }"
    $manifest = $manifest.Replace('RootModule=', "RequiredModules=@('$dependencyName'); RootModule=")
}
Set-Content -LiteralPath (Join-Path $moduleRoot ($moduleName + '.psd1')) -Value $manifest
$selected = [PowerForge.ApprovedModuleSource]::new($moduleName, '9.8.7', $moduleRoot, $ExpectedGuid, $null)
$options = [PowerForge.MissingFunctionsOptions]::new(
    [string[]] @(), [string[]] @(), [string[]] @(), $true,
    [PowerForge.ApprovedModuleSource[]] @(), $false,
    [PowerForge.ApprovedModuleSource[]] @($selected)
)
$env:PSModulePath = $Root + [IO.Path]::PathSeparator + (Join-Path $PSHOME 'Modules')
$beforePath = $env:PSModulePath
$report = [PowerForge.MissingFunctionsAnalyzer]::new().Analyze($null, 'Get-SelectedBinaryRuntime', $options)
[pscustomobject] @{
    ModuleName = $moduleName
    ModulePathUnchanged = $beforePath -eq $env:PSModulePath
    SummaryCount = @($report.Summary).Count
    Error = $report.Summary[0].Error
    Source = $report.Summary[0].Source
    CommandType = $report.Summary[0].CommandType
    FunctionCount = @($report.Functions).Count
} | ConvertTo-Json -Compress
