param([Parameter(Mandatory = $true)][string] $Root)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'New-Fixtures.ps1') -Root $Root
foreach ($version in 'old', 'new') {
    $directory = New-Item -ItemType Directory -Path (Join-Path $Root $version) -Force
    $expression = if ($version -eq 'old') { '"old"' } else { '"shared:" + SharedDisplay.Broker.Register()' }
    $source = @"
using System.Management.Automation;
namespace RetainedProduct {
    [Cmdlet(VerbsCommon.Get, "RetainedData")]
    public sealed class GetDataCommand : PSCmdlet {
        protected override void ProcessRecord() { WriteObject($expression); }
    }
}
"@
    $references = @([System.Management.Automation.PSCmdlet].Assembly.Location)
    if ($version -eq 'new') { $references += (Join-Path $Root 'Core1\SharedDisplay.dll') }
    Add-Type -TypeDefinition $source -ReferencedAssemblies $references -OutputAssembly (Join-Path $directory.FullName 'Retained.Host.dll')
}
'REVIEW_FIXTURES_OK'
