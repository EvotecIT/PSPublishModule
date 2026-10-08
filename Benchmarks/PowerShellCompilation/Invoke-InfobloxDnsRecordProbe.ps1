param([Parameter(Mandatory)][string]$ModulePath)
$ErrorActionPreference = 'Stop'
$PSModuleAutoloadingPreference = 'None'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
Import-Module Microsoft.PowerShell.Management -ErrorAction Stop
Import-Module -Name $ModulePath -Force -ErrorAction Stop
function global:Get-FieldsFromSchema {
    [CmdletBinding()]param([string]$SchemaObject,[string[]]$RequestedFields)
    $global:OfflineDnsCalls.Add([pscustomobject]@{kind='schema';schema=$SchemaObject;requested=@($RequestedFields)})
    if($RequestedFields){$RequestedFields -join ','}else{'offline_schema,name'}
}
function global:Invoke-InfobloxQuery {
    [CmdletBinding(SupportsShouldProcess=$true)]param([string]$RelativeUri,[string]$Method,[Collections.IDictionary]$QueryParameter)
    $global:OfflineDnsCalls.Add([pscustomobject]@{kind='query';uri=$RelativeUri;method=$Method;query=$QueryParameter;queryType=$QueryParameter.GetType().FullName;whatIf=$WhatIfPreference})
    if($global:OfflineDnsMode -eq 'failure'){throw 'offline DNS query failure'}
    if($global:OfflineDnsMode -eq 'empty'){return}
    [pscustomobject]@{name='first.example';_ref='record:offline/1'}
    [pscustomobject]@{name='second.example';_ref='record:offline/2'}
}
function global:Select-ObjectByProperty {
    [CmdletBinding()]param([Parameter(ValueFromPipeline)]$InputObject,[string[]]$FirstProperty,[string[]]$LastProperty)
    process {
        $global:OfflineDnsCalls.Add([pscustomobject]@{kind='select';first=@($FirstProperty);last=@($LastProperty);nullInput=$null -eq $InputObject})
        [pscustomobject]@{input=$InputObject;first=@($FirstProperty);last=@($LastProperty)}
    }
}
$cases=@(
    @{id='a';args=@{Type='A';Name='HOST.EXAMPLE';Zone='EXAMPLE';View='DEFAULT'}}
    @{id='aaaa';args=@{Type='aaaa'}}
    @{id='cname';args=@{Type='cname'}}
    @{id='host';args=@{Type='Host';PartialMatch=$true;Name='HOST'}}
    @{id='mx';args=@{Type='MX'}}
    @{id='ns';args=@{Type='ns'}}
    @{id='ptr';args=@{Type='ptr'}}
    @{id='txt';args=@{Type='txt'}}
    @{id='unknown';args=@{Type='lbdn'}}
    @{id='explicit-fields';args=@{Type='a';ReturnFields=@('zone','name','name');MaxResults=2}}
    @{id='schema-fields';args=@{Type='a';FetchFromSchema=$true}}
    @{id='reference';args=@{ReferenceID='record:mx/offline-reference'}}
    @{id='invalid-reference';args=@{ReferenceID='network/offline-reference'}}
)
foreach($mode in 'normal','empty','failure') {
    foreach($case in $cases) {
        $global:OfflineDnsCalls=[Collections.Generic.List[object]]::new()
        $global:OfflineDnsMode=$mode
        Set-OfflineDnsConfiguration -Connected $true
        $records=[Collections.Generic.List[object]]::new();$failure=$null
        $arguments=$case.args
        try {Get-InfobloxDNSRecord @arguments -ErrorAction Stop | ForEach-Object {$records.Add($_)}}
        catch {$failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
        [pscustomobject]@{case=$case.id;mode=$mode;calls=@($global:OfflineDnsCalls.ToArray());result=@($records.ToArray());failure=$failure}|ConvertTo-Json -Depth 12 -Compress
    }
}
foreach($action in 'Continue','Stop') {
    Set-OfflineDnsConfiguration -Connected $false
    $global:OfflineDnsCalls=[Collections.Generic.List[object]]::new();$failure=$null;$warnings=@()
    try {Get-InfobloxDNSRecord -Type a -ErrorAction $action -WarningVariable warnings}
    catch {$failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
    [pscustomobject]@{case='disconnected';action=$action;calls=@($global:OfflineDnsCalls.ToArray());warnings=@($warnings|ForEach-Object {$_.Message});failure=$failure}|ConvertTo-Json -Depth 12 -Compress
}
