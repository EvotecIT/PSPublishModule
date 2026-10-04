param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath,[ValidateSet('real','type-failure')][string]$Case='real')
$ErrorActionPreference='Stop'
if('PInvoke.Win32Utils' -as [type]){throw 'Use a fresh child process without the authored runtime type'}
$module=Import-Module $ModulePath -Force -PassThru
$drive=[IO.Path]::GetPathRoot($env:SystemRoot).TrimEnd('\')
$rows=@(
 [pscustomobject]@{ComputerName=$env:COMPUTERNAME;PSComputerName=$env:COMPUTERNAME;DeviceID=$drive;DriveType=3;ProviderName='';FreeSpace=3221225472;Size=10737418240;VolumeName='owned-local'},
 [pscustomobject]@{ComputerName='offline-remote.invalid';PSComputerName='offline-remote.invalid';DeviceID='R:';DriveType=4;ProviderName='offline-provider';FreeSpace=1;Size=3;VolumeName='owned-remote'},
 [pscustomobject]@{ComputerName=$env:COMPUTERNAME;PSComputerName=$null;DeviceID=$null;DriveType=2;ProviderName='';FreeSpace=0;Size=0;VolumeName='owned-zero'},
 [pscustomobject]@{ComputerName=$env:COMPUTERNAME;PSComputerName=$null;DeviceID='compiler-offline-unmapped:';DriveType=0;ProviderName='';FreeSpace=0;Size=0;VolumeName='owned-unmapped'}
)
$trace=[Collections.Generic.List[object]]::new()
& $module {param($rows,$trace,$refuse) $script:DiskRows=$rows;$script:ProviderTrace=$trace;$script:ProviderFailure=$false;if($refuse){function script:Add-Type {param($TypeDefinition) throw 'owned offline native type failure'}}} $rows $trace ($Case -eq 'type-failure')
$observations=[Collections.Generic.List[object]]::new()
try {
 foreach($name in $(if($Case -eq 'real'){@('first','repeat','local-only','all','mb-rounding','tb','provider-failure','empty','invalid-size')}else{@('type-failure')})) {
  $trace.Clear()
  & $module {param($failure,$empty,$rows) $script:ProviderFailure=$failure;$script:DiskRows=if($empty){@()}else{$rows}} ($name -eq 'provider-failure') ($name -eq 'empty') $rows
  $arguments=@{}
  switch($name){'local-only'{$arguments.OnlyLocalDisk=$true};'all'{$arguments.All=$true};'mb-rounding'{$arguments.Size='MB';$arguments.RoundingPlace=3;$arguments.RoundingPlacePercent=4;$arguments.Protocol='Dcom';$arguments.ComputerName=@('offline-one.invalid','offline-two.invalid')};'tb'{$arguments.Size='TB';$arguments.RoundingPlace=5};'invalid-size'{$arguments.Size='invalid'}}
  $caught=$null;$result=@()
  try {$result=@(Get-ComputerDiskLogical @arguments)}catch{$caught=[ordered]@{type=$_.Exception.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}
  if($name -in @('type-failure','provider-failure','invalid-size')) {
   if(-not $caught){throw 'Expected failure was not preserved'}
   if($name -eq 'type-failure' -and ($trace.Count -ne 0 -or $caught.message -notlike '*owned offline native type failure*')){throw 'Type failure must precede provider invocation'}
  } else {
   if($caught){throw "Unexpected failure: $($caught.message)"}
   if($name -in @('first','repeat')) {
    if($result.Count -ne 4 -or -not $result[0].DiskPartition -or $result[0].FreeSpace -ne 3 -or $result[0].TotalSpace -ne 10 -or $result[0].UsedSpace -ne 7 -or $result[0].FreePercent -ne 30){throw 'Actual local query or arithmetic failed'}
    if($result[1].DiskPartition -ne '' -or $result[2].DiskPartition -ne '' -or $result[3].DiskPartition -ne '' -or $result[2].FreePercent -cne '0'){throw 'Remote/missing/unmapped or zero-size behavior changed'}
   }
   if($name -eq 'local-only' -and ($result.Count -ne 1 -or $result[0].DriveType -ne 'Local Disk')){throw 'Local disk filter failed'}
   if($name -eq 'all' -and ($result.Count -ne 4 -or -not [object]::ReferenceEquals($result[0],$rows[0]) -or $trace[0].properties[0] -ne '*')){throw 'All must return borrowed provider records'}
   if($name -eq 'mb-rounding' -and ($result[0].FreeSpace -ne 3072 -or $result[1].FreePercent -ne 33.3333)){throw 'Unit/rounding contract failed'}
   if($name -eq 'tb' -and $result[0].TotalSpace -ne 0.00977){throw 'TB conversion failed'}
   if($name -eq 'empty' -and $result.Count -ne 0){throw 'Empty provider must emit no records'}
  }
  $observations.Add([ordered]@{case=$name;records=$result;trace=$trace.ToArray();caught=$caught;nativeTypeLoaded=[bool]('PInvoke.Win32Utils' -as [type])})
 }
 $native='PInvoke.Win32Utils' -as [type]
 $abi=if($native){$method=$native.GetMethod('QueryDosDevice');$attr=@($method.GetCustomAttributes([Runtime.InteropServices.DllImportAttribute],$false))[0];[ordered]@{type=$native.FullName;library=$attr.Value;charSet=$attr.CharSet.ToString();setLastError=$attr.SetLastError;returnType=$method.ReturnType.FullName;parameters=@($method.GetParameters()|ForEach-Object {$_.ParameterType.FullName})}}else{$null}
 if($Case -eq 'real' -and ($abi.library -ne 'kernel32.dll' -or $abi.charSet -ne 'Auto' -or -not $abi.setLastError -or $abi.returnType -ne 'System.UInt32')){throw 'Authored native ABI was not loaded'}
} finally {Remove-Module $module -Force}
[ordered]@{case=$Case;hostVersion=$PSVersionTable.PSVersion.ToString();abi=$abi;observations=$observations.ToArray()}|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $OutputPath -Encoding UTF8
