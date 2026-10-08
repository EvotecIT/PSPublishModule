# Offline probe for unchanged PSSharedGoods computer/OS projection functions.
# SourceDirectory supplies the pinned four original files; ModulePath supplies a generated module.
param([string]$SourceDirectory,[string]$ModulePath)
$ErrorActionPreference='Stop'
Import-Module Microsoft.PowerShell.Management -ErrorAction Stop
if($ModulePath){$module=Import-Module $ModulePath -Force -PassThru}else{
 $module=New-Module -Name OfflineHostEnumOriginal -ArgumentList $SourceDirectory -ScriptBlock {
  param($root)
  . (Join-Path $root 'Public/Computers/Get-ComputerSystem.ps1')
  . (Join-Path $root 'Public/Computers/Get-ComputerOperatingSystem.ps1')
  . (Join-Path $root 'Public/Converts/ConvertTo-OperatingSystem.ps1')
  . (Join-Path $root 'Public/Converts/ConvertFrom-LanguageCode.ps1')
 }
 Import-Module $module -Force
}
& $module {
 $script:ProbeCalls=[Collections.Generic.List[object]]::new()
 function script:Get-CimData {
  param($ComputerName,$Protocol,$Credential,$Class,$Properties)
  $script:ProbeCalls.Add([pscustomobject]@{computer=@($ComputerName);protocol=$Protocol;class=$Class;properties=@($Properties);credential=[bool]$Credential})
  if($script:ProbeCase -eq 'error'){throw 'offline-provider-failure'}
  if($script:ProbeCase -eq 'empty'){return}
  $count=if($script:ProbeCase -eq 'many'){2}else{1}
  foreach($index in 1..$count){
   $value=switch($script:ProbeCase){'null'{$null};'named'{if($Class -eq 'Win32_ComputerSystem'){'Desktop'}else{'SmallBusinessServer'}};'undefined'{65535};'invalid'{'bad-enum'};default{1}}
   [pscustomobject]@{PSComputerName=if($script:ProbeCase -eq 'fallback'){''}else{'offline-host'};Name='offline';Manufacturer='offline-vendor';Domain='offline.invalid';Model='offline-model';Systemtype='x64';PrimaryOwnerName='offline-owner';PCSystemType=$value;PartOfDomain=$false;CurrentTimeZone=60;BootupState='Normal';SystemFamily='offline-family';Roles=@('one','two');Caption='Windows Server 2022';Version='10.0.20348';OSArchitecture='64-bit';OSLanguage=1033;OSProductSuite=$value;InstallDate='fixed-install';LastBootUpTime='fixed-boot';LocalDateTime='fixed-time';SerialNumber='offline-serial';BootDevice='offline-device';WindowsDirectory='offline-directory';CountryCode='1'}
  }
 }
}
foreach($command in 'Get-ComputerSystem','Get-ComputerOperatingSystem'){
 foreach($case in 'empty','null','valid','named','many','undefined','invalid','error','fallback'){
  foreach($all in $false,$true){
   & $module {param($case) $script:ProbeCase=$case;$script:ProbeCalls.Clear()} $case
   $errorData=$null;$rows=@()
   try{$rows=@(& $command -ComputerName offline-host -Protocol Dcom -All:$all | ForEach-Object {
    $enum=if($command -eq 'Get-ComputerSystem'){$_.PCSystemType}else{$_.OSProductSuite}
    [pscustomobject]@{keys=@($_.PSObject.Properties.Name);value=$_;enumType=if($null -ne $enum){$enum.GetType().FullName}else{$null};enumText=if($null -ne $enum){$enum.ToString()}else{$null}}
   })}catch{$errorData=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
   [pscustomobject]@{command=$command;case=$case;all=$all;count=$rows.Count;records=@($rows);error=$errorData;calls=@(& $module {$script:ProbeCalls.ToArray()})}|ConvertTo-Json -Depth 12 -Compress
  }
 }
}
