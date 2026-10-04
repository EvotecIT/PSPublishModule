param(
 [Parameter(Mandatory)][string]$HostPath,
 [Parameter(Mandatory)][string]$ModulePath,
 [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if(-not ('WpfOwnedDynamicCapture' -as [type])){Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WpfOwnedDynamicCapture {
 [DllImport("user32.dll")]
 public static extern bool PrintWindow(IntPtr window, IntPtr target, uint flags);
}
'@}
$hostFile=(Get-Item -LiteralPath $HostPath -ErrorAction Stop).FullName
if((Split-Path -Leaf $hostFile) -notin @('pwsh.exe','powershell.exe')){throw 'Only an explicit PowerShell host executable is accepted'}
$moduleFile=(Get-Item -LiteralPath $ModulePath -ErrorAction Stop).FullName
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$output=(New-Item -ItemType Directory -Path $OutputDirectory -ErrorAction Stop).FullName
$driver=(Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Observe.ps1')).FullName
$resultPath=Join-Path $output 'result.json'
$stdoutPath=Join-Path $output 'stdout.log'
$stderrPath=Join-Path $output 'stderr.log'
$screenshotPath=Join-Path $output 'form.png'
$controlsPath=Join-Path $output 'controls.json'
$args=@('-NoLogo','-NoProfile','-STA','-File',('"'+$driver+'"'),'-ModulePath',('"'+$moduleFile+'"'),'-OutputPath',('"'+$resultPath+'"'))
$process=Start-Process -FilePath $hostFile -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
$processStart=$process.StartTime
try{
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 $window=$null
 while([DateTime]::UtcNow -lt $deadline){
  if($process.HasExited){throw "Owned form process exited before window opened: $(Get-Content -LiteralPath $stderrPath -Raw)"}
  $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
  $candidate=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,$condition)
  if($null -ne $candidate -and $candidate.Current.Name -eq 'New Dynamic Parameter'){$window=$candidate;break}
  Start-Sleep -Milliseconds 100
 }
 if($null -eq $window){throw 'Owned dynamic form did not appear within 30 seconds'}
 $children=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 $controls=[Collections.Generic.List[object]]::new()
 $nameField=$null;$conditionField=$null;$create=$null;$close=$null
 for($i=0;$i -lt $children.Count;$i++){
  $child=$children.Item($i)
  $kind=$child.Current.ControlType.ProgrammaticName
  $name=$child.Current.Name
  $id=$child.Current.AutomationId
  $controls.Add([ordered]@{type=$kind;name=$name;id=$id})
  if($kind -eq 'ControlType.Edit' -and $id -eq 'ParameterName'){$nameField=$child}
  if($kind -eq 'ControlType.Edit' -and $id -eq 'Condition'){$conditionField=$child}
  if($kind -eq 'ControlType.Button' -and $name -in @('Create','_Create')){$create=$child}
  if($kind -eq 'ControlType.Button' -and $name -eq 'Close'){$close=$child}
 }
 $controls.ToArray()|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $controlsPath -Encoding UTF8
 if($null -eq $nameField -or $null -eq $conditionField -or $null -eq $create -or $null -eq $close){throw 'Owned form controls did not match the expected Create/Close workflow'}
 $nameValue=[Windows.Automation.ValuePattern]$nameField.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
 $conditionValue=[Windows.Automation.ValuePattern]$conditionField.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
 $nameValue.SetValue('CompilerProbe')
 $conditionValue.SetValue('$True')
 if($nameValue.Current.Value -ne 'CompilerProbe' -or $conditionValue.Current.Value -ne '$True'){throw 'Owned form values did not reach the edit controls'}
 $bounds=$window.Current.BoundingRectangle
 if($bounds.Width -lt 400 -or $bounds.Height -lt 300 -or $bounds.Width -gt 1500 -or $bounds.Height -gt 1200){throw 'Owned form bounds outside capture size'}
 $rendered=$false
 $renderDeadline=[DateTime]::UtcNow.AddSeconds(10)
 while(-not $rendered -and [DateTime]::UtcNow -lt $renderDeadline){
  $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  try{
   $target=$graphics.GetHdc()
   try{$captured=[WpfOwnedDynamicCapture]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle,$target,[uint32]0)}finally{$graphics.ReleaseHdc($target)}
   if(-not $captured){throw 'PrintWindow did not capture owned form'}
   $darkCount=0
   for($y=50;$y -lt $bitmap.Height-30;$y+=8){for($x=20;$x -lt $bitmap.Width-20;$x+=8){$p=$bitmap.GetPixel($x,$y);if($p.R -lt 120 -and $p.G -lt 120 -and $p.B -lt 120){$darkCount++}}}
   if($darkCount -ge 12){$bitmap.Save($screenshotPath,[Drawing.Imaging.ImageFormat]::Png);$rendered=$true}
  }finally{$graphics.Dispose();$bitmap.Dispose()}
  if(-not $rendered){Start-Sleep -Milliseconds 250}
 }
 if(-not $rendered){throw 'Owned form controls appeared but rendered pixels were not captured'}
 ([Windows.Automation.InvokePattern]$create.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
 if($process.HasExited){throw 'Create unexpectedly closed the owned form'}
 ([Windows.Automation.InvokePattern]$close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
 if(-not $process.WaitForExit(15000)){throw 'Owned form did not close after Close callback'}
 if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)){throw "Owned form probe failed: $(Get-Content -LiteralPath $stderrPath -Raw)"}
 $result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
 [pscustomobject]@{
  hostVersion=$result.hostVersion
  apartment=$result.apartment
  wpfAssembly=$result.wpfAssembly
  capturedCount=$result.capturedCount
  codeLength=$result.codeLength
  codeSha256=$result.codeSha256
  resultCount=$result.resultCount
  screenshotSha256=(Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
  controlsSha256=(Get-FileHash -LiteralPath $controlsPath -Algorithm SHA256).Hash.ToLowerInvariant()
  resultSha256=(Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant()
  exitCode=$process.ExitCode
 }
}finally{
 $current=Get-Process -Id $process.Id -ErrorAction SilentlyContinue
 if($null -ne $current -and $current.StartTime -eq $processStart){Stop-Process -Id $process.Id -Force -ErrorAction Stop;$process.WaitForExit(5000)|Out-Null}
}
