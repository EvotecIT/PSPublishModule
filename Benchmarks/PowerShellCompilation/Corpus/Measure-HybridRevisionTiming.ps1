#requires -Version 7.4
<#
.SYNOPSIS
Compares two verified Hybrid revisions with alternating fresh processes.
.DESCRIPTION
Uses the public packet's exact probes and the shared loaded-module timing child.
Builds, payload verification and process startup remain outside the child clocks.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaselineWorkspacePath,
    [Parameter(Mandatory)] [string] $BaselineEvidencePath,
    [Parameter(Mandatory)] [string] $CandidateWorkspacePath,
    [Parameter(Mandatory)] [string] $CandidateEvidencePath,
    [Parameter(Mandatory)] [string[]] $ModuleId,
    [Parameter(Mandatory)] [string] $EvidencePath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.TimingIdentity.ps1')
$packetPath = Join-Path $PSScriptRoot 'public-corpus.net10.json'
$packet = Get-Content $packetPath -Raw | ConvertFrom-Json
$packetHash = (Get-FileHash $packetPath -Algorithm SHA256).Hash.ToLowerInvariant()
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$hostExecutable = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$runtimeRoot = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$coreClr = Join-Path $runtimeRoot $(if ($IsWindows) { 'coreclr.dll' } else { 'libcoreclr.so' })
$inputs = [ordered]@{
    baseline = @{ workspace = $BaselineWorkspacePath; evidence = $BaselineEvidencePath }
    candidate = @{ workspace = $CandidateWorkspacePath; evidence = $CandidateEvidencePath }
}
$identities = [ordered]@{}
foreach ($variant in $inputs.Keys) {
    $inputRecord = $inputs[$variant]
    $inputRecord.report = Get-Content $inputRecord.evidence -Raw | ConvertFrom-Json
    if ($inputRecord.report.packetSha256 -cne $packetHash) { throw 'Revision report belongs to a different packet.' }
    $identities[$variant] = [ordered]@{
        compilerSourceCommit = $inputRecord.report.sourceCommit
        corpusEvidenceSha256 = (Get-FileHash $inputRecord.evidence -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$report = [ordered]@{
    schemaVersion = 1; completed = $false
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    harnessCommit = (git -C $repository rev-parse HEAD).Trim()
    harnessSha256 = (Get-FileHash $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    childSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'Measure-ModuleChild.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
    packetSha256 = $packetHash; revisions = $identities
    host = [ordered]@{
        operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        powerShell = $PSVersionTable.PSVersion.ToString()
        runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        runtimeLocation = '<PowerShellHostRoot>'
        runtimeLayout = 'Installation prefix omitted; hashes identify the actual host and loaded runtime bytes.'
        runtimeFileSystem = if ($IsWindows) { [IO.DriveInfo]::new([IO.Path]::GetPathRoot($runtimeRoot)).DriveFormat } else { $null }
        executableSha256 = (Get-FileHash $hostExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        coreClrSha256 = (Get-FileHash $coreClr -Algorithm SHA256).Hash.ToLowerInvariant()
        smaSha256 = (Get-FileHash (Join-Path $PSHOME 'System.Management.Automation.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        logicalProcessors = [Environment]::ProcessorCount
        priority = [Diagnostics.Process]::GetCurrentProcess().PriorityClass.ToString()
        affinity = [Diagnostics.Process]::GetCurrentProcess().ProcessorAffinity.ToString('X')
        cpu = if ($IsWindows) { @((Get-CimInstance Win32_Processor).Name | ForEach-Object { $_.Trim() }) } else { $null }
        powerPlan = if ($IsWindows) { (powercfg /getactivescheme | Out-String).Trim() } else { $null }
    }
    policy = [ordered]@{
        pairs = 8; order = 'AlternatingPairs'; percentile = 'Nearest rank'
        cold = 'One import and first packet invocation in a fresh process; OS caches are not evicted.'
        warm = 'Five repeated packet invocations in the same loaded module.'
    }
    modules = [Collections.Generic.List[object]]::new()
}
try {
    foreach ($id in ($ModuleId | Select-Object -Unique)) {
        $entries = @($packet.modules | Where-Object id -eq $id)
        if ($entries.Count -ne 1) { throw "Unknown packet module: $id" }
        $entry = $entries[0]
        $resolved = @{}
        foreach ($variant in $inputs.Keys) {
            $inputRecord = $inputs[$variant]
            $builds = @($inputRecord.report.modules | Where-Object id -eq $id)
            if ($builds.Count -ne 1 -or -not $builds[0].succeeded) { throw "Missing successful build: $variant/$id" }
            $build = $builds[0]
            $manifests = @(Get-ChildItem (Join-Path $inputRecord.workspace 'runs') -Recurse -File -Filter ($id + '.psd1') |
                Where-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -eq $build.artifactSha256 })
            if ($manifests.Count -ne 1) { throw "Cannot identify unique artifact: $variant/$id" }
            $root = $manifests[0].Directory.Parent.FullName
            Assert-CorpusTimingIdentity -Entry $entry -Build $build -WorkspacePath $inputRecord.workspace -ArtifactRoot $root
            $resolved[$variant] = @{ manifest = $manifests[0].FullName; root = $root; build = $build }
        }
        Write-Host "[$id] eight alternating Hybrid revision pairs"
        $row = [ordered]@{
            id = $id; version = $entry.version; packageSha256 = $entry.sha256
            artifactSha256 = @{ baseline = $resolved.baseline.build.artifactSha256; candidate = $resolved.candidate.build.artifactSha256 }
            probeScript = $entry.probeScript; succeeded = $false
            samples = [Collections.Generic.List[object]]::new(); summary = [ordered]@{}
        }
        $report.modules.Add($row)
        for ($pair = 0; $pair -lt 8; $pair++) {
            $order = if ($pair % 2 -eq 0) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
            foreach ($variant in $order) {
                $child = Invoke-OwnedProcess -FileName $hostExecutable -Arguments @(
                    '-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Measure-ModuleChild.ps1'), '-InvocationOnly'
                ) -WorkingDirectory $CandidateWorkspacePath -Environment @{
                    PSModulePath = (Join-Path $PSHOME 'Modules'); POWERSHELL_TELEMETRY_OPTOUT = '1'
                    POWERFORGE_TIMING_MODULE = $resolved[$variant].manifest
                    POWERFORGE_TIMING_PROBE = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($entry.probeScript))
                } -TimeoutSeconds 180
                if ($child.ExitCode -ne 0) { throw "Revision probe failed: $($child.StandardError) $($child.StandardOutput)" }
                $line = @($child.StandardOutput -split '\r?\n' | Where-Object { $_.StartsWith('{', [StringComparison]::Ordinal) })[-1]
                $observed = $line | ConvertFrom-Json
                $row.samples.Add([ordered]@{ pair = $pair; order = $order -join ','; variant = $variant; processMilliseconds = $child.DurationMilliseconds; powerShell = $observed.powerShell; rows = @($observed.rows) })
                Write-Utf8Json -Path $EvidencePath -Value $report
            }
        }
        foreach ($variant in $inputs.Keys) {
            Assert-CorpusTimingIdentity -Entry $entry -Build $resolved[$variant].build -WorkspacePath $inputs[$variant].workspace -ArtifactRoot $resolved[$variant].root
            foreach ($state in @('cold-module', 'warm-module')) {
                $values = @($row.samples | Where-Object variant -eq $variant | ForEach-Object rows | Where-Object state -eq $state)
                $summary = [ordered]@{ samples = $values.Count }
                foreach ($metric in @('importMilliseconds', 'invocationMilliseconds')) {
                    if ($state -eq 'warm-module' -and $metric -eq 'importMilliseconds') { continue }
                    $sorted = @($values.$metric | Sort-Object)
                    $summary[$metric] = @{ p50 = $sorted[[int][Math]::Ceiling($sorted.Count*0.5)-1]; p95 = $sorted[[int][Math]::Ceiling($sorted.Count*0.95)-1] }
                }
                $row.summary["$variant/$state"] = $summary
            }
        }
        $exports = @($row.samples | ForEach-Object rows | ForEach-Object exportedCommands | Sort-Object -Unique)
        if ($exports.Count -ne 1) { throw "Revision export counts differ: $id" }
        $row.succeeded = $true
    }
    $report.completed = $true
} finally {
    Write-Utf8Json -Path $EvidencePath -Value $report
}
