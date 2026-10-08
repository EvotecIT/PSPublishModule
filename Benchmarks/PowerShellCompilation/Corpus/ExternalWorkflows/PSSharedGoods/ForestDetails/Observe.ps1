param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
Import-Module ActiveDirectory -ErrorAction Stop
$module=Import-Module $ModulePath -Force -PassThru
$trace=[Collections.Generic.List[string]]::new()
$controllers=@{}
foreach($domain in 'root.offline.invalid','child.offline.invalid'){
 $controllers[$domain]=@(foreach($prefix in 'dc','rodc'){[pscustomobject]@{Domain=$domain;HostName="$prefix.$domain";Name=$prefix;Forest='root.offline.invalid';Site='owned-site';IPv4Address='192.0.2.1';IPv6Address='';IsGlobalCatalog=$true;IsReadOnly=($prefix -eq 'rodc');OperationMasterRoles=@('PDCEmulator');OperatingSystem='owned';OperatingSystemVersion='1';LdapPort=389;SslPort=636;ComputerObjectDN="CN=$prefix,DC=offline,DC=invalid";NTDSSettingsObjectDN="CN=NTDS,CN=$prefix,DC=offline,DC=invalid"}})
}
& $module {param($trace,$controllers) $script:Trace=$trace;$script:Controllers=$controllers;foreach($name in 'Get-ADForest','Get-ADDomainController','Get-ADDomain','Get-ADObject','Test-Connection','Test-WinRM','Test-ComputerPort'){if((Get-Command $name).CommandType -ne 'Function'){throw 'Offline provider shadow missing'}}} $trace $controllers
$rows=[Collections.Generic.List[object]]::new();$prior=$Global:ProgressPreference;$cache=$null
try {
 foreach($case in 'normal','typed-fallback','fallback-partial','listing-failure','forest-failure','empty-forest','discovery-failure','include-domain','include-controller','skip-rodc','availability-ping','credential','extended','cache-subset','cache-skip-rodc'){
  $trace.Clear();& $module {param($case) $script:Case=$case} $case
  $Global:ProgressPreference='Continue';$warnings=@();$caught=$null;$arguments=@{}
  switch($case){'include-domain'{$arguments.IncludeDomains='root.offline.invalid'};'include-controller'{$arguments.IncludeDomainControllers='dc'};'skip-rodc'{$arguments.SkipRODC=$true};'availability-ping'{$arguments.TestAvailability=$true;$arguments.Test='Ping'};'credential'{$arguments.Credential=[pscredential]::new('owned-offline',(ConvertTo-SecureString 'owned-nonsecret' -AsPlainText -Force));$arguments.PreferWritable=$true};'extended'{$arguments.Extended=$true};'cache-subset'{$arguments.ExtendedForestInformation=$cache;$arguments.IncludeDomains='root.offline.invalid'};'cache-skip-rodc'{$arguments.ExtendedForestInformation=$cache;$arguments.SkipRODC=$true}}
  try{$result=@(Get-WinADForestDetails @arguments -WarningVariable warnings -WarningAction SilentlyContinue)}catch{$result=@();$caught=[ordered]@{type=$_.Exception.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}
  if($caught){throw "Unexpected forest failure: $($caught.message)"}
  if($case -in 'forest-failure','empty-forest'){if($result.Count){throw 'Early-return case emitted a result'}}else{if($result.Count -ne 1){throw 'Expected one findings dictionary'}}
  if($case -eq 'normal'){$cache=$result[0];if($cache.ForestDomainControllers.Count -ne 4){throw 'Normal forest cardinality mismatch'}}
  if($case -eq 'extended'){$cache=$result[0];if($cache.DomainsExtended.Count -ne 2 -or $cache.DomainsExtendedNetBIOS.Count -ne 2){throw 'Extended forest indexing mismatch'}}
  if($case -eq 'typed-fallback' -and (-not @($trace|Where-Object {$_ -like 'replica:*'}).Count -or $result[0].ForestDomainControllers.Count -ne 4)){throw 'Real AD exception did not reach replica fallback'}
  if($case -eq 'fallback-partial' -and ($warnings.Count -ne 2 -or $result[0].ForestDomainControllers.Count -ne 2)){throw 'Partial replica continuation mismatch'}
  if($case -in 'include-domain','include-controller','skip-rodc','cache-subset','cache-skip-rodc' -and $result[0].ForestDomainControllers.Count -ne 2){throw 'Filtering cardinality mismatch'}
  if($case -like 'cache-*' -and ($trace.Count -or $cache.Domains.Count -ne 2 -or $cache.ForestDomainControllers.Count -ne 4)){throw 'Cached filtering invoked providers or mutated its caller'}
  $expectedProgress=if($case -in 'forest-failure','empty-forest','cache-subset','cache-skip-rodc'){'SilentlyContinue'}else{'Continue'}
  if($Global:ProgressPreference.ToString() -ne $expectedProgress){throw 'Authored progress-preference effect changed'}
  if($case -eq 'availability-ping' -and (@($trace|Where-Object {$_ -like 'port:*'}).Count -ne 4 -or @($trace|Where-Object {$_ -like 'winrm:*'}).Count)){throw 'Authored Ping-only port-probe defect changed'}
  if($case -eq 'cache-subset' -and ($result[0].DomainsExtended.Contains('child.offline.invalid') -or -not $result[0].DomainsExtendedNetBIOS.Contains('CHILD'))){throw 'Authored stale NetBIOS-index effect changed'}
  $summary=@($result|ForEach-Object {[ordered]@{domains=@($_.Domains);controllers=@($_.ForestDomainControllers|ForEach-Object {[ordered]@{name=$_.HostName;readOnly=$_.IsReadOnly;domain=$_.Domain;guid=$_.DsaGuid;ping=$_.Pingable;winrm=$_.WinRM;port=$_.PortOpen}});queryKeys=@($_.QueryServers.Keys|Sort-Object);extendedKeys=@($_.DomainsExtended.Keys|Sort-Object);netbiosKeys=@($_.DomainsExtendedNetBIOS.Keys|Sort-Object)}})
  $rows.Add([ordered]@{case=$case;result=$summary;warnings=@($warnings|ForEach-Object {$_.Message});trace=$trace.ToArray();progressAfter=$Global:ProgressPreference.ToString();callerCacheDomains=if($cache){@($cache.Domains)}else{@()}})
 }
}finally {$Global:ProgressPreference=$prior;Remove-Module $module -Force;Remove-Module ActiveDirectory -Force}
[ordered]@{hostVersion=$PSVersionTable.PSVersion.ToString();catchType=[Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException].AssemblyQualifiedName;observations=$rows.ToArray()}|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $OutputPath -Encoding UTF8
