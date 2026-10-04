param(
 [Parameter(Mandatory)][string]$HostPath,
 [Parameter(Mandatory)][string]$ModulePath,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]{1,25}$')][string]$Title
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if(-not ('WpfOwnedGridCapture' -as [type])){Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WpfOwnedGridCapture {
 [DllImport("user32.dll")]
 public static extern bool PrintWindow(IntPtr window, IntPtr target, uint flags);
}
'@}
$hostFile=(Get-Item -LiteralPath $HostPath -ErrorAction Stop).FullName
if((Split-Path -Leaf $hostFile) -ne 'pwsh.exe'){throw 'This pinned grid source needs the installed PowerShell 7 ThreadJob command.'}
$moduleFile=(Get-Item -LiteralPath $ModulePath -ErrorAction Stop).FullName
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$output=(New-Item -ItemType Directory -Path $OutputDirectory -ErrorAction Stop).FullName
$driver=(Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Observe.ps1')).FullName
$resultPath=Join-Path $output 'result.json'
$stdoutPath=Join-Path $output 'stdout.log'
$stderrPath=Join-Path $output 'stderr.log'
$screenshotPath=Join-Path $output 'grid.png'
$controlsPath=Join-Path $output 'controls.json'
$gridControlsPath=Join-Path $output 'grid-controls.json'
$args=@('-NoLogo','-NoProfile','-STA','-File',('"'+$driver+'"'),'-ModulePath',('"'+$moduleFile+'"'),'-OutputPath',('"'+$resultPath+'"'),'-Title',$Title)
$process=Start-Process -FilePath $hostFile -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
$processStart=$process.StartTime
try{
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 $window=$null
 while([DateTime]::UtcNow -lt $deadline){
  if($process.HasExited){throw "Owned grid process exited before window opened: $(Get-Content -LiteralPath $stderrPath -Raw)"}
  $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
  $candidate=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,$condition)
  if($null -ne $candidate -and $candidate.Current.Name -eq $Title){$window=$candidate;break}
  Start-Sleep -Milliseconds 100
 }
 if($null -eq $window){throw 'Owned grid did not appear within 30 seconds'}
 $children=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 $controls=[Collections.Generic.List[object]]::new()
 $close=$null
 $grid=$null
 for($i=0;$i -lt $children.Count;$i++){
  $child=$children.Item($i)
  $kind=$child.Current.ControlType.ProgrammaticName
  $name=$child.Current.Name
  $controls.Add([ordered]@{type=$kind;name=$name})
  if($kind -eq 'ControlType.Button' -and $name -eq 'Close'){$close=$child}
  if($kind -eq 'ControlType.DataGrid'){$grid=$child}
 }
 $controls.ToArray() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $controlsPath -Encoding UTF8
 if($null -eq $close -or $null -eq $grid){throw 'Expected owned Close button and DataGrid controls'}
 $gridControls=[Collections.Generic.List[object]]::new()
 $gridControls.Add([ordered]@{type='ControlType.DataGrid';name=$grid.Current.Name})
 $gridChildren=$grid.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 for($i=0;$i -lt $gridChildren.Count;$i++){
  $child=$gridChildren.Item($i)
  $gridControls.Add([ordered]@{type=$child.Current.ControlType.ProgrammaticName;name=$child.Current.Name})
 }
 if(@($gridControls | Where-Object {$_.type -eq 'ControlType.DataItem'}).Count -ne 2){throw 'Expected two owned grid rows'}
 foreach($expected in @('Name','Count','Alpha','2','Beta','5')){
  if(-not @($gridControls | Where-Object {$_.name -eq $expected}).Count){throw "Expected grid content $expected is absent"}
 }
 [IO.File]::WriteAllText($gridControlsPath,($gridControls.ToArray()|ConvertTo-Json -Compress -Depth 5),[Text.UTF8Encoding]::new($false))
 $bounds=$window.Current.BoundingRectangle
 if($bounds.Width -lt 100 -or $bounds.Height -lt 100 -or $bounds.Width -gt 2000 -or $bounds.Height -gt 2000){throw 'Owned grid bounds outside capture size'}
 $rendered=$false
 $captureFlag=$null
 $renderDeadline=[DateTime]::UtcNow.AddSeconds(12)
 while(-not $rendered -and [DateTime]::UtcNow -lt $renderDeadline){
  foreach($flag in @([uint32]0,[uint32]2)){
  $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
  $graphics=[Drawing.Graphics]::FromImage($bitmap)
  try{
   $target=$graphics.GetHdc()
   try{$captured=[WpfOwnedGridCapture]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle,$target,$flag)}finally{$graphics.ReleaseHdc($target)}
   if(-not $captured){throw 'PrintWindow did not capture owned grid'}
   $darkCount=0
   for($y=65;$y -lt $bitmap.Height-20;$y+=4){
    for($x=15;$x -lt $bitmap.Width-15;$x+=4){
     $pixel=$bitmap.GetPixel($x,$y)
     if($pixel.R -lt 120 -and $pixel.G -lt 120 -and $pixel.B -lt 120){$darkCount++}
    }
   }
   if($darkCount -ge 8){$bitmap.Save($screenshotPath,[Drawing.Imaging.ImageFormat]::Png);$rendered=$true;$captureFlag=$flag}
  }finally{$graphics.Dispose();$bitmap.Dispose()}
   if($rendered){break}
  }
  if(-not $rendered){Start-Sleep -Milliseconds 250}
 }
 if(-not $rendered){throw 'Owned grid controls appeared but rendered grid pixels were not captured'}
 ([Windows.Automation.InvokePattern]$close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
 if(-not $process.WaitForExit(85000)){throw 'Owned grid did not close after Close callback'}
 if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)){throw "Owned grid probe failed: $(Get-Content -LiteralPath $stderrPath -Raw)"}
 $result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
 [pscustomobject]@{
  hostVersion=$result.hostVersion
  apartment=$result.apartment
  wpfAssembly=$result.wpfAssembly
  jobState=$result.jobState
  resultCount=$result.resultCount
  screenshotSha256=(Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
  renderedDarkSamples=$darkCount
  captureFlag=$captureFlag
  controlsSha256=(Get-FileHash -LiteralPath $controlsPath -Algorithm SHA256).Hash.ToLowerInvariant()
  gridControlsSha256=(Get-FileHash -LiteralPath $gridControlsPath -Algorithm SHA256).Hash.ToLowerInvariant()
  gridControlsCount=$gridControls.Count
  resultSha256=(Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant()
  bounds=$bounds.ToString()
 }
}finally{
 $current=Get-Process -Id $process.Id -ErrorAction SilentlyContinue
 if($null -ne $current -and $current.StartTime -eq $processStart){Stop-Process -Id $process.Id -Force -ErrorAction Stop;$process.WaitForExit(5000)|Out-Null}
}
