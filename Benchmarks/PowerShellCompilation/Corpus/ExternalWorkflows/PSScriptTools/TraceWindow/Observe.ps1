param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Owned Trace window probe requires STA'}
Add-Type -AssemblyName PresentationCore
[System.Windows.Media.RenderOptions]::ProcessRenderMode=[System.Windows.Interop.RenderMode]::SoftwareOnly
function global:Get-Date { throw 'Date command lookup escaped fixture' }
function global:Get-CimInstance { throw 'CIM command lookup escaped fixture' }
function global:New-RunspaceCleanupJob { throw 'Cleanup command lookup escaped fixture' }
$global:TraceEnabled=$true
$module=Import-Module $ModulePath -Force -PassThru
try {
 foreach($name in @('Get-Date','Get-CimInstance','New-RunspaceCleanupJob')){
  $resolved=& $module {param($n) Get-Command $n -ErrorAction Stop} $name
  if($resolved.CommandType -ne 'Function' -or $resolved.ModuleName -ne $module.Name){throw "Authored $name command did not resolve to fixture provider"}
 }
 $init=@(Trace-Message -Title 'Owned Trace' -Width 600 -Height 360)
 if($init.Count){throw 'Trace init unexpectedly wrote success output'}
 $append=@(Trace-Message -Message 'compiler-note')
 if($append.Count){throw 'Trace append unexpectedly wrote success output'}
 $owned=@(Get-CapturedCleanup)
 if($owned.Count -ne 1 -or $owned[0].Sleep -ne 30){throw 'Authored runspace cleanup boundary was not captured exactly once'}
 $deadline=[DateTime]::UtcNow.AddSeconds(20)
 while(-not $owned[0].Handle.IsCompleted -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
 if(-not $owned[0].Handle.IsCompleted){throw 'Owned trace runspace did not finish after Quit'}
 $runspaceOutput=@($owned[0].PowerShell.EndInvoke($owned[0].Handle))
 $errors=@($owned[0].PowerShell.Streams.Error)
 if($runspaceOutput.Count -or $errors.Count -ne 1){throw "Unexpected trace runspace output=$($runspaceOutput.Count), errors=$($errors.Count)"}
 $authoredError=$errors[0]
 $errorLine=$authoredError.InvocationInfo.Line.Trim()
 if($authoredError.FullyQualifiedErrorId -ne 'PropertyNotFound' -or $errorLine -ne '$traceSynchHash.Error = $Error'){
  throw "Unexpected Trace Quit error: id=$($authoredError.FullyQualifiedErrorId), line=$errorLine"
 }
 if($null -eq $global:traceSynchHash -or $global:traceSynchHash.Count -ne 0){throw 'Authored Quit did not clear the synchronized hash'}
 [ordered]@{
  hostVersion=$PSVersionTable.PSVersion.ToString()
  apartment=[Threading.Thread]::CurrentThread.ApartmentState.ToString()
  wpfAssembly=[Windows.Window].Assembly.FullName
  cleanupCaptured=$owned.Count
  cleanupCompleted=$owned[0].Handle.IsCompleted
  runspaceOutput=$runspaceOutput.Count
  runspaceErrors=$errors.Count
  authoredErrorId=$authoredError.FullyQualifiedErrorId
  authoredErrorLine=$errorLine
  hashCount=$global:traceSynchHash.Count
 }|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $OutputPath -Encoding UTF8
}finally{
 $owned=@(Get-CapturedCleanup)
 foreach($entry in $owned){
  if($null -ne $entry.PowerShell){$entry.PowerShell.Dispose()}
  if($null -ne $entry.Runspace){$entry.Runspace.Dispose()}
 }
 Remove-Module $module -Force
 Remove-Item -LiteralPath Function:\Get-Date,Function:\Get-CimInstance,Function:\New-RunspaceCleanupJob -ErrorAction SilentlyContinue
 Remove-Variable -Name TraceEnabled,traceSynchHash -Scope Global -ErrorAction SilentlyContinue
}
