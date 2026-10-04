# Offline contract probe for two pinned GpoZaurr conversion functions.
# Supply unchanged source modules or generated module artifacts, one function per module.
param([string]$SystemServicesModulePath,[string]$PrinterModulePath)
$ErrorActionPreference='Stop'
$null=Import-Module $SystemServicesModulePath -Force
$null=Import-Module $PrinterModulePath -Force
& (Get-Command ConvertTo-XMLPrinterInternal).Module { $script:Actions=@{D='OfflineCreate'} }
foreach($mode in 'empty','true','false','bad','missing','many') {
    $dataset=if($mode -eq 'empty'){@()}else{
        foreach($text in $(if($mode -eq 'many'){@('True','False','bad')}else{@($mode)})) {
            [pscustomobject]@{Name='offline-service';StartUpMode='Automatic';SecurityDescriptor=[pscustomobject]@{
                AuditingPresent=[pscustomobject]@{'#text'=$(if($mode -eq 'missing'){$null}else{$text})}
                PermissionsPresent=[pscustomobject]@{'#text'='False'}
            }}
        }
    }
    foreach($single in $false,$true) {
        $gpo=[pscustomobject]@{DisplayName='offline';DomainName='offline.invalid';GUID='00000000-0000-0000-0000-000000000001';GpoType='Computer';DataSet=$dataset;Linked=$true;LinksCount=2;Links=@('first','second')}
        $records=@(ConvertTo-XMLSystemServices -GPO $gpo -SingleObject:$single)
        [pscustomobject]@{kind='service';mode=$mode;single=$single;count=$records.Count;records=$records;propertyOrder=@(foreach($record in $records){,@($record.PSObject.Properties.Name)})}|ConvertTo-Json -Depth 14 -Compress
    }
}
foreach($dateMode in 'valid','bad','null') {
    $date=switch($dateMode){'valid' {'2026-09-27T12:34:56Z'} 'bad' {'not-a-date'} 'null' {$null}}
    foreach($limited in $false,$true) {
        foreach($action in 'D','') {
            $gpo=[pscustomobject]@{DisplayName='offline';DomainName='offline.invalid';GUID='00000000-0000-0000-0000-000000000001';GpoType='Computer';Linked=$true;LinksCount=2;Links=@('first','second')}
            $entry=[pscustomobject]@{changed=$date;bypassErrors='1';GPOSettingOrder=2;Filter='offline';Properties=[pscustomobject]@{action=$action;comment='offline';path='/offline/printer';location='offline';useDNS='1';default='0';port='123'}}
            $records=@(ConvertTo-XMLPrinterInternal -GPO $gpo -Entry $entry -Type 'Network' -Limited:$limited)
            $record=$records[0]
            $value=$record.Changed
            [pscustomobject]@{kind='printer';mode=$dateMode;limited=$limited;authoredAction=$action;count=$records.Count;changedType=$(if($null -ne $value){$value.GetType().FullName}else{$null});changed=$(if($value -is [datetime]){$value.ToString('o')}else{$value});action=$record.Action;path=$record.Path;useDNS=$record.UseDNS;default=$record.Default;port=$record.PortNumber;propertyOrder=@($record.PSObject.Properties.Name)}|ConvertTo-Json -Depth 8 -Compress
        }
    }
}
