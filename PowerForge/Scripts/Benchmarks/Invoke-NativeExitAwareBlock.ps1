param(
    [Parameter(Mandatory = $true)] [scriptblock] $Block,
    [object[]] $Arguments = @(),
    [bool] $StrictMode = $false,
    [object] $NativeExitCodeTrackerType,
    [System.Diagnostics.Stopwatch] $OperationStopwatch,
    [bool] $CapturedOperationBody = $false
)

$previousGlobalLastExitCodeVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$previousGlobalLastExitCode = if ($null -eq $previousGlobalLastExitCodeVariable) { $null } else { $previousGlobalLastExitCodeVariable.Value }
$global:LASTEXITCODE = 0
if ($null -eq $NativeExitCodeTrackerType) {
    $NativeExitCodeTrackerType = [PowerForge.PowerShellNativeExitCodeTracker]
}
$installNativeExitTracker = $NativeExitCodeTrackerType.GetMethod(
    'Install',
    [type[]] @([System.Management.Automation.SessionState])
)
if ($null -eq $installNativeExitTracker) {
    throw "PowerShell native exit-code tracker type '$NativeExitCodeTrackerType' does not expose Install(SessionState)."
}
$nativeExitTracker = $installNativeExitTracker.Invoke($null, @($ExecutionContext.SessionState))
$benchmarkDslCommandAliasNames = [System.Collections.Generic.List[string]]::new()
try {
    $dslCommandAliasesVariable = Get-Variable -Name PowerForgeBenchmarkDslCommandAliases -ErrorAction Ignore
    $dslCommandAliases = if ($null -eq $dslCommandAliasesVariable) { $null } else { $dslCommandAliasesVariable.Value }
    if ($null -ne $dslCommandAliases) {
        foreach ($entry in @($dslCommandAliases.GetEnumerator())) {
            Set-Alias -Name $entry.Key -Value $entry.Value -Scope Local -Force
            $benchmarkDslCommandAliasNames.Add([string] $entry.Key)
        }
    }
    if ($StrictMode) {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
    }
    if ($CapturedOperationBody -and $null -ne $OperationStopwatch) {
        & $Block @Arguments
    } else {
        if ($null -ne $OperationStopwatch) { $OperationStopwatch.Start() }
        try {
            & $Block @Arguments
        }
        finally {
            if ($null -ne $OperationStopwatch) { $OperationStopwatch.Stop() }
        }
    }
    $nativeExitCode = $nativeExitTracker.FirstFailureExitCode
    if ($null -eq $nativeExitCode) {
        $nativeExitCode = $global:LASTEXITCODE
    }
    if ($null -ne $nativeExitCode -and $nativeExitCode -ne 0) {
        throw "Native command exited with code $nativeExitCode."
    }
}
finally {
    foreach ($aliasName in $benchmarkDslCommandAliasNames) {
        Remove-Item -LiteralPath "Alias:$aliasName" -Force -ErrorAction SilentlyContinue
    }
    $nativeExitTracker.Dispose()
    if ($null -eq $previousGlobalLastExitCodeVariable) {
        Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    } else {
        $global:LASTEXITCODE = $previousGlobalLastExitCode
    }
}
