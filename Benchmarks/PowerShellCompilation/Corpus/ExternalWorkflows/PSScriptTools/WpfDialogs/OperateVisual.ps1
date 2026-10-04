param(
    [Parameter(Mandatory)][string] $HostPath,
    [Parameter(Mandatory)][string] $ModulePath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][ValidateSet('message', 'input')][string] $Case,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]{1,25}$')][string] $Title
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if (-not ('WpfOwnedWindowCapture' -as [type])) { Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WpfOwnedWindowCapture {
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr target, uint flags);
}
'@
}

$hostFile = (Get-Item -LiteralPath $HostPath -ErrorAction Stop).FullName
if ((Split-Path -Leaf $hostFile) -notin @('pwsh.exe', 'powershell.exe')) {
    throw 'Only an explicit PowerShell host executable is accepted.'
}
$moduleFile = (Get-Item -LiteralPath $ModulePath -ErrorAction Stop).FullName
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new task-owned output directory.' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory -ErrorAction Stop).FullName
$driver = (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Observe.ps1')).FullName
$resultPath = Join-Path $output 'result.json'
$stdoutPath = Join-Path $output 'stdout.log'
$stderrPath = Join-Path $output 'stderr.log'
$screenshotPath = Join-Path $output 'dialog.png'
$args = @(
    '-NoLogo', '-NoProfile', '-STA', '-File', ('"' + $driver + '"'),
    '-ModulePath', ('"' + $moduleFile + '"'),
    '-OutputPath', ('"' + $resultPath + '"'),
    '-Case', $Case, '-Title', $Title
)
$process = Start-Process -FilePath $hostFile -ArgumentList $args -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
$processStart = $process.StartTime
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $window = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "Owned dialog process exited before a window opened: $(Get-Content -LiteralPath $stderrPath -Raw)" }
        $condition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
        $candidate = [Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $candidate -and $candidate.Current.Name -eq $Title) {
            $window = $candidate
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if ($null -eq $window) { throw 'Owned dialog did not appear within 30 seconds.' }

    $children = $window.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.Condition]::TrueCondition)
    $button = $null
    $edit = $null
    $messageObserved = $false
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children.Item($i)
        if ($child.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and
            $child.Current.Name -eq 'OK') { $button = $child }
        if ($child.Current.ControlType -eq [Windows.Automation.ControlType]::Edit) { $edit = $child }
        if ($Case -eq 'message' -and $child.Current.Name -like 'Owned compiler dialog: Unicode*') {
            $messageObserved = $true
        }
        if ($Case -eq 'input' -and $child.Current.Name -like '*Owned offline input: enter compiler-value*') {
            $messageObserved = $true
        }
    }
    if ($null -eq $button -or -not $messageObserved -or ($Case -eq 'input' -and $null -eq $edit)) {
        throw 'Owned dialog controls did not match the expected case.'
    }

    $bounds = $window.Current.BoundingRectangle
    if ($bounds.Width -lt 100 -or $bounds.Height -lt 100 -or $bounds.Width -gt 2000 -or $bounds.Height -gt 2000) {
        throw 'Owned dialog bounds are outside the expected capture size.'
    }
    $bitmap = [Drawing.Bitmap]::new([int] $bounds.Width, [int] $bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $target = $graphics.GetHdc()
        try {
            $captured = [WpfOwnedWindowCapture]::PrintWindow(
                [IntPtr] $window.Current.NativeWindowHandle, $target, [uint32] 0)
        } finally { $graphics.ReleaseHdc($target) }
        if (-not $captured) { throw 'PrintWindow did not capture the owned dialog.' }
        $bitmap.Save($screenshotPath, [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    if ($Case -eq 'input') {
        $value = [Windows.Automation.ValuePattern] $edit.GetCurrentPattern(
            [Windows.Automation.ValuePattern]::Pattern)
        $value.SetValue('compiler-value')
        if ($value.Current.Value -ne 'compiler-value') { throw 'Input value did not reach the owned edit control.' }
    }
    ([Windows.Automation.InvokePattern] $button.GetCurrentPattern(
        [Windows.Automation.InvokePattern]::Pattern)).Invoke()
    if (-not $process.WaitForExit(20000)) { throw 'Owned dialog did not close after its OK callback.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath)) {
        throw "Owned dialog probe failed: $(Get-Content -LiteralPath $stderrPath -Raw)"
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    [pscustomobject]@{
        Case = $Case
        Host = $result.hostVersion
        ResultType = $result.type
        Value = $result.value
        Apartment = $result.apartment
        WpfAssembly = $result.wpfAssembly
        Screenshot = $screenshotPath
        ScreenshotSha256 = (Get-FileHash -LiteralPath $screenshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Bounds = $bounds.ToString()
        ExitCode = $process.ExitCode
    }
} finally {
    $current = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($null -ne $current -and $current.StartTime -eq $processStart) {
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        $process.WaitForExit(5000) | Out-Null
    }
}
