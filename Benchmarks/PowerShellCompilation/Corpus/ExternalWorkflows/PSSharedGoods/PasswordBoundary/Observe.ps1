param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$DeclarationPath,[Parameter(Mandatory)][string]$OutputPath,[ValidateSet('owned-call','declaration-only','real-no-target')][string]$Case='owned-call')
$ErrorActionPreference='Stop'
$definition=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $DeclarationPath).Path)
$rows=[Collections.Generic.List[object]]::new()
function New-OwnedSecureString([string]$Value){
 $secure=[Security.SecureString]::new()
 foreach($character in $Value.ToCharArray()){$secure.AppendChar($character)}
 $secure.MakeReadOnly()
 $secure
}
if($Case -eq 'declaration-only'){
 # Load and inspect the actual authored ABI. NEVER invoke this native method.
 $type=Add-Type -MemberDefinition $definition -Name NetApi32 -Namespace Win32 -PassThru
 $method=$type.GetMethod('NetUserChangePassword')
 $import=@($method.GetCustomAttributes([Runtime.InteropServices.DllImportAttribute],$false))[0]
 $signature=[ordered]@{type=$type.FullName;method=$method.Name;returnType=$method.ReturnType.FullName;parameters=@($method.GetParameters()|ForEach-Object {$_.ParameterType.FullName});library=$import.Value;charSet=$import.CharSet.ToString();nativeInvocationCount=0}
 if($signature.type -ne 'Win32.NetApi32' -or $signature.returnType -ne 'System.Boolean' -or $signature.parameters.Count -ne 4 -or @($signature.parameters|Where-Object {$_ -ne 'System.String'}).Count -or $signature.library -ne 'netapi32.dll' -or $signature.charSet -ne 'Unicode'){throw 'Authored native signature mismatch'}
 $rows.Add($signature)
}elseif($Case -eq 'real-no-target'){
 if('Win32.NetApi32' -as [type]){throw 'Use a fresh child without a native password type'}
 $module=Import-Module $ModulePath -Force -PassThru
 $savedComputer=$env:COMPUTERNAME;$savedDomain=$env:USERDOMAIN
 $old=New-OwnedSecureString 'owned-old-value'
 $new=New-OwnedSecureString 'owned-new-value'
 try {
  & $module {
   $script:PromptCalls=0
   function script:Read-Host {param([string]$Prompt) $script:PromptCalls++;''}
  }
  $prompt=& $module {Get-Command Read-Host -CommandType Function}
  $compiler=& $module {Get-Command Add-Type}
  if($prompt.Module -ne $module -or $compiler.CommandType -ne 'Cmdlet' -or $compiler.ModuleName -ne 'Microsoft.PowerShell.Utility'){throw 'Expected real Add-Type and owned empty prompt'}
  $env:COMPUTERNAME='__PFC_OWNED_LOCAL__';$env:USERDOMAIN='__PFC_OWNED_LOCAL__'
  foreach($iteration in 1,2){
   $warnings=@();$info=@()
   $success=@(Set-PasswordRemotely -UserName owned-user -OldPassword $old -NewPassword $new -WarningVariable warnings -WarningAction SilentlyContinue -InformationVariable info 6>$null)
   if($success.Count -or $info.Count -or $warnings.Count -ne 1 -or $warnings[0].Message -cne 'Set-PasswordRemotely - Password change for account failed. All parameters are required. '){throw 'Expected warning without a target'}
   $type='Win32.NetApi32' -as [type]
   if(-not $type -or $type.GetMethod('NetUserChangePassword').ReturnType -ne [bool]){throw 'Real authored native declaration missing'}
   $promptCalls=& $module {$script:PromptCalls}
   if($promptCalls -ne $iteration){throw 'Expected empty prompt per invocation'}
   $rows.Add([ordered]@{iteration=$iteration;successRecords=0;informationRecords=0;warnings=@($warnings[0].Message);promptCalls=$promptCalls;authoredTypeLoaded=$true;nativeOperationReached=$false})
  }
 }finally {$env:COMPUTERNAME=$savedComputer;$env:USERDOMAIN=$savedDomain;$old.Dispose();$new.Dispose();Remove-Module $module -Force}
}else{
 if('Win32.NetApi32' -as [type]){throw 'Real password native type must not be present in owned-call child'}
 Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'OwnedCall.cs')))
 $module=Import-Module $ModulePath -Force -PassThru
 $savedComputer=$env:COMPUTERNAME;$savedDomain=$env:USERDOMAIN
 $old=New-OwnedSecureString 'owned-old-value'
 $new=New-OwnedSecureString 'owned-new-value'
 try {
  & $module {
   param($ExpectedDefinition)
   $script:ExpectedDefinition=$ExpectedDefinition
   $script:Boundary=@{addTypeCalls=0;promptCalls=0;refuseAssembly=$false;prompt='owned-target.invalid'}
   function script:Add-Type {
    [CmdletBinding()]param([string]$MemberDefinition,[string]$Name,[string]$Namespace,[switch]$PassThru)
    $script:Boundary.addTypeCalls++
    if($MemberDefinition.Trim() -cne $script:ExpectedDefinition.Trim() -or $Name -cne 'NetApi32' -or $Namespace -cne 'Win32' -or -not $PassThru){throw 'Owned declaration boundary mismatch'}
    if($script:Boundary.refuseAssembly){throw 'Owned assembly refusal'}
    [type][PfcOwnedPasswordCall]
   }
   function script:Read-Host {param([string]$Prompt) $script:Boundary.promptCalls++;$script:Boundary.prompt}
  } $definition
  foreach($command in 'Add-Type','Read-Host'){
   $owned=& $module {param($Name) Get-Command $Name -CommandType Function} $command
   if($owned.Module -ne $module){throw 'Expected module-local boundary'}
  }
  $env:COMPUTERNAME='__PFC_OWNED_LOCAL__';$env:USERDOMAIN='__PFC_OWNED_LOCAL__'
  foreach($probe in 'success','failure','throw','assembly-refusal','prompt','empty-prompt','alias','repeat','invalid-user','invalid-password'){
   [PfcOwnedPasswordCall]::Calls=0;[PfcOwnedPasswordCall]::ArgumentsMatched=$false
   [PfcOwnedPasswordCall]::FailureResult=($probe -eq 'failure');[PfcOwnedPasswordCall]::ThrowOnCall=($probe -eq 'throw')
   & $module {param($Probe) $script:Boundary.addTypeCalls=0;$script:Boundary.promptCalls=0;$script:Boundary.refuseAssembly=($Probe -eq 'assembly-refusal');$script:Boundary.prompt=if($Probe -eq 'empty-prompt'){''}else{'owned-target.invalid'}} $probe
   $info=@();$warnings=@();$errorKind=$null
   $arguments=@{UserName='owned-user';OldPassword=$old;NewPassword=$new;DomainController='owned-target.invalid';InformationVariable='+info';WarningVariable='warnings';WarningAction='SilentlyContinue'}
   if($probe -in @('prompt','empty-prompt')){$arguments.Remove('DomainController')}
   if($probe -eq 'alias'){$arguments.Remove('DomainController');$arguments.Server='owned-target.invalid'}
   if($probe -eq 'invalid-user'){$arguments.UserName=''}
   if($probe -eq 'invalid-password'){$arguments.NewPassword=$null}
   $success=@()
   try {
    $success=@(Set-PasswordRemotely @arguments 6>$null)
    if($probe -eq 'repeat'){$success+=@(Set-PasswordRemotely @arguments 6>$null)}
   }catch{
    $errorKind=if($_.Exception.Message -like '*Owned native boundary refusal*'){'native-refusal'}elseif($_.Exception.Message -like '*Owned assembly refusal*'){'assembly-refusal'}elseif($_.Exception -is [Management.Automation.ParameterBindingException]){'parameter-binding'}else{throw}
   }
   $state=& $module {$script:Boundary.Clone()}
   $calls=[PfcOwnedPasswordCall]::Calls
   $expectedCalls=if($probe -in @('assembly-refusal','empty-prompt','invalid-user','invalid-password')){0}elseif($probe -eq 'repeat'){2}else{1}
   if($calls -ne $expectedCalls -or ($calls -gt 0 -and -not [PfcOwnedPasswordCall]::ArgumentsMatched) -or $success.Count){throw "Owned call contract mismatch: $probe"}
   $messages=@($info|ForEach-Object {[ordered]@{message=$_.MessageData.Message;color=$_.MessageData.ForegroundColor.ToString()}})
   if($probe -in @('success','failure','prompt','alias','repeat')){
    if($errorKind -or $warnings.Count -or $messages.Count -ne $expectedCalls){throw 'Expected information only'}
    $suffix=if($probe -eq 'failure'){'failed on owned-target.invalid. Please try again.'}else{'succeeded on owned-target.invalid.'}
    $color=if($probe -eq 'failure'){'Red'}else{'Cyan'}
    foreach($message in $messages){if($message.message -cne "Set-PasswordRemotely - Password change for account owned-user $suffix" -or $message.color -ne $color){throw 'Host information mismatch'}}
   }elseif($probe -eq 'empty-prompt'){
    if($warnings.Count -ne 1 -or $messages.Count -or $errorKind){throw 'Expected missing-target warning'}
   }else{
    $expectedError=if($probe -eq 'throw'){'native-refusal'}elseif($probe -eq 'assembly-refusal'){'assembly-refusal'}else{'parameter-binding'}
    if($errorKind -ne $expectedError -or $messages.Count -or $warnings.Count){throw 'Expected bounded error'}
   }
   if($state.addTypeCalls -ne $(if($probe -in @('invalid-user','invalid-password')){0}elseif($probe -eq 'repeat'){2}else{1}) -or $state.promptCalls -ne $(if($probe -in @('prompt','empty-prompt')){1}else{0})){throw 'Unexpected begin/prompt calls'}
   if('Win32.NetApi32' -as [type]){throw 'Owned execution loaded a real native type'}
   $rows.Add([ordered]@{case=$probe;ownedCalls=$calls;argumentsMatched=if($calls){[PfcOwnedPasswordCall]::ArgumentsMatched}else{$null};addTypeCalls=$state.addTypeCalls;promptCalls=$state.promptCalls;successRecords=$success.Count;information=$messages;warnings=@($warnings|ForEach-Object {$_.Message});errorKind=$errorKind;realNativeTypeLoaded=$false})
  }
  # Caller-owned SecureStrings remain usable; no plaintext is retained in the mock.
  if([Net.NetworkCredential]::new('',$old).Password -cne 'owned-old-value' -or [Net.NetworkCredential]::new('',$new).Password -cne 'owned-new-value'){throw 'Caller SecureStrings changed'}
 }finally {$env:COMPUTERNAME=$savedComputer;$env:USERDOMAIN=$savedDomain;$old.Dispose();$new.Dispose();Remove-Module $module -Force}
}
[ordered]@{case=$Case;hostVersion=$PSVersionTable.PSVersion.ToString();actualPasswordOperations=0;observations=$rows.ToArray()}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $OutputPath -Encoding UTF8
