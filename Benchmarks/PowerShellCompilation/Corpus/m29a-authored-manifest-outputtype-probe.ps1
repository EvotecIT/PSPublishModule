param(
    [Parameter(Mandatory)]
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$probeRoot = Join-Path $OutputRoot ('authored-outputtype-' + $PSVersionTable.PSVersion.Major + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeRoot | Out-Null
$classPath = Join-Path $probeRoot 'Class.ps1'
$modulePath = Join-Path $probeRoot 'Probe.psm1'
$manifestPath = Join-Path $probeRoot 'Probe.psd1'
$binaryPath = Join-Path $probeRoot 'ProbeBinary.dll'

Set-Content -LiteralPath $classPath -Value 'class PFAuthoredProbeWidget { [int] $Value }'
Set-Content -LiteralPath $modulePath -Value @'
function Get-ProbeWidget {
    [OutputType([PFAuthoredProbeWidget[]])]
    param()
    [PFAuthoredProbeWidget]::new()
}
Export-ModuleMember -Function Get-ProbeWidget
'@
Set-Content -LiteralPath $manifestPath -Value @'
@{ RootModule = 'Probe.psm1'; ModuleVersion = '1.0.0'; GUID = '55ee2cbb-5b9a-4caa-826f-b2ebcd89ba7c'; ScriptsToProcess = @('Class.ps1'); FunctionsToExport = @('Get-ProbeWidget'); CmdletsToExport = @() }
'@

Import-Module $manifestPath -Force
$original = (Get-Command Get-ProbeWidget).OutputType[0]
$originalObject = Get-ProbeWidget
$source = @'
using System.Management.Automation;
[Cmdlet("Get", "CompiledProbeWidget")]
[OutputType("PFAuthoredProbeWidget[]")]
public sealed class GetCompiledProbeWidgetCommand : PSCmdlet {
    protected override void ProcessRecord() { WriteObject(1); }
}
'@
Add-Type -TypeDefinition $source -OutputAssembly $binaryPath -ErrorAction Stop
Import-Module $binaryPath -Force
$compiled = (Get-Command Get-CompiledProbeWidget).OutputType[0]

[pscustomobject]@{
    host = $PSVersionTable.PSVersion.ToString()
    originalName = $original.Name
    compiledName = $compiled.Name
    originalType = $original.Type.FullName
    compiledType = $compiled.Type.FullName
    sameType = ($original.Type -eq $compiled.Type)
    originalObjectType = $originalObject.GetType().FullName
} | ConvertTo-Json -Compress
