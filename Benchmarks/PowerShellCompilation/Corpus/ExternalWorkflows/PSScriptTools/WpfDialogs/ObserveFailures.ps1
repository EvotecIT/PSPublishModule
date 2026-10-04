param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'WPF construction failure probe requires STA'}
$module=Import-Module $ModulePath -Force -PassThru
$rows=[Collections.Generic.List[object]]::new()
try {
 foreach($case in 'message-platform','input-platform','message-assembly','input-assembly','message-background','input-background','input-title') {
  & $module {param($windows,$refuse) $script:ProbeWindows=$windows;function script:Test-IsPSWindows {$script:ProbeWindows};if($refuse){function script:Add-Type {[CmdletBinding()]param($AssemblyName) throw 'owned WPF assembly refusal'}}else{Remove-Item Function:script:Add-Type -ErrorAction SilentlyContinue}} (-not $case.EndsWith('platform')) ($case.EndsWith('assembly'))
  $warnings=@();$caught=$null;$result=@()
  try {
   if($case.StartsWith('message')){$args=@{Message='owned failure probe';Title='PFC owned failure'};if($case.EndsWith('background')){$args.Background='owned-not-a-color'};$result=@(New-WPFMessageBox @args -WarningVariable warnings -WarningAction SilentlyContinue)}
   else{$args=@{Title='PFC owned failure'};if($case.EndsWith('background')){$args.BackgroundColor='owned-not-a-color'};if($case.EndsWith('title')){$args.Title=('x'*26)};$result=@(Invoke-InputBox @args -WarningVariable warnings -WarningAction SilentlyContinue)}
  }catch{$caught=[ordered]@{type=$_.Exception.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}
  if($case.EndsWith('platform')){if($caught -or $warnings.Count -ne 1 -or $result.Count){throw 'Expected platform refusal warning and no result'}}
  else{if(-not $caught){throw 'Expected binding/assembly failure before any dialog display'};if($case.EndsWith('assembly') -and $caught.message -notlike '*owned WPF assembly refusal*'){throw 'Assembly shadow did not intercept load'}}
  $rows.Add([ordered]@{case=$case;records=$result;warnings=@($warnings|ForEach-Object {$_.Message});caught=$caught})
 }
}finally {Remove-Module $module -Force}
[ordered]@{hostVersion=$PSVersionTable.PSVersion.ToString();apartment=[Threading.Thread]::CurrentThread.ApartmentState.ToString();observations=$rows.ToArray()}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $OutputPath -Encoding UTF8
