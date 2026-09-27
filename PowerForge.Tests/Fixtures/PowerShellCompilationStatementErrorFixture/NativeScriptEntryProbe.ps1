param([string]$Assembly, [string]$Factory = 'Generic.Compiler.StatementErrors.CompiledRoot')
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskRoot=Join-Path $tempRoot ('pfc-native-script-entry-'+[guid]::NewGuid().ToString('N'))
if(-not [IO.Path]::GetFullPath($taskRoot).StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)){throw 'Probe output escaped its temporary root.'}
$null=New-Item -ItemType Directory -Path $taskRoot -ErrorAction Stop
$Source=Join-Path $taskRoot 'authored.ps1'
$Output=Join-Path $taskRoot 'observations.json'
try {
@'
param([ValidateScript({$global:ValidateCount++; $_ -ne 'reject'})][string]$Value = $($global:BindCount++; 'default'))
$Scratch='owned'
if($Value -eq 'rebind'){$Value='changed'}
$Value
$global:BindCount
$global:ValidateCount
$PSBoundParameters.ContainsKey('Value')
$args.Count
$MyInvocation.MyCommand.CommandType.ToString()
$PSCommandPath
$PSScriptRoot
$Scratch
'@ | Set-Content -LiteralPath $Source -Encoding UTF8

$ErrorActionPreference='Stop'
Add-Type -Path $Assembly
$entryFactory = [Reflection.Assembly]::LoadFrom($Assembly).GetType($Factory, $true)
$results=@()
foreach($compiled in @($false,$true)) {
 foreach($arguments in @(@(), @('-Value','explicit'), @('-Value','rebind'), @('-Value','explicit','tail'))) {
  $runspace=[runspacefactory]::CreateRunspace();$runspace.Open()
  $ps=[powershell]::Create();$ps.Runspace=$runspace
  $null=$ps.AddScript('$global:BindCount=0;$global:ValidateCount=0');$null=$ps.Invoke();$ps.Commands.Clear()
  try {
   if($compiled){
    $null=$ps.AddScript('param($Factory,$Path,$Named,$Positional) try { $Info = $Factory::Create($ExecutionContext.SessionState,$Path); & $Info @Named @Positional } catch { throw }', $false)
    $null=$ps.AddParameter('Factory',$entryFactory);$null=$ps.AddParameter('Path',$Source)
    $named=@{};if($arguments.Count){$named['Value']=$arguments[1]}
    $extra=@();if($arguments.Count -gt 2){$extra=@($arguments[2])}
    $null=$ps.AddParameter('Named',$named);$null=$ps.AddParameter('Positional',$extra)
   }else{
    $null=$ps.AddCommand($Source)
    if($arguments.Count){$null=$ps.AddParameter('Value',$arguments[1]);if($arguments.Count -gt 2){$null=$ps.AddArgument($arguments[2])}}
   }
   $records=@($ps.Invoke())
   if($ps.HadErrors){throw ($ps.Streams.Error|Out-String)}
   if($compiled -and [Generic.Compiler.StatementErrors.NativeScriptEntryFixture]::CallbackInvocations -ne 1){throw 'Expected exactly one compiled entry callback.'}
   $results+= [pscustomobject]@{Compiled=$compiled; Explicit=($arguments.Count -gt 0); Values=@($records|ForEach-Object{$_}); Types=@($records|ForEach-Object{$_.GetType().FullName});}
  }catch { Write-Output "Compiled=$compiled arguments=$($arguments -join ',')"; Write-Output $_.Exception.ToString(); throw }finally{$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Output -Encoding UTF8;$ps.Dispose();$runspace.Dispose()}
 }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Output -Encoding UTF8
$expectedValues=@('default','explicit','changed','explicit')
for($i=0;$i -lt 4;$i++) {
 $a=$results[$i];$b=$results[$i+4]
 $expected=@($expectedValues[$i], $(if($i -eq 0){1}else{0}), $(if($i -eq 0){0}elseif($i -eq 2){2}else{1}), ($i -ne 0), $(if($i -eq 3){1}else{0}), 'ExternalScript', $Source, $taskRoot, 'owned')
 if($a.Values.Count -ne $expected.Count){throw "Original cardinality differs from the authored contract at case $i"}
 for($n=0;$n -lt $expected.Count;$n++){if($a.Values[$n] -cne $expected[$n]){throw "Original contract mismatch at case $i record $n"}}
 if($a.Explicit -ne $b.Explicit -or $a.Values.Count -ne $b.Values.Count){throw "Cardinality mismatch at case $i"}
 for($n=0;$n -lt $a.Values.Count;$n++) {
  if($a.Values[$n] -cne $b.Values[$n] -or $a.Types[$n] -cne $b.Types[$n]){throw "Value/type mismatch at case $i record $n"}
 }
}
# A rejected validation value fails before either original or compiled body runs.
$failures=@()
foreach($compiled in @($false,$true)) {
 $runspace=[runspacefactory]::CreateRunspace();$runspace.Open()
 $ps=[powershell]::Create();$ps.Runspace=$runspace
 try {
  if($compiled){
   $null=$ps.AddScript('param($Factory,$Path) try { $Info = $Factory::Create($ExecutionContext.SessionState,$Path); & $Info -Value reject } catch { throw }', $false)
   $null=$ps.AddParameter('Factory',$entryFactory);$null=$ps.AddParameter('Path',$Source)
  }else{$null=$ps.AddCommand($Source);$null=$ps.AddParameter('Value','reject')}
  $caught=$null
  try{$rejectedRecords=@($ps.Invoke())}catch{$caught=$_.Exception}
  if(-not $ps.HadErrors -or $rejectedRecords.Count -gt 0){throw 'Expected native validation failure and no body output.'}
  if($ps.Streams.Error.Count -eq 1){$errorRecord=$ps.Streams.Error[0]}elseif($null -ne $caught){$errorRecord=$caught.InnerException.ErrorRecord}else{throw 'Expected an observable native validation error.'}
  $failures+= [pscustomobject]@{State=$ps.InvocationStateInfo.State;ErrorId=$errorRecord.FullyQualifiedErrorId;Category=$errorRecord.CategoryInfo.Category;Message=$errorRecord.Exception.Message}
  if($compiled -and [Generic.Compiler.StatementErrors.NativeScriptEntryFixture]::CallbackInvocations -ne 0){throw 'Compiled body ran after binding rejection.'}
 }finally{$ps.Dispose();$runspace.Dispose()}
}
Write-Output ('Validation pipeline states: original={0}, compiled-wrapper={1}' -f $failures[0].State,$failures[1].State)
if($failures[0].State -ne $failures[1].State){throw 'Original/compiled invocation state mismatch.'}
if($failures[0].ErrorId -cne $failures[1].ErrorId -or $failures[0].Category -ne $failures[1].Category -or $failures[0].Message -cne $failures[1].Message){throw 'Original/compiled binding rejection mismatch.'}
'Native script entry scope/binding comparison passed: 4 cases plus binding rejection.'
}finally{if(Test-Path -LiteralPath $taskRoot){Remove-Item -LiteralPath $taskRoot -Recurse -ErrorAction Stop}}
