param(
    [Parameter(Mandatory = $true)] [scriptblock] $Block,
    [object[]] $Arguments = @()
)

$previousGlobalLastExitCodeVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$previousGlobalLastExitCode = if ($null -eq $previousGlobalLastExitCodeVariable) { $null } else { $previousGlobalLastExitCodeVariable.Value }
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
    if ($null -eq $previousGlobalLastExitCodeVariable) {
        Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    } else {
        $global:LASTEXITCODE = $previousGlobalLastExitCode
    }
}
