[CmdletBinding()]
param([switch] $InvocationOnly)

# Runs in a disposable PowerShell process. Cold means first use in that process;
# warm means repeated use with assemblies, JIT code and module state already loaded.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$path = $env:POWERFORGE_TIMING_MODULE
$probe = [scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:POWERFORGE_TIMING_PROBE)))
$rows = [Collections.Generic.List[object]]::new()
for ($iteration = 0; $iteration -le 5; $iteration++) {
    $clock = [Diagnostics.Stopwatch]::new()
    $importMilliseconds = $null
    if (-not $InvocationOnly -or $iteration -eq 0) {
        if ($iteration -gt 0) { Remove-Module -ModuleInfo $module -Force -ErrorAction Stop }
        $clock.Start()
        $module = Import-Module -Name $path -Force -PassThru -ErrorAction Stop
        $clock.Stop()
        $importMilliseconds = $clock.Elapsed.TotalMilliseconds
    }
    $clock.Restart()
    $output = @(& $probe)
    $clock.Stop()
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
[ordered]@{
    powerShell = $PSVersionTable.PSVersion.ToString()
    runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    rows = @($rows)
} | ConvertTo-Json -Depth 8 -Compress
