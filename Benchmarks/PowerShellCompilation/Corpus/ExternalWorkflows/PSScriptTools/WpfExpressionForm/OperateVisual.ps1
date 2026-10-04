param(
 [Parameter(Mandatory)][string]$HostPath,
 [Parameter(Mandatory)][string]$ModulePath,
 [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if(-not ('WpfOwnedExpressionCapture' -as [type])){Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WpfOwnedExpressionCapture {
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
$screenshotPath=Join-Path $output 'form-before.png'
$afterPath=Join-Path $output 'form-after.png'
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
  if($null -ne $candidate -and $candidate.Current.Name -eq 'Test Expression'){$window=$candidate;break}
  Start-Sleep -Milliseconds 100
 }
 if($null -eq $window){throw 'Owned expression form did not appear within 30 seconds'}
 $children=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 $controls=[Collections.Generic.List[object]]::new()
 $expressionField=$null;$resultsField=$null;$run=$null;$quit=$null
 for($i=0;$i -lt $children.Count;$i++){
  $child=$children.Item($i)
  $kind=$child.Current.ControlType.ProgrammaticName
  $name=$child.Current.Name
  $id=$child.Current.AutomationId
  if($kind -eq 'ControlType.TitleBar' -or $id -in @('SystemMenuBar','Item 1','Minimize','Maximize','Close')){continue}
  $controls.Add([ordered]@{type=$kind;name=$name;id=$id})
  if($kind -eq 'ControlType.Edit' -and $id -eq 'txtScriptBlock'){$expressionField=$child}
  if($kind -eq 'ControlType.Edit' -and $id -eq 'tbResults'){$resultsField=$child}
  if($kind -eq 'ControlType.Button' -and $name -in @('Run','_Run')){$run=$child}
  if($kind -eq 'ControlType.Button' -and $name -in @('Quit','_Quit')){$quit=$child}
 }
 $controls.ToArray()|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $controlsPath -Encoding UTF8
 if($null -eq $expressionField -or $null -eq $resultsField -or $null -eq $run -or $null -eq $quit){throw 'Owned form controls did not match the expected Run/Quit workflow'}
 $expressionValue=[Windows.Automation.ValuePattern]$expressionField.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
 $resultsValue=[Windows.Automation.ValuePattern]$resultsField.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
 $expressionValue.SetValue('40+2')
 if($expressionValue.Current.Value -ne '40+2'){throw 'Owned expression did not reach the edit control'}
 $bounds=$window.Current.BoundingRectangle
 if($bounds.Width -lt 400 -or $bounds.Height -lt 300 -or $bounds.Width -gt 1500 -or $bounds.Height -gt 1200){throw 'Owned form bounds outside capture size'}
 $rendered=$false
 $renderDeadline=[DateTime]::UtcNow.AddSeconds(10)
 while(-not $rendered -and [DateTime]::UtcNow -lt $renderDeadline){
  $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  try{
   $target=$graphics.GetHdc()
   try{$captured=[WpfOwnedExpressionCapture]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle,$target,[uint32]0)}finally{$graphics.ReleaseHdc($target)}
   if(-not $captured){throw 'PrintWindow did not capture owned form'}
   $darkCount=0
   for($y=50;$y -lt $bitmap.Height-30;$y+=8){for($x=20;$x -lt $bitmap.Width-20;$x+=8){$p=$bitmap.GetPixel($x,$y);if($p.R -lt 120 -and $p.G -lt 120 -and $p.B -lt 120){$darkCount++}}}
   if($darkCount -ge 12){$bitmap.Save($screenshotPath,[Drawing.Imaging.ImageFormat]::Png);$rendered=$true}
  }finally{$graphics.Dispose();$bitmap.Dispose()}
  if(-not $rendered){Start-Sleep -Milliseconds 250}
 }
 if(-not $rendered){throw 'Owned form controls appeared but rendered pixels were not captured'}
 ([Windows.Automation.InvokePattern]$run.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
 if($process.HasExited){throw 'Run unexpectedly closed the owned form'}
 $displayDeadline=[DateTime]::UtcNow.AddSeconds(10)
 while($resultsValue.Current.Value -notmatch 'compiler-result' -and [DateTime]::UtcNow -lt $displayDeadline){Start-Sleep -Milliseconds 100}
 $display=$resultsValue.Current.Value
 if($display -notmatch 'compiler-result'){throw 'Run did not display the in-memory provider result'}
 $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
 $graphics=[Drawing.Graphics]::FromImage($bitmap)
 try{
  $target=$graphics.GetHdc()
  try{$captured=[WpfOwnedExpressionCapture]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle,$target,[uint32]0)}finally{$graphics.ReleaseHdc($target)}
  if(-not $captured){throw 'PrintWindow did not capture the post-Run form'}
  $bitmap.Save($afterPath,[Drawing.Imaging.ImageFormat]::Png)
 }finally{$graphics.Dispose();$bitmap.Dispose()}
 ([Windows.Automation.InvokePattern]$quit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
 if(-not $process.WaitForExit(15000)){throw 'Owned form did not close after Quit callback'}
 if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)){throw "Owned form probe failed: $(Get-Content -LiteralPath $stderrPath -Raw)"}
 $result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
 [pscustomobject]@{
  hostVersion=$result.hostVersion
  apartment=$result.apartment
  wpfAssembly=$result.wpfAssembly
  capturedCount=$result.capturedCount
  displayedResult=$display
  resultCount=$result.resultCount
  screenshotSha256=(Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
  afterScreenshotSha256=(Get-FileHash -LiteralPath $afterPath -Algorithm SHA256).Hash.ToLowerInvariant()
  controlsSha256=(Get-FileHash -LiteralPath $controlsPath -Algorithm SHA256).Hash.ToLowerInvariant()
  resultSha256=(Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant()
  exitCode=$process.ExitCode
 }
}finally{
 $current=Get-Process -Id $process.Id -ErrorAction SilentlyContinue
 if($null -ne $current -and $current.StartTime -eq $processStart){Stop-Process -Id $process.Id -Force -ErrorAction Stop;$process.WaitForExit(5000)|Out-Null}
}
