# Offline original/generated proof for pinned GpoZaurr ConvertTo-XMLSecurityOptions.
# Pass an original source module or generated artifact module; no GPO/AD connection is used.
param([string]$ModulePath)
$ErrorActionPreference='Stop'
$null=Import-Module $ModulePath -Force
foreach($mode in 'empty','null','true','false','bad','missing','many','number') {
    $dataset=switch($mode) {
        'empty' { @() }
        'null' { $null }
        'missing' { [pscustomobject]@{KeyName='missing';Display=[pscustomobject]@{Name='display';Units='unit';DisplayString='text'};SettingNumber=2} }
        'many' {foreach($value in 'true','false','bad'){[pscustomobject]@{KeyName=$value;Display=[pscustomobject]@{Name='display';Units='unit';DisplayBoolean=$value;DisplayString='text'};SettingString='value';SettingNumber=2}}}
        default { [pscustomobject]@{KeyName=$mode;Display=[pscustomobject]@{Name='display';Units='unit';DisplayBoolean=$(if($mode -eq 'number'){'False'}else{$mode});DisplayString='text'};SettingString=$(if($mode -eq 'number'){''}else{'value'});SettingNumber=2} }
    }
    foreach($single in $false,$true) {
        $gpo=[pscustomobject]@{DisplayName='offline';DomainName='offline.invalid';GUID='00000000-0000-0000-0000-000000000001';GpoType='Computer';DataSet=$dataset;Linked=$true;LinksCount=2;Links=@('first','second')}
        $records=@(ConvertTo-XMLSecurityOptions -GPO $gpo -SingleObject:$single)
        [pscustomobject]@{mode=$mode;single=$single;count=$records.Count;records=$records;propertyOrder=@(foreach($record in $records){,@($record.PSObject.Properties.Name)})}|ConvertTo-Json -Depth 12 -Compress
    }
}
