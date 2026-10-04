param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
Import-Module ActiveDirectory -ErrorAction Stop
$assembly=[Microsoft.ActiveDirectory.Management.ADAccount].Assembly
$root=[Microsoft.ActiveDirectory.Management.ADObject]::new()
$root.defaultNamingContext='DC=offline,DC=invalid';$root.schemaNamingContext='CN=Schema,DC=offline,DC=invalid';$root.ConfigurationNamingContext='CN=Configuration,DC=offline,DC=invalid'
$guid=[guid]'12345678-1234-1234-1234-1234567890ab'
$right=[guid]'23456789-2345-2345-2345-234567890abc'
$account=[Microsoft.ActiveDirectory.Management.ADAccount]::new();$account.ObjectGUID=$guid
$module=Import-Module $ModulePath -Force -PassThru
$trace=[Collections.Generic.List[string]]::new()
$schema=@([pscustomobject]@{name='Offline schema';lDAPDisplayName='offlineSchema';schemaIDGUID=$guid.ToByteArray()})
$rights=@([pscustomobject]@{name='Offline right';displayName='Offline right display';rightsGUID=$right.ToString()})
& $module {param($root,$trace,$schema,$rights) $script:OfflineRoot=$root;$script:ProviderTrace=$trace;$script:SchemaRows=$schema;$script:RightsRows=$rights;$script:ProviderFailure=$false;foreach($name in 'Get-ADRootDSE','Get-ADObject','Get-ADDomainController'){if((Get-Command $name).CommandType -ne 'Function'){throw 'Offline provider shadow missing'}}} $root $trace $schema $rights
$observations=[Collections.Generic.List[object]]::new()
try {
 foreach($case in 'schema-first','schema-cache','schema-display','schema-map','schema-missing','guid-first','guid-cache','guid-string','guid-map','guid-missing','schema-failure','account','guid-only-defect','no-value','invalid-schema-root','invalid-guid-root','invalid-account') {
  $trace.Clear();$caught=$null;$values=@()
  if($case -eq 'schema-failure'){& $module {$script:ADSchemaMap=$null;$script:ADSchemaMapDisplayName=$null;$script:StandardRights=$null;$script:ExtendedRightsGuids=$null;$script:ProviderFailure=$true}}
  try {
   $values=@(switch($case){
    'schema-first'{Convert-ADGuidToSchema -Guid $guid -RootDSE $root}
    'schema-cache'{Convert-ADGuidToSchema -Guid $guid}
    'schema-display'{Convert-ADGuidToSchema -Guid $right -DisplayName}
    'schema-map'{Convert-ADGuidToSchema}
    'schema-missing'{Convert-ADGuidToSchema -Guid 'missing'}
    'guid-first'{Convert-ADSchemaToGuid -SchemaName 'offlineSchema' -RootDSE $root}
    'guid-cache'{Convert-ADSchemaToGuid -SchemaName 'Offline schema'}
    'guid-string'{Convert-ADSchemaToGuid -SchemaName 'Offline right display' -AsString}
    'guid-map'{Convert-ADSchemaToGuid}
    'guid-missing'{Convert-ADSchemaToGuid -SchemaName 'missing'}
    'schema-failure'{Convert-ADGuidToSchema -Guid $guid -RootDSE $root}
    'account'{ConvertTo-ImmutableID -User $account}
    'guid-only-defect'{ConvertTo-ImmutableID -ObjectGUID $guid.ToString()}
    'no-value'{ConvertTo-ImmutableID}
    'invalid-schema-root'{Convert-ADGuidToSchema -RootDSE (@('one','two'))}
    'invalid-guid-root'{Convert-ADSchemaToGuid -RootDSE (@('one','two'))}
    'invalid-account'{ConvertTo-ImmutableID -User (@('one','two'))}
   })
  }catch{$caught=[ordered]@{type=$_.Exception.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}
  if($case -in 'schema-failure','guid-only-defect','no-value','invalid-schema-root','invalid-guid-root','invalid-account'){if(-not $caught){throw 'Expected authored failure missing'}}elseif($caught){throw "Unexpected failure in ${case}: $($caught.message)"}
  if($case -like 'invalid-*' -and $trace.Count){throw 'Invalid authored type must fail before provider access'}
  if($case -in 'schema-cache','guid-cache' -and $trace.Count){throw 'Cache hit performed provider work'}
  if($case -eq 'schema-first' -and $values[0] -ne 'Offline schema'){throw 'Schema mapping mismatch'}
  if($case -eq 'schema-display' -and $values[0] -ne 'Offline right display'){throw 'Extended-right display mismatch'}
  if($case -eq 'guid-first' -and ($values[0] -isnot [guid] -or $values[0] -ne $guid)){throw 'Guid mapping/type mismatch'}
  if($case -eq 'guid-string' -and ($values[0] -isnot [string] -or $values[0] -ne $right.ToString())){throw 'Guid string mapping mismatch'}
  if($case -eq 'account' -and $values[0] -ne [Convert]::ToBase64String($guid.ToByteArray())){throw 'Real ADAccount conversion mismatch'}
  $normalized=@($values|ForEach-Object {if($_ -is [Collections.IDictionary]){$map=$_;[ordered]@{type=$map.GetType().FullName;entries=@($map.GetEnumerator()|Sort-Object Key|ForEach-Object {[ordered]@{key=$_.Key;type=$_.Value.GetType().FullName;value=$_.Value.ToString()}})}}elseif($null -eq $_){$null}else{[ordered]@{type=$_.GetType().FullName;value=$_.ToString()}}})
  $observations.Add([ordered]@{case=$case;records=$normalized;trace=$trace.ToArray();caught=$caught;callerGuid=$account.ObjectGUID.ToString()})
 }
 $metadata=@('Convert-ADGuidToSchema','Convert-ADSchemaToGuid','ConvertTo-ImmutableID'|ForEach-Object {$name=$_;$command=Get-Command $name;[ordered]@{name=$name;adParameterType=if($name -eq 'ConvertTo-ImmutableID'){$command.Parameters['User'].ParameterType.FullName}else{$command.Parameters['RootDSE'].ParameterType.FullName}}})
} finally {Remove-Module $module -Force;Remove-Module ActiveDirectory -Force}
[ordered]@{hostVersion=$PSVersionTable.PSVersion.ToString();adAssembly=$assembly.FullName;adAssemblySha256=(Get-FileHash $assembly.Location).Hash.ToLowerInvariant();metadata=$metadata;observations=$observations.ToArray()}|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $OutputPath -Encoding UTF8
