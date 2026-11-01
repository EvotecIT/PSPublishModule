param(
    [Parameter(Mandatory)][string] $ModuleDll,
    [Parameter(Mandatory)][string] $Root,
    [Parameter(Mandatory)][string] $ActualVersion,
    [Parameter(Mandatory)][string] $ExpectedGuid,
    [Parameter(Mandatory)][string] $ActualGuid
)
$ErrorActionPreference = 'Stop'
Import-Module $ModuleDll -Force -WarningAction SilentlyContinue -ErrorAction Stop
$identity = [guid]::NewGuid().ToString('N')
$moduleName = 'Selected.Binary.' + $identity
$binary = Join-Path $Root ($moduleName + '.dll')
$source = @"
using System.Management.Automation;
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[Cmdlet("Get", "SelectedBinaryRuntime")]
public sealed class SelectedBinary$identity : PSCmdlet {
    protected override void ProcessRecord() { WriteObject("selected-binary"); }
}
"@
Add-Type -TypeDefinition $source -OutputAssembly $binary -ErrorAction Stop
Set-Content -LiteralPath (Join-Path $Root ($moduleName + '.psm1')) -Value 'Export-ModuleMember -Cmdlet Get-SelectedBinaryRuntime'
$manifest = "@{ RootModule='$moduleName.psm1'; NestedModules=@('$moduleName.dll'); ModuleVersion='$ActualVersion'; GUID='$ActualGuid'; FunctionsToExport=@(); CmdletsToExport=@('Get-SelectedBinaryRuntime'); AliasesToExport=@() }"
Set-Content -LiteralPath (Join-Path $Root ($moduleName + '.psd1')) -Value $manifest
$selected = [PowerForge.ApprovedModuleSource]::new($moduleName, '9.8.7', $Root, $ExpectedGuid, $null)
$options = [PowerForge.MissingFunctionsOptions]::new(
    [string[]] @(), [string[]] @(), [string[]] @(), $true,
    [PowerForge.ApprovedModuleSource[]] @(), $false,
    [PowerForge.ApprovedModuleSource[]] @($selected)
)
$report = [PowerForge.MissingFunctionsAnalyzer]::new().Analyze($null, 'Get-SelectedBinaryRuntime', $options)
[pscustomobject] @{
    ModuleName = $moduleName
    SummaryCount = @($report.Summary).Count
    Error = $report.Summary[0].Error
    Source = $report.Summary[0].Source
    CommandType = $report.Summary[0].CommandType
    FunctionCount = @($report.Functions).Count
} | ConvertTo-Json -Compress
