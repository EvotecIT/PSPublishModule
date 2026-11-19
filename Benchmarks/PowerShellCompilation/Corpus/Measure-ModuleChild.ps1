[CmdletBinding()]
param([switch] $InvocationOnly, [switch] $TracePhases)

# Runs in a disposable PowerShell process. Cold means first use in that process;
# warm means repeated use with assemblies, JIT code and module state already loaded.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$path = $env:POWERFORGE_TIMING_MODULE
$probe = [scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:POWERFORGE_TIMING_PROBE)))
$phaseTrace = if ($TracePhases) { [Diagnostics.Tracing.EventSource]::new('PowerForge-ModuleTiming') }
if ($phaseTrace) {
    $traceOptions = [Diagnostics.Tracing.EventSourceOptions]::new()
    $traceOptions.Level = [Diagnostics.Tracing.EventLevel]::Informational
    $phaseTrace.Write('Ready', $traceOptions)
}
$rows = [Collections.Generic.List[object]]::new()
for ($iteration = 0; $iteration -le 5; $iteration++) {
    $clock = [Diagnostics.Stopwatch]::new()
    $importMilliseconds = $null
    if (-not $InvocationOnly -or $iteration -eq 0) {
        if ($iteration -gt 0) { Remove-Module -ModuleInfo $module -Force -ErrorAction Stop }
        if ($phaseTrace) { $phaseTrace.Write('ImportStart', $traceOptions) }
        $clock.Start()
        $module = Import-Module -Name $path -Force -PassThru -ErrorAction Stop
        $clock.Stop()
        if ($phaseTrace) { $phaseTrace.Write('ImportStop', $traceOptions) }
        $importMilliseconds = $clock.Elapsed.TotalMilliseconds
    }
    if ($phaseTrace) {
        $phaseName = if ($iteration -eq 0) { 'First' } else { 'Warm' }
        $phaseTrace.Write($phaseName + 'Start', $traceOptions)
    }
    $clock.Restart()
    $output = @(& $probe)
    $clock.Stop()
    if ($phaseTrace) { $phaseTrace.Write($phaseName + 'Stop', $traceOptions) }
    if ('ok' -notin $output) { throw 'Invocation did not return the packet success marker.' }
    $rows.Add([ordered]@{
        state = if ($InvocationOnly) {
            if ($iteration -eq 0) { 'cold-module' } else { 'warm-module' }
        } elseif ($iteration -eq 0) { 'cold-process' } else { 'warm-process' }
        iteration = $iteration
        importMilliseconds = $importMilliseconds
        invocationMilliseconds = $clock.Elapsed.TotalMilliseconds
        exportedCommands = $module.ExportedCommands.Count
        invocationSucceeded = $true
    })
}
if ($phaseTrace) { $phaseTrace.Dispose() }
[ordered]@{
    powerShell = $PSVersionTable.PSVersion.ToString()
    runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    rows = @($rows)
} | ConvertTo-Json -Depth 8 -Compress
