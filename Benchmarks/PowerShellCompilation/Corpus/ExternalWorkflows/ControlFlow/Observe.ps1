param([ValidateSet('plain','loop','none')][string]$Mode,[Parameter(Mandatory)][string]$ModulePath,[ValidateSet('Accounts','Drive')][string]$Workflow='Accounts')
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$module=if($Workflow -eq 'Accounts'){(Get-Command Get-ADServiceAccountsToProcess).Module}else{(Get-Command New-PSDriveHere).Module}
& $module {function script:Write-Color {} ; function script:Get-Date {[datetime]'2024-01-02T03:04:05'}}
if($Workflow -eq 'Drive'){
 & $module {
  function script:Get-Item {[pscustomobject]@{Name='!!!';PSProvider='FileSystem'}}
  function script:New-PSDrive {throw 'Forbidden drive creation'}
  function script:Set-Location {throw 'Forbidden location change'}
 }
}
$accounts=@([pscustomobject]@{Name='excluded';DistinguishedName='CN=excluded'},[pscustomobject]@{Name='included';DistinguishedName='CN=included'})
$exclusions=if($Mode -eq 'none'){@()}else{@('excluded')}
$events=[Collections.Generic.List[string]]::new()
try {
 if($Mode -eq 'loop'){
  foreach($i in 1,2){
   $events.Add('before-call:'+$i)
   $records=if($Workflow -eq 'Accounts'){@(Get-ADServiceAccountsToProcess -Type Disable -Accounts $accounts -ActionIf @{} -Exclusions $exclusions)}else{@(New-PSDriveHere -Path $PSScriptRoot -WarningAction SilentlyContinue)}
   $events.Add('returned:'+($records.Name -join ','))
  }
 }else{
  $events.Add('before-call')
  $records=if($Workflow -eq 'Accounts'){@(Get-ADServiceAccountsToProcess -Type Disable -Accounts $accounts -ActionIf @{} -Exclusions $exclusions)}else{@(New-PSDriveHere -Path $PSScriptRoot -WarningAction SilentlyContinue)}
  $events.Add('returned:'+($records.Name -join ','))
 }
 $events.Add('after-call')
}finally{
 [pscustomobject]@{mode=$Mode;events=@($events)}|ConvertTo-Json -Compress
}
$events.Add('after-finally')
[pscustomobject]@{mode=$Mode;complete=@($events)}|ConvertTo-Json -Compress
