param([string]$ResultPath,[string]$SourcePath,[string]$Case)
$trace=[Collections.Generic.List[string]]::new()
$failure=$null
try{
 Import-Module -Name $SourcePath -Force
 if((Get-OfflineTransferMarker) -ne 1){throw 'Generated marker mismatch'}
 if($Case -eq 'format-loop'){
  for($i=0;$i -lt 3;$i++){$trace.Add("before:$i");Format-Stream -InputObject @() -Stream Output;$trace.Add("after:$i")}
  $trace.Add('tail')
 }elseif($Case -eq 'format-outside'){
  $trace.Add('before');Format-Stream -InputObject @() -Stream Output;$trace.Add('after')
 }elseif($Case -eq 'exit-loop'){
  function global:Search-Command {param($CommandName);$false}
  for($i=0;$i -lt 3;$i++){$trace.Add("before:$i");Test-ModuleAvailability 3>$null;$trace.Add("after:$i")}
  $trace.Add('tail')
 }elseif($Case -eq 'drive-loop'){
 for($i=0;$i -lt 3;$i++){$trace.Add("before:$i");New-PSDriveHere -Path (Join-Path (Split-Path $ResultPath) '---') -Confirm:$false 3>$null;$trace.Add("after:$i")};$trace.Add('tail')
}elseif($Case -eq 'child-exit'){
 function global:Import-Clixml {param($LiteralPath,$ErrorAction);throw 'offline-clixml-rejection'}
 $trace.Add('before');Invoke-ADComputerInventoryChildProcess -ConfigurationPath 'offline.invalid' -ErrorAction Continue 2>$null;$trace.Add('after')
}elseif($Case -eq 'synthetic-break-loop'){
  function Invoke-OfflineTransfer {try{$trace.Add('function-before');break;$trace.Add('function-after')}catch{$trace.Add('caught')}finally{$trace.Add('function-finally')}}
  for($i=0;$i -lt 3;$i++){$trace.Add("before:$i");Invoke-OfflineTransfer;$trace.Add("after:$i")};$trace.Add('tail')
 }elseif($Case -eq 'synthetic-exit-finally'){
  function Invoke-OfflineTransfer {try{$trace.Add('function-before');exit 7;$trace.Add('function-after')}catch{$trace.Add('caught')}finally{$trace.Add('function-finally')}}
  try{Invoke-OfflineTransfer;$trace.Add('after')}catch{$trace.Add('outer-caught')}finally{$trace.Add('outer-finally')}
 }
}catch{$failure=$_.FullyQualifiedErrorId}finally{
 $record=[pscustomobject]@{case=$Case;trace=@($trace.ToArray());failure=$failure}
 [IO.File]::WriteAllText($ResultPath,($record|ConvertTo-Json -Depth 5 -Compress),[Text.UTF8Encoding]::new($false))
}
