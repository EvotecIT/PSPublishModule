param(
    [Parameter(Mandatory = $true)] [scriptblock] $Block,
    [object[]] $Arguments = @()
)

$previousGlobalLastExitCode = $global:LASTEXITCODE
$global:LASTEXITCODE = 0
$nativeExitTracker = [PowerForge.PowerShellNativeExitCodeTracker]::Install($ExecutionContext.SessionState)
try {
    & $Block @Arguments
    $nativeExitCode = $nativeExitTracker.FirstFailureExitCode
    if ($null -eq $nativeExitCode) {
        $nativeExitCode = $global:LASTEXITCODE
    }
    if ($null -ne $nativeExitCode -and $nativeExitCode -ne 0) {
        throw "Native command exited with code $nativeExitCode."
    }
}
finally {
    $nativeExitTracker.Dispose()
    $global:LASTEXITCODE = $previousGlobalLastExitCode
}
