<#
.SYNOPSIS
Compares source and Hybrid imports and packet invocations in clean processes.
.DESCRIPTION
Uses the verified packages and retained artifacts from Invoke-PublicCorpus.
Alternates source/Hybrid order, retaining every first-use and repeated-use sample.
Cold refers to process state, not OS cache eviction. Compilation is excluded.
.PARAMETER WorkspacePath
The workspace used by Invoke-PublicCorpus with KeepRunArtifacts.
.PARAMETER CorpusEvidencePath
The successful report identifying the exact package and artifact hashes.
.PARAMETER EvidencePath
Destination for raw samples, host metadata and nearest-rank summaries.
.PARAMETER InvocationOnly
Keeps each module loaded after its first import and measures repeated calls.
.EXAMPLE
./Measure-PublicModuleTiming.ps1 -WorkspacePath /tmp/corpus -CorpusEvidencePath /tmp/corpus.json -EvidencePath /tmp/timing.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WorkspacePath,
    [Parameter(Mandatory)] [string] $CorpusEvidencePath,
    [Parameter(Mandatory)] [string] $EvidencePath,
    [switch] $InvocationOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.TimingIdentity.ps1')
$packetPath = Join-Path $PSScriptRoot 'public-corpus.net10.json'
$packet = Get-Content -LiteralPath $packetPath -Raw | ConvertFrom-Json
$corpus = Get-Content -LiteralPath $CorpusEvidencePath -Raw | ConvertFrom-Json
if ($corpus.packetSha256 -cne (Get-FileHash $packetPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Corpus report belongs to a different packet.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$process = [Diagnostics.Process]::GetCurrentProcess()
$report = [ordered]@{
    schemaVersion = 1
    completed = $false
    mode = if ($InvocationOnly) { 'LoadedModuleInvocation' } else { 'ImportAndReimport' }
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    harnessCommit = (git -C $repository rev-parse HEAD).Trim()
    compilerSourceCommit = Get-CorpusProperty $corpus 'sourceCommit' 'NotRecorded'
    harnessSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    childSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Measure-ModuleChild.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
    packetSha256 = $corpus.packetSha256
    corpusEvidenceSha256 = (Get-FileHash $CorpusEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    host = [ordered]@{
        operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        powerShell = $PSVersionTable.PSVersion.ToString()
        runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        runtimeLocation = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
        logicalProcessors = [Environment]::ProcessorCount
        priority = $process.PriorityClass.ToString()
        affinity = $process.ProcessorAffinity.ToString('X')
        cpu = if ($IsWindows) { @((Get-CimInstance Win32_Processor).Name | ForEach-Object { $_.Trim() }) } else { $null }
        powerPlan = if ($IsWindows) { (powercfg /getactivescheme | Out-String).Trim() } else { $null }
    }
    policy = [ordered]@{
        pairs = 8
        order = 'AlternatingPairs'
        cold = if ($InvocationOnly) { 'First invocation after one import in a fresh process.' } else { 'First import and first packet invocation in a fresh process; OS caches are not evicted.' }
        warm = if ($InvocationOnly) { 'Five repeated invocations in the same loaded module; no reimport.' } else { 'Five remove/import and invocation repetitions in the same process; assemblies remain loaded.' }
        percentile = 'Nearest rank'
        invocation = 'Exact public packet probe, including assertions and any declared file I/O.'
    }
    modules = [Collections.Generic.List[object]]::new()
}
foreach ($entry in $packet.modules) {
    Write-Host "[$($entry.id)] eight source/Hybrid timing pairs"
    $built = @($corpus.modules | Where-Object id -eq $entry.id)
    if ($built.Count -ne 1 -or -not $built[0].succeeded) { throw "Missing successful corpus build: $($entry.id)" }
    $source = Join-Path $WorkspacePath ('extract/' + $entry.sha256 + '/' + $entry.entryPoint)
    $artifacts = @(Get-ChildItem -LiteralPath (Join-Path $WorkspacePath 'runs') -Recurse -File -Filter ($entry.id + '.psd1') |
        Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -eq $built[0].artifactSha256 })
    if ($artifacts.Count -ne 1) { throw "Cannot identify unique measured artifact: $($entry.id)" }
    $artifactRoot = $artifacts[0].Directory.Parent.FullName
    Assert-CorpusTimingIdentity -Entry $entry -Build $built[0] -WorkspacePath $WorkspacePath -ArtifactRoot $artifactRoot
    $samples = [Collections.Generic.List[object]]::new()
    $row = [ordered]@{
        id = $entry.id
        version = $entry.version
        packageSha256 = $entry.sha256
        artifactSha256 = $built[0].artifactSha256
        typedAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $artifacts[0].DirectoryName ($entry.id + '.dll')) -Algorithm SHA256).Hash.ToLowerInvariant()
        rootModuleSha256 = (Get-FileHash -LiteralPath (Join-Path $artifacts[0].DirectoryName ($entry.id + '.psm1')) -Algorithm SHA256).Hash.ToLowerInvariant()
        artifactFiles = @($built[0].artifactFiles)
        verifiedSourceFiles = $true
        counts = $built[0].counts
        probeScript = $entry.probeScript
        succeeded = $false
        samples = $samples
        summary = [ordered]@{}
    }
    $report.modules.Add($row)
    try {
        for ($pair = 0; $pair -lt 8; $pair++) {
            $order = if ($pair % 2 -eq 0) { @('source', 'hybrid') } else { @('hybrid', 'source') }
            foreach ($surface in $order) {
                $path = if ($surface -eq 'source') { $source } else { $artifacts[0].FullName }
                $environment = @{
                    PSModulePath = (Join-Path $PSHOME 'Modules')
                    POWERSHELL_TELEMETRY_OPTOUT = '1'
                    POWERFORGE_TIMING_MODULE = $path
                    POWERFORGE_TIMING_PROBE = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($entry.probeScript))
                }
                $childArguments = @(
                    '-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Measure-ModuleChild.ps1')
                )
                if ($InvocationOnly) { $childArguments += '-InvocationOnly' }
                $child = Invoke-OwnedProcess -FileName (Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })) -Arguments $childArguments -WorkingDirectory $WorkspacePath -Environment $environment -TimeoutSeconds 180
                if ($child.ExitCode -ne 0) { throw "Timing probe failed: $($child.StandardError) $($child.StandardOutput)" }
                $jsonLine = @($child.StandardOutput -split '\r?\n' | Where-Object { $_.StartsWith('{', [StringComparison]::Ordinal) })[-1]
                $observed = $jsonLine | ConvertFrom-Json
                $samples.Add([ordered]@{
                    pair = $pair
                    order = $order -join ','
                    surface = $surface
                    processMilliseconds = $child.DurationMilliseconds
                    powerShell = $observed.powerShell
                    rows = @($observed.rows)
                })
                Write-Utf8Json -Path $EvidencePath -Value $report
            }
        }
        Assert-CorpusTimingIdentity -Entry $entry -Build $built[0] -WorkspacePath $WorkspacePath -ArtifactRoot $artifactRoot
        foreach ($surface in @('source', 'hybrid')) {
            $states = if ($InvocationOnly) { @('cold-module', 'warm-module') } else { @('cold-process', 'warm-process') }
            foreach ($state in $states) {
                $values = @($samples | Where-Object surface -eq $surface | ForEach-Object rows | Where-Object state -eq $state)
                $summary = [ordered]@{ samples = $values.Count }
                foreach ($metric in @('importMilliseconds', 'invocationMilliseconds')) {
                    if ($state -eq 'warm-module' -and $metric -eq 'importMilliseconds') { continue }
                    $sorted = @($values.$metric | Sort-Object)
                    $summary[$metric] = [ordered]@{
                        p50 = $sorted[[int][Math]::Ceiling($sorted.Count * 0.5) - 1]
                        p95 = $sorted[[int][Math]::Ceiling($sorted.Count * 0.95) - 1]
                    }
                }
                $row.summary["$surface/$state"] = $summary
            }
        }
        $row.succeeded = $true
    } catch {
        $row.error = $_.Exception.Message
        throw
    } finally {
        Write-Utf8Json -Path $EvidencePath -Value $report
    }
}
$report.completed = $true
Write-Utf8Json -Path $EvidencePath -Value $report
