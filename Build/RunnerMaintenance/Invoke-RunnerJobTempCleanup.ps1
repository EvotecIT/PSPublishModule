<#
.SYNOPSIS
Runs private runner temporary cleanup with a 45-second process boundary.
.DESCRIPTION
Installed as both GitHub job hooks. GitHub provides no timeout for hooks, so
the cleanup runs in an owned child process. A timeout stops only that child;
remaining files are retained. Cleanup warnings do not change the job result.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$process = $null
try {
    $shell = Join-Path $PSHOME 'pwsh.exe'
    if (-not (Test-Path -LiteralPath $shell)) { $shell = Join-Path $PSHOME 'powershell.exe' }
    $worker = Join-Path $PSScriptRoot 'Clear-RunnerJobTemp.ps1'
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $shell
    $start.Arguments = '-NoLogo -NoProfile -NonInteractive -File "' + $worker + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(45000)) {
        $process.Kill()
        $process.WaitForExit()
        Write-Warning 'Private runner temporary cleanup timed out; only its owned child was stopped.'
    }
    Write-Output $stdout.GetAwaiter().GetResult()
    $errorText = $stderr.GetAwaiter().GetResult()
    if ($errorText) { Write-Warning $errorText }
    if ($process.ExitCode) { Write-Warning "Private runner temporary cleanup exited $($process.ExitCode)." }
} catch {
    Write-Warning "Private runner temporary cleanup could not run: $($_.Exception.Message)"
} finally {
    if ($process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
