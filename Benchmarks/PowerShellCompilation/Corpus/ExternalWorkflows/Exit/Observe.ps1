param([Parameter(Mandatory)][string]$ModulePath,[ValidateSet('available','missing','config-failure','readiness-failure','query-failure','query-empty','query-one')][string]$Case,[Parameter(Mandatory)][string]$OutputRoot)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$events=[Collections.Generic.List[string]]::new()
if($Case -in 'available','missing'){
 $module=(Get-Command Test-ModuleAvailability).Module
 & $module {param($present) $script:Available=$present;function script:Search-Command {$script:Available}} ($Case -eq 'available')
}else{
 $module=(Get-Command Invoke-ADComputerInventoryChildProcess).Module
 & $module {
  param($caseName,$root)
  $script:CaseName=$caseName
  $script:OfflineConfiguration=[pscustomobject]@{
   InitializationPath=(Join-Path $root 'initialization.txt');ReadyPath=(Join-Path $root 'ready.txt');ProgressPath=(Join-Path $root 'progress.txt');DataPath=(Join-Path $root 'data.csv');SuccessPath=(Join-Path $root 'success.txt');ErrorPath=(Join-Path $root 'error.txt')
   Server='offline.invalid';Filter='*';Properties=@('Name');PageSize=100;ProgressIntervalMilliseconds=1000;SearchBase=$null
  }
  function script:Import-Clixml {if($script:CaseName -eq 'config-failure'){throw 'Offline configuration failure'};$script:OfflineConfiguration}
  function script:Import-Module {param($Name) if($Name -ne 'ActiveDirectory'){throw 'Unexpected module request'}}
  function script:Get-ADRootDSE {if($script:CaseName -eq 'readiness-failure'){throw 'Offline readiness failure'};[pscustomobject]@{Server='offline.invalid'}}
  function script:Get-ADComputer {
   if($script:CaseName -eq 'query-failure'){throw 'Offline query failure'}
   if($script:CaseName -eq 'query-one'){
    [pscustomobject]@{Name='owned';DNSHostName='owned.invalid';SamAccountName='owned$';DistinguishedName='CN=owned';Enabled=$true;OperatingSystem='offline';OperatingSystemVersion='1';LastLogonDate=[datetime]'2024-01-02T03:04:05';PasswordLastSet=$null;PasswordExpired=$false;servicePrincipalName=@('offline/owned');logonCount=1;ManagedBy=$null;Description='owned record';WhenCreated=[datetime]'2023-01-02T03:04:05';WhenChanged=$null;ProtectedFromAccidentalDeletion=$false}
   }
  }
 } $Case $OutputRoot
}
try {
 $events.Add('before-call')
 if($Case -in 'available','missing'){
  Test-ModuleAvailability -WarningAction SilentlyContinue -WarningVariable warnings
 }else{
  Invoke-ADComputerInventoryChildProcess -ConfigurationPath 'offline' -ErrorAction Continue -ErrorVariable errors 2>$null
 }
 $events.Add('after-call')
}finally{
 $files=@(Get-ChildItem -LiteralPath $OutputRoot -File | Sort-Object Name | ForEach-Object {[pscustomobject]@{name=$_.Name;content=[IO.File]::ReadAllText($_.FullName)}})
 [pscustomobject]@{case=$Case;events=@($events);warnings=@($warnings|ForEach-Object {$_.Message});errors=@($errors|ForEach-Object {$_.FullyQualifiedErrorId});files=$files}|ConvertTo-Json -Depth 6 -Compress
}
$events.Add('after-finally')
[pscustomobject]@{case=$Case;complete=@($events)}|ConvertTo-Json -Compress
